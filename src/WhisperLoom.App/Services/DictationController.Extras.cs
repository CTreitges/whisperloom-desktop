using System.Diagnostics;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Reinsert;
using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Services;

/// <summary>
/// Windows-Extras des Controllers (WLD-P5b): Stufe des App-Profils, Verlauf (letzte Einträge, neu verarbeiten) und
/// Markierung umschreiben (Aufnahme = Anweisung).
/// </summary>
public sealed partial class DictationController
{
    /// <summary>Profil des Zielfensters für das laufende Diktat per Tastenkürzel; null = Standard. Endet mit dem Diktat.</summary>
    public ProfileMatch? ActiveProfile { get; set; }

    /// <summary>Umschreiben der Markierung; null = nicht verfügbar.</summary>
    public ISelectionRewriter? Rewriter { get; set; }

    /// <summary>Ein Eintrag kam in den Verlauf (Tray-Menü neu aufbauen).</summary>
    public event Action? HistoryChanged;

    /// <summary>Stufe des laufenden Diktats: die des aktiven Profils, sonst die Standard-Stufe.</summary>
    public RefineMode EffectiveStage => ActiveProfile is { } m ? ProfileStage(m.Profile) : Stage;

    /// <summary>Stufe des Profils; "" oder eine von IT gesperrte Stufe → Standard-Stufe.</summary>
    public RefineMode ProfileStage(AppProfile profile) =>
        StageLocked || profile.Stage.Length == 0 ? Stage : StageOf(WithStage(Settings.Refine, profile.Stage));

    /// <summary>Stufen-Hotkey bei aktivem Profil: nächste Stufe des Profils (gespeichert in <c>desktop.profiles</c>).</summary>
    public RefineMode NextProfileStage(int index)
    {
        var desktop = SettingsStore.Desktop(Settings);
        if (index < 0 || index >= desktop.Profiles.Count)
        {
            return Stage;
        }
        var profile = desktop.Profiles[index];
        if (StageLocked)
        {
            _log.Event("stage.locked", ("profile", true));
            return ProfileStage(profile);
        }
        var choices = RefineModes.SettingsFor(Settings.Refine.PromptEnabled);
        var target = choices[(choices.ToList().IndexOf(ProfileStage(profile)) + 1) % choices.Count];
        profile.Stage = target.Key();
        SettingsStore.SetDesktop(Settings, desktop);
        _saveSettings(Settings);
        if (ActiveProfile?.Index == index)
        {
            ActiveProfile = new ProfileMatch(index, profile);
        }
        _log.Event("stage.set", ("stage", target.Key()), ("profile", true));
        return target;
    }

    /// <summary>Die letzten Einträge fürs Tray (<see cref="RecentHistory"/>).</summary>
    /// <exception cref="IOException">Verlauf nicht lesbar.</exception>
    public async Task<IReadOnlyList<HistoryEntry>> RecentAsync() =>
        RecentHistory.Take(await _history.ListAsync(CancellationToken.None));

