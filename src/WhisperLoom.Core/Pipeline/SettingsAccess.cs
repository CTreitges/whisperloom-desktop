using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Pipeline;

/// <summary>Die vier Keys eines Auftrags aus dem <see cref="ISecretStore"/>; "" = keiner. <see cref="ToString"/> ohne Werte.</summary>
/// <param name="Providers">W2: Keys der eigenen Anbieter, Name = Anbieter-ID (<see cref="SecretNames.ForProvider"/>); null = keine.</param>
public sealed record AccessKeys(string Stt = "", string SttFallback = "", string Llm = "", string LlmFallback = "",
    IReadOnlyDictionary<string, string>? Providers = null)
{
    public override string ToString() => "AccessKeys(…)";
}

/// <summary>
/// Liest Stufe und Zugänge aus den Einstellungen (Port der Zugangs- und Stufen-Getter von <c>Prefs.kt</c>:
/// <c>sttAccess</c>, <c>llmAccess</c>, <c>sttFallbackAccess</c>, <c>llmFallbackAccess</c>, <c>refinementFor</c>).
/// </summary>
public static class SettingsAccess
{
    /// <summary>Altwert der früheren Stufe „Absätze“ — gilt als Glätten.</summary>
    private const string LegacyParagraphs = "paragraphs";

    public static Engine? EngineOf(WhisperLoomSettings s) => Engines.FromKey(s.Engine);

    /// <summary>
    /// Die einzige Auflösung der gespeicherten Diktat-Stufe (App und Core, TS-WSTUFEN): "paragraphs" → Glätten,
    /// „Prompt“ nur eingeschaltet (sonst Glätten), „readable“ → Glätten (nie wählbar, siehe <see cref="RefineMode"/>;
    /// „Lesbar“ entsteht nur über die Bereinigung), Unbekanntes → aus.
    /// </summary>
    public static RefineMode DictationStage(RefineSettings refine)
    {
        var mode = refine.Stage == LegacyParagraphs ? RefineMode.Polish : RefineModes.FromKey(refine.Stage);
        return mode switch
        {
            RefineMode.Prompt when !refine.PromptEnabled => RefineMode.Polish,
            RefineMode.Readable => RefineMode.Polish,
            _ => mode,
        };
    }

    /// <summary>Wirksame Verarbeitung der Diktat-Stufe (<see cref="Refinement.Of"/>, Weg Diktat).</summary>
    public static Refinement DictationRefinement(RefineSettings refine)
    {
        var stage = DictationStage(refine);
        var paragraphs = stage switch
        {
            RefineMode.Polish => refine.ParagraphsPolish,
            RefineMode.Beautify => refine.ParagraphsBeautify,
            _ => true,
        };
        return Refinement.Of(RefineWay.Dictation, stage, RefineKeys.CleanupFromKey(refine.PolishCleanup), paragraphs,
            RefineKeys.FormFromKey(refine.SummarizeForm));
    }

    /// <summary>Die wirksame Verarbeitung des Wegs (<see cref="Refinement.Of"/>).</summary>
    public static Refinement RefinementFor(WhisperLoomSettings s, RefineWay way)
    {
        if (way == RefineWay.Dictation) return DictationRefinement(s.Refine);
        var shareStage = RefineModes.FromKey(s.Share.Stage);
        if (!RefineModes.Settings.Contains(shareStage)) shareStage = RefineMode.Off;
        return Refinement.Of(way, shareStage, RefineKeys.CleanupFromKey(s.Share.PolishCleanup), true, RefineKeys.FormFromKey(s.Share.SummarizeForm));
    }

    /// <summary>Eigenes Modell der Stufe von <paramref name="mode"/> („Lesbar“ = Glätten); "" = Standard.</summary>
    public static string LlmModelFor(WhisperLoomSettings s, RefineMode? mode) =>
        mode?.ModelStage() is { } stage ? s.Llm.StageModels.GetValueOrDefault(stage.Key()) ?? "" : "";

