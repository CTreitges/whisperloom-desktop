using System.Net.NetworkInformation;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Pipeline;

/// <summary>Ist Netz für den Zugang an <paramref name="baseUrl"/> da? <paramref name="proven"/>: eine Anfrage hat es gerade getragen.</summary>
public delegate bool NetworkCheck(string baseUrl, bool proven);

/// <summary>Austauschbare Teile der Pipeline. Ohne Angabe: kein lokaler Teil, Merkliste nur im Speicher, nichts getrimmt.</summary>
public sealed class DictationPipelineOptions
{
    /// <summary>Lokale Erkennung (whisper.cpp-Begleitprozess, P4).</summary>
    public ILocalStt? LocalStt { get; init; }

    /// <summary>Lokale Textverbesserung (Ollama, P4). Fehlt sie, wirkt jede Offline-Regel wie „Überspringen“.</summary>
    public ILocalRefiner? LocalRefiner { get; init; }

    /// <summary>Modell-Listen und Merkliste „ohne temperature“ (mit Datei im Datenordner).</summary>
    public ModelCache? ModelCache { get; init; }

    /// <summary>Standard: bewiesen, Loopback oder eine aktive Netzwerkschnittstelle.</summary>
    public NetworkCheck? NetworkCheck { get; init; }

    /// <summary>Stille am Rand schneiden (WLD-P1a: <c>AudioUtils.trimSilence</c>; verdrahtet WLD-INT).</summary>
    public Func<AudioBuffer, AudioBuffer>? TrimSilence { get; init; }

    /// <summary>Kontext-Wörter für die Erkennung, bei jedem Diktat neu (WLD-P1a: <c>Vocabulary</c>; verdrahtet WLD-INT).</summary>
    public Func<WhisperLoomSettings, string>? VocabularyPrompt { get; init; }
}