    /// <summary>Text fürs erneute Einfügen: letztes Ergebnis dieser Sitzung, sonst der neueste Verlaufseintrag.</summary>
    public async Task<string?> LatestTextAsync()
    {
        if (LastResult.Length > 0)
        {
            return LastResult;
        }
        try
        {
            var history = await _history.ListAsync(CancellationToken.None);
            return history.Count > 0 ? history[0].Text : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Neu verarbeiten aus dem Rohtext eines Verlaufseintrags (neuester oder <paramref name="id"/>) mit <paramref name="stage"/>,
    /// ohne Audio. Ergebnis wie ein Diktat: Ausgabe, neuer Verlaufseintrag.
    /// </summary>
    /// <returns>"done", "skipped" (ohne KI), "no-entry", "busy" oder "error".</returns>
    /// <exception cref="ArgumentException">Unbekannte oder nicht wählbare Stufe.</exception>
    public async Task<string> ReprocessAsync(string stage, string? id = null)
    {
        var target = RefineModes.FromKey(stage);
        if (target.Key() != stage || !RefineModes.SettingsFor(Settings.Refine.PromptEnabled).Contains(target))
        {
            throw new ArgumentException($"Stufe nicht wählbar: {stage}", nameof(stage));
        }
        if (State != DictationState.Idle)
        {
            return "busy";
        }
        HistoryEntry? entry;
        try
        {
            entry = id is null ? (await _history.ListAsync(CancellationToken.None)).FirstOrDefault() : await _history.GetAsync(id, CancellationToken.None);
        }
        catch (IOException)
        {
            entry = null;
        }
        if (entry is null)
        {
            return "no-entry";
        }
        LastErrorKey = null;
        LastErrorClass = null;
        State = DictationState.Processing;
        var watch = Stopwatch.StartNew();
        try
        {
            var refined = await _pipeline.RefineAsync(entry.Raw, entry.Language, RefinementOf(WithStage(Settings.Refine, stage)), CancellationToken.None);
            LastRaw = entry.Raw;
            LastResult = refined.Text;
            LastRefined = refined;
            try
            {
                await _output.WriteAsync(refined.Text, CancellationToken.None);
            }
            catch (IOException)
            {
                Fail("Error_Output", "OutputFailed");
            }
            await AddHistoryAsync(new DictationResult(entry.Raw, entry.Language, entry.DurationMs, refined, entry.SttLabel));
            _log.Event("reprocess", ("status", "ok"), ("stage", stage), ("model", refined.Model), ("refineSkipped", refined.Skipped is not null),
                ("processingMs", watch.ElapsedMilliseconds), ("output", _output.Kind));
            return LastErrorClass is not null ? "error" : refined.Skipped is null ? "done" : "skipped";
        }
        finally
        {
            State = DictationState.Idle;
        }
    }

    /// <summary>
    /// Beendet die Aufnahme der Anweisung, schreibt <paramref name="selection"/> danach um und gibt das Ergebnis aus (ins
    /// gemerkte Ziel, wo es die Markierung ersetzt). Kein Verlaufseintrag: die Markierung stammt aus fremden Dokumenten.
    /// </summary>
    /// <returns>"done" oder "error" (<see cref="LastErrorKey"/>; dann wurde nichts ausgegeben).</returns>
    public async Task<string> StopAndRewriteAsync(string selection)
    {
        if (!IsRecording)
        {
            return State == DictationState.Processing ? "busy" : "not-recording";
        }
        if (State == DictationState.Recording)
        {
            _recorded += _now() - _segmentStart;
        }
        var audio = _recorder.Stop();
        State = DictationState.Processing;
        _processing = new CancellationTokenSource();
        var ct = _processing.Token;
        var watch = Stopwatch.StartNew();
        try
        {
            // Die Anweisung: nur Erkennung und Regeln, keine Stufe.
            var spoken = await _pipeline.RunAsync(new DictationRequest(audio, Settings.Language, Refinement.Off), ct);
            var instruction = spoken.Text.Trim();
            if (instruction.Length == 0)
            {
                Fail("Rewrite_NoInstruction", "EmptyInstruction");
                _log.Event("rewrite", ("status", "error"), ("errorClass", "EmptyInstruction"));
                return "error";
            }
            var rewriter = Rewriter ?? throw new ApiNotConfiguredException();
            var refined = await rewriter.RewriteAsync(selection, instruction, spoken.Language, ct);
            if (refined.Model is null)
            {
                // Ohne KI kein Ersetzen: die Markierung bliebe sonst stehen, als wäre sie umgeschrieben.
                Fail("Rewrite_Failed", "RewriteSkipped");
                _log.Event("rewrite", ("status", "error"), ("errorClass", "RewriteSkipped"), ("processingMs", watch.ElapsedMilliseconds));
                return "error";
            }
            LastRaw = instruction;
            LastResult = refined.Text;
            LastRefined = refined;
            try
            {
                await _output.WriteAsync(refined.Text, CancellationToken.None);
            }
            catch (IOException)
            {
                Fail("Error_Output", "OutputFailed");
            }
            _log.Event("rewrite", ("status", "ok"), ("durationMs", spoken.DurationMs), ("processingMs", watch.ElapsedMilliseconds),
                ("stt", spoken.SttLabel), ("model", refined.Model), ("selectionChars", selection.Length), ("output", _output.Kind));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log.Event("rewrite", ("status", "cancelled"));
            return "cancelled";
        }
        catch (Exception e) when (e is ApiHttpException or ApiNetworkException or ApiNotConfiguredException or LocalNotAvailableException
                                      or IOException)
        {
            Fail(ErrorKey(e), e.GetType().Name);
            _log.Event("rewrite", ("status", "error"), ("errorClass", e.GetType().Name), ("httpStatus", (e as ApiHttpException)?.StatusCode));
        }
        finally
        {
            _processing?.Dispose();
            _processing = null;
            _recorded = TimeSpan.Zero;
            ActiveProfile = null;
            State = DictationState.Idle;
        }
        return LastErrorClass is null ? "done" : "error";
    }

    /// <summary>Stufen-Einstellungen mit der Stufe des Profils (sonst unverändert).</summary>
    private RefineSettings EffectiveRefine() =>
        ActiveProfile is { Profile.Stage.Length: > 0 } m && !StageLocked ? WithStage(Settings.Refine, m.Profile.Stage) : Settings.Refine;

    private static RefineSettings WithStage(RefineSettings refine, string stage) => new()
    {
        Stage = stage,
        PromptEnabled = refine.PromptEnabled,
        PolishCleanup = refine.PolishCleanup,
        SummarizeForm = refine.SummarizeForm,
        ParagraphsPolish = refine.ParagraphsPolish,
        ParagraphsBeautify = refine.ParagraphsBeautify,
    };

    /// <summary>Lokalisierungs-Schlüssel eines Fehlers der Pipeline (wie im Diktat).</summary>
    private static string ErrorKey(Exception e) => e switch
    {
        ApiNotConfiguredException => "Error_NotConfigured",
        ApiHttpException => "Error_Http",
        ApiNetworkException => "Error_Network",
        IOException => "Key_StoreError",
        _ => "Error_Local",
    };
}