    /// <summary>Katalog, eigene Anbieter und lokale Server dieser Einstellungen mit den Keys der Einträge.</summary>
    public static ProviderDirectory Providers(WhisperLoomSettings s, AccessKeys keys) =>
        ProviderDirectory.For(s).WithKeys(keys.Providers ?? new Dictionary<string, string>());

    public static ApiAccess Stt(WhisperLoomSettings s, AccessKeys keys, IServerModelLookup? lookup = null) =>
        AccessResolver.ResolveStt(s.Stt.ProviderId, s.Stt.BaseUrl, keys.Stt, s.Stt.Model, s.Stt.ReadTimeoutSec, lookup, Providers(s, keys));

    /// <summary>Zugang der Textverbesserung; mit <paramref name="mode"/> gilt das Modell dieser Stufe.</summary>
    public static ApiAccess Llm(WhisperLoomSettings s, AccessKeys keys, IServerModelLookup? lookup = null, RefineMode? mode = null) =>
        AccessResolver.ResolveLlm(Stt(s, keys, lookup), s.Llm.ProviderId, s.Llm.BaseUrl, keys.Llm, s.Llm.Model, lookup,
            sttOffline: EngineOf(s) == Engine.Offline, stageModel: LlmModelFor(s, mode), stage: mode, providers: Providers(s, keys));

    /// <summary>Fallback der Erkennung; null = Schalter aus. Bereit: <see cref="SetupState.SttFallbackReady"/>.</summary>
    public static ApiAccess? SttFallback(WhisperLoomSettings s, AccessKeys keys, IServerModelLookup? lookup = null) =>
        !s.SttFallback.Enabled
            ? null
            : AccessResolver.ResolveSttFallback(Stt(s, keys, lookup), s.SttFallback.ProviderId, s.SttFallback.BaseUrl, keys.SttFallback,
                s.SttFallback.Model, lookup, Providers(s, keys));

    /// <summary>Fallback der Textverbesserung für die Stufe von <paramref name="mode"/>; null = Schalter aus.</summary>
    public static ApiAccess? LlmFallback(WhisperLoomSettings s, AccessKeys keys, IServerModelLookup? lookup = null, RefineMode? mode = null) =>
        !s.LlmFallback.Enabled
            ? null
            : AccessResolver.ResolveLlmFallback(Stt(s, keys, lookup), Llm(s, keys, lookup, mode), s.LlmFallback.ProviderId,
                s.LlmFallback.BaseUrl, keys.LlmFallback, s.LlmFallback.Model, lookup, EngineOf(s) == Engine.Offline, mode, Providers(s, keys));

    /// <summary>Die vier Zugangs-Keys; mit <paramref name="settings"/> auch die Keys der eigenen Anbieter.</summary>
    public static async Task<AccessKeys> KeysAsync(ISecretStore secrets, CancellationToken ct, WhisperLoomSettings? settings = null)
    {
        var providers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in settings?.Providers.Custom ?? [])
        {
            var name = SecretNames.ForProvider(ProvidersSettings.CustomPrefix + entry.Id);
            if (SecretNames.IsProviderKey(name) && await secrets.GetAsync(name, ct).ConfigureAwait(false) is { Length: > 0 } key)
            {
                providers[name] = key;
            }
        }
        // W2-ENR: Key des Firmen-Proxys (Geräte-Token) nur für dessen Eintrag; der Speicher gibt ihn nur im Zustand approved.
        if (settings is not null && CompanyProvider.IsActive(settings.Management)
            && await secrets.GetAsync(CompanyProvider.EntryId, ct).ConfigureAwait(false) is { Length: > 0 } token)
        {
            providers[CompanyProvider.EntryId] = token;
        }
        return new(
            await secrets.GetAsync(SecretNames.Stt, ct).ConfigureAwait(false) ?? "",
            await secrets.GetAsync(SecretNames.SttFallback, ct).ConfigureAwait(false) ?? "",
            await secrets.GetAsync(SecretNames.Llm, ct).ConfigureAwait(false) ?? "",
            await secrets.GetAsync(SecretNames.LlmFallback, ct).ConfigureAwait(false) ?? "",
            providers);
    }
}