/// <summary>
/// Der ganze Weg eines Diktats (Port von <c>TranscriptionEngine</c> ohne Android-Context): Stille trimmen → Erkennung (mit
/// Fallback) → Verbesserung (<see cref="RefinePlan"/>: online → Fallback-Zugang → lokal → Rohtext) → Regeln
/// (<see cref="ITextPolisher"/>). Die Einstellungen und Keys gelten je Auftrag frisch.
/// </summary>
public sealed class DictationPipeline(
    Func<WhisperLoomSettings> settings,
    ISecretStore secrets,
    HttpClient http,
    IRefinePromptBuilder prompts,
    ITextPolisher polisher,
    DictationPipelineOptions? options = null) : IDictationPipeline
{
    private readonly DictationPipelineOptions _options = options ?? new DictationPipelineOptions();
    private readonly ModelCache _cache = options?.ModelCache ?? new ModelCache();
    private readonly TextCleanup _cleanup = TextCleanup.For(polisher);

    public Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct) => RunAsync(request, null, null, ct);

    /// <param name="skip">Ausweg während der Verbesserung: liefert sofort den Text ohne KI (ohne Hinweis).</param>
    /// <param name="onRefineStart">Die Verbesserung beginnt (online oder lokal) — ab jetzt hilft <paramref name="skip"/>.</param>
    public async Task<DictationResult> RunAsync(DictationRequest request, RefineSkip? skip, Action? onRefineStart, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var s = settings();
        var keys = await SettingsAccess.KeysAsync(secrets, ct).ConfigureAwait(false);
        await RequireConfiguredAsync(s, keys, ct).ConfigureAwait(false);
        var backend = Backend(s, keys);
        var result = await backend.TranscribeAsync(Trim(request.Audio), request.Language, ct).ConfigureAwait(false);
        var sttNote = SttFallbackNote(backend);
        var usedFallback = backend is FallbackBackend { UsedFallback: true };
        var language = EffectiveLanguage(request.Language, result.DetectedLanguage);
        var duration = request.Audio.DurationMs;
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return new DictationResult("", language, duration, new RefinedText(request.Refinement, "", Note: sttNote), backend.Label, usedFallback);
        }
        // Die Online-Erkennung hat das Netz gerade bewiesen.
        var refined = await RefineCoreAsync(s, keys, result.Text, language, request.Refinement,
            SettingsAccess.EngineOf(s) != Engine.Offline, skip, onRefineStart, ct).ConfigureAwait(false);
        var note = string.Join(" · ", new[] { sttNote, refined.Note }.OfType<string>());
        return new DictationResult(result.Text, language, duration, refined with { Note = note.Length == 0 ? null : note }, backend.Label, usedFallback);
    }

    /// <summary>Neu verarbeiten aus dem Rohtext (Verlauf), ohne Netz-Nachweis. Wirft nur bei Abbruch.</summary>
    public async Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(refinement);
        var s = settings();
        var keys = await SettingsAccess.KeysAsync(secrets, ct).ConfigureAwait(false);
        return await RefineCoreAsync(s, keys, raw, language, refinement, false, null, null, ct).ConfigureAwait(false);
    }

    /// <summary>Stufe der Sprachnachrichten (<see cref="RefineWay.Share"/>) über mehrere Stücke (<see cref="SharedRefine"/>).</summary>
    /// <param name="onStart">Vor der ersten Rechnung; true = das lokale Textmodell rechnet.</param>
    public async Task<SharedRefine.Result> RefineSharedAsync(
        IReadOnlyList<string> parts,
        string language,
        Func<bool>? isCancelled,
        Action<bool>? onStart,
        CancellationToken ct)
    {
        var s = settings();
        var refinement = SettingsAccess.RefinementFor(s, RefineWay.Share);
        if (refinement.Mode == RefineMode.Off) return new SharedRefine.Result(RefineMode.Off, null, null);
        var keys = await SettingsAccess.KeysAsync(secrets, ct).ConfigureAwait(false);
        var plan = await PlanAsync(s, keys, refinement.Mode, SettingsAccess.EngineOf(s) != Engine.Offline, ct).ConfigureAwait(false);
        if (plan.Route is RefineRoute.Raw raw)
        {
            // „Überspringen“ ohne eigenen Zugang gilt wie „Aus“: so gewollt, kein Hinweis.
            return raw.Hint is { } hint
                ? new SharedRefine.Result(refinement.Mode, null, RefinePlan.Message(hint))
                : new SharedRefine.Result(RefineMode.Off, null, null);
        }
        var localFallback = false;
        var result = await SharedRefine.RunAsync(
            refinement,
            parts,
            PolishRules.For(s.Polish, language, refinement.Mode, refinement.Paragraphs),
            polisher,
            (part, token) => plan.RefineAsync(part, language, refinement, n => localFallback |= n == RefinePlan.MsgOnlineFailedLocal, token),
            isCancelled ?? (() => false),
            () => onStart?.Invoke(plan.Route is RefineRoute.Local),
            ct).ConfigureAwait(false);
        return result.Paragraphs is not null ? result with { LocalFallback = localFallback } : result;
    }

    /// <summary>
    /// Aufnahmestart: wird gleich lokal verbessert, kann die App das Textmodell schon laden (Port der Entscheidung von
    /// <c>TranscriptionEngine.warmUp</c>). <paramref name="mode"/> = feste Stufe; null = Stufe des Diktats.
    /// </summary>
    public async Task<bool> ShouldWarmUpLocalAsync(RefineMode? mode, CancellationToken ct)
    {
        var s = settings();
        if (SettingsAccess.EngineOf(s) != Engine.Offline) return false;
        var keys = await SettingsAccess.KeysAsync(secrets, ct).ConfigureAwait(false);
        var stage = mode ?? SettingsAccess.RefinementFor(s, RefineWay.Dictation).Mode;
        return (await PlanAsync(s, keys, stage, false, ct).ConfigureAwait(false)).Route is RefineRoute.Local;
    }

    /// <summary>Ob überhaupt diktiert werden kann (Zugang vollständig bzw. lokale Erkennung bereit).</summary>
    public async Task<bool> IsConfiguredAsync(CancellationToken ct)
    {
        var s = settings();
        var keys = await SettingsAccess.KeysAsync(secrets, ct).ConfigureAwait(false);
        return SettingsAccess.EngineOf(s) switch
        {
            Engine.Online => SttComplete(SettingsAccess.Stt(s, keys, _cache)),
            Engine.Offline => _options.LocalStt is { } local && await local.IsReadyAsync(ct).ConfigureAwait(false),
            _ => false,
        };
    }

    /// <summary>Plan der Textverbesserung für <paramref name="mode"/> mit den aktuellen Einstellungen.</summary>
    public async Task<RefinePlan> PlanAsync(RefineMode mode, bool networkProven, CancellationToken ct)
    {
        var s = settings();
        var keys = await SettingsAccess.KeysAsync(secrets, ct).ConfigureAwait(false);
        return await PlanAsync(s, keys, mode, networkProven, ct).ConfigureAwait(false);
    }

    /// <summary>Womit erkannt wird (ohne Prüfung, ob der Zugang vollständig ist).</summary>
    public async Task<ITranscriptionBackend> BackendAsync(CancellationToken ct) =>
        Backend(settings(), await SettingsAccess.KeysAsync(secrets, ct).ConfigureAwait(false));

    /// <summary>Bei "auto" die vom Erkenner gemeldete Sprache — sonst bleibt "auto".</summary>
    public static string EffectiveLanguage(string configured, string? detected) =>
        configured == "auto" && !string.IsNullOrWhiteSpace(detected) ? detected : configured;

    /// <summary>Nur zur Info: der erste Anbieter ist gescheitert, der Fallback hat erkannt.</summary>
    public static string SttFallbackMessage(string label) => $"Spracherkennung fehlgeschlagen — mit Fallback {label} erkannt";

    public static string? SttFallbackNote(ITranscriptionBackend backend) =>
        backend is FallbackBackend { UsedFallback: true } f ? SttFallbackMessage(f.FallbackLabel) : null;

    /// <summary>
    /// KI (falls möglich) und Regeln. Die Verbesserung darf einen erkannten Text nie verschlucken: scheitert sie, kommt der
    /// Rohtext mit den vollen Regeln und Hinweis (<see cref="RefinedText.Skipped"/>). Ohne Netz geht keine Anfrage raus.
    /// </summary>
    private async Task<RefinedText> RefineCoreAsync(
        WhisperLoomSettings s,
        AccessKeys keys,
        string raw,
        string language,
        Refinement refinement,
        bool networkProven,
        RefineSkip? skip,
        Action? onStart,
        CancellationToken ct)
    {
        var plan = await PlanAsync(s, keys, refinement.Mode, networkProven, ct).ConfigureAwait(false);
        string? ai = null;
        string? skipped = null;
        string? note = null;
        if (plan.Route is RefineRoute.Raw r)
        {
            skipped = r.Hint is { } hint ? RefinePlan.Message(hint) : null;
        }
        else
        {
            onStart?.Invoke();
            Task<string> Work(CancellationToken token) => plan.RefineAsync(raw, language, refinement, n => note = n, token);
            try
            {
                ai = skip is null ? await Work(ct).ConfigureAwait(false) : await skip.RaceAsync(Work, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                skipped = string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
            }
        }
        // Ohne KI = Rohtext, den keine KI bearbeitet hat: volle Regeln.
        var options = PolishRules.For(s.Polish, language, ai is null ? RefineMode.Off : refinement.Mode, refinement.Paragraphs);
        var text = polisher.Polish(ai ?? raw, options);
        return new RefinedText(refinement, text, ai is null ? null : plan.ModelLabel(), skipped, ai is null ? null : note);
    }

    /// <summary>
    /// Sammelt die Eingaben der Entscheidungstabelle. Netz nur, wenn es zählt (Online-Erkennung oder eigener Zugang), das
    /// lokale Modell nur bei lokaler Erkennung — bei Stufe „Aus“ beides nicht.
    /// </summary>
    private async Task<RefinePlan> PlanAsync(WhisperLoomSettings s, AccessKeys keys, RefineMode mode, bool networkProven, CancellationToken ct)
    {
        var access = SettingsAccess.Llm(s, keys, _cache, mode);
        var engine = SettingsAccess.EngineOf(s);
        var stageActive = mode != RefineMode.Off;
        var ownOnlineReady = SetupState.LlmReady(access);
        var network = stageActive && (engine != Engine.Offline || ownOnlineReady) && Network(access.BaseUrl, networkProven);
        var local = _options.LocalRefiner;
        var localReady = stageActive && engine == Engine.Offline && local is not null && await local.IsReadyAsync(ct).ConfigureAwait(false);
        var rule = Engines.RuleFromKey(s.Offline.Refine).Effective(textModelFits: local is not null);
        var route = RefineDecision.Route(engine, rule, stageActive, ownOnlineReady, network, localReady);
        // Ein unvollständiger Fallback springt gar nicht erst ein.
        var fallback = SettingsAccess.LlmFallback(s, keys, _cache, mode) is { } f && SetupState.LlmReady(f) ? f : null;
        return new RefinePlan(route, access, fallback, local, new RefineServices(http, prompts, _cleanup, _cache.RememberNoTemperature));
    }

    private async Task RequireConfiguredAsync(WhisperLoomSettings s, AccessKeys keys, CancellationToken ct)
    {
        switch (SettingsAccess.EngineOf(s))
        {
            case Engine.Online when SttComplete(SettingsAccess.Stt(s, keys, _cache)):
                return;
            case Engine.Offline:
                if (_options.LocalStt is { } local && await local.IsReadyAsync(ct).ConfigureAwait(false)) return;
                throw new LocalNotAvailableException("Lokales Erkennungsmodell fehlt oder der Begleitprozess ist nicht bereit");
            default:
                throw new ApiNotConfiguredException();
        }
    }

    private ITranscriptionBackend Backend(WhisperLoomSettings s, AccessKeys keys)
    {
        switch (SettingsAccess.EngineOf(s))
        {
            case Engine.Online:
                var prompt = Vocabulary(s);
                var primary = new OnlineBackend(http, SettingsAccess.Stt(s, keys, _cache), prompt);
                var fallback = SettingsAccess.SttFallback(s, keys, _cache);
                return fallback is not null && SetupState.SttFallbackReady(fallback)
                    ? new FallbackBackend(primary, new OnlineBackend(http, fallback, prompt))
                    : primary;
            case Engine.Offline:
                return _options.LocalStt ?? throw new LocalNotAvailableException("Keine lokale Erkennung eingerichtet");
            default:
                throw new ApiNotConfiguredException();
        }
    }

    private static bool SttComplete(ApiAccess stt) => SetupState.SttComplete(stt.BaseUrl, stt.ApiKey, stt.Provider.NeedsKey);

    /// <summary>Eine fehlende oder gesperrte Vokabel-Datei bricht kein Diktat ab.</summary>
    private string Vocabulary(WhisperLoomSettings s)
    {
        try
        {
            return _options.VocabularyPrompt?.Invoke(s) ?? "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private AudioBuffer Trim(AudioBuffer audio) => _options.TrimSilence?.Invoke(audio) ?? audio;

    private bool Network(string baseUrl, bool proven) => _options.NetworkCheck?.Invoke(baseUrl, proven) ?? DefaultNetwork(baseUrl, proven);

    // Doku: https://learn.microsoft.com/dotnet/api/system.net.networkinformation.networkinterface.getisnetworkavailable (abgerufen 2026-10-30)
    private static bool DefaultNetwork(string baseUrl, bool proven) =>
        proven ||
        (Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) && uri.IsLoopback) ||
        NetworkInterface.GetIsNetworkAvailable();
}
