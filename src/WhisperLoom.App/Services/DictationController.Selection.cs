using System.Diagnostics;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.History;
using WhisperLoom.App.Output;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Services;

/// <summary>
/// „Markierung verarbeiten“ (W2-SEL): eine markierte Textstelle mit einer gewählten Stufe neu verarbeiten (wie „Neu verarbeiten“
/// im Verlauf, nur ohne Audio) und das Ergebnis ausgeben – unter Windows ins gemerkte Ziel, wo es die Markierung ersetzt, sonst in
/// die Zwischenablage. Der Eintrag kommt mit Quelle „Markierung“ in den Verlauf.
/// </summary>
public sealed partial class DictationController
{
    /// <summary>Stufen im Stufen-Wähler der Markierung: alle wählbaren außer „Aus“; mit IT-Sperre nur die gesperrte (bei „Aus“ keine).</summary>
    public IReadOnlyList<RefineMode> SelectionStages()
    {
        if (StageLocked)
        {
            return Stage == RefineMode.Off ? [] : [Stage];
        }
        return [.. RefineModes.SettingsFor(Settings.Refine.PromptEnabled).Where(m => m != RefineMode.Off)];
    }

    /// <summary>
    /// Verarbeitet <paramref name="selection"/> mit <paramref name="stage"/> und gibt das Ergebnis aus. Ohne KI-Ergebnis wird
    /// nichts ausgegeben (die Markierung bliebe sonst stehen, als wäre sie verarbeitet). Leerraum am Rand der Markierung bleibt
    /// (dreifach geklickte Zeilen enden auf einem Zeilenumbruch).
    /// </summary>
    /// <returns>"done", "error" (<see cref="LastErrorKey"/>), "cancelled" oder "busy".</returns>
    /// <exception cref="ArgumentException">Stufe nicht in <see cref="SelectionStages"/>.</exception>
    public async Task<string> ProcessSelectionAsync(string selection, string stage)
    {
        var mode = RefineModes.FromKey(stage);
        if (mode.Key() != stage || !SelectionStages().Contains(mode))
        {
            throw new ArgumentException($"Stufe nicht wählbar: {stage}", nameof(stage));
        }
        if (State != DictationState.Idle)
        {
            return "busy";
        }
        LastErrorKey = null;
        LastErrorClass = null;
        State = DictationState.Processing;
        _processing = new CancellationTokenSource();
        var ct = _processing.Token;
        var watch = Stopwatch.StartNew();
        try
        {
            var language = DictationPipeline.EffectiveLanguage(Settings.Language, null);
            var refinement = RefinementOf(WithStage(Settings.Refine, stage));
            // Ohne Randleerraum ans Modell; er kommt danach wieder dazu (KeepEdges).
            var refined = await _pipeline.RefineAsync(selection.Trim(), language, refinement, ct);
            if (!refined.Refined)
            {
                Fail("Selection_Failed", "RefineSkipped");
                _log.Event("selection", ("status", "error"), ("errorClass", "RefineSkipped"), ("stage", stage));
                return "error";
            }
            var text = KeepEdges(selection, refined.Text);
            try
            {
                await _output.WriteAsync(text, CancellationToken.None);
            }
            catch (IOException)
            {
                Fail("Error_Output", "OutputFailed");
            }
            // Passwortfeld (UI Automation): nichts merken.
            if (_output is not IPrivateAwareOutput { LastWasPrivate: true })
            {
                await AddSelectionHistoryAsync(selection, text, language, refined);
            }
            _log.Event("selection", ("status", LastErrorClass is null ? "ok" : "error"), ("stage", stage), ("model", refined.Model),
                ("selectionChars", selection.Length), ("processingMs", watch.ElapsedMilliseconds), ("output", _output.Kind));
            return LastErrorClass is null ? "done" : "error";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log.Event("selection", ("status", "cancelled"));
            return "cancelled";
        }
        catch (Exception e) when (e is ApiHttpException or ApiNetworkException or ApiNotConfiguredException or LocalNotAvailableException
                                      or IOException)
        {
            Fail(ErrorKey(e), ErrorClass(e));
            _log.Event("selection", ("status", "error"), ("errorClass", e.GetType().Name), ("httpStatus", (e as ApiHttpException)?.StatusCode));
            return "error";
        }
        finally
        {
            _processing?.Dispose();
            _processing = null;
            State = DictationState.Idle;
        }
    }

    /// <summary>Leerraum am Anfang und Ende der Markierung um das Ergebnis legen.</summary>
    public static string KeepEdges(string selection, string result)
    {
        var lead = selection[..(selection.Length - selection.TrimStart().Length)];
        var trail = selection[selection.TrimEnd().Length..];
        return selection.Trim().Length == 0 ? result : lead + result.Trim() + trail;
    }

    private async Task AddSelectionHistoryAsync(string selection, string text, string language, RefinedText refined)
    {
        var now = _now();
        var entry = new HistoryEntry(FileHistoryStore.NewId(now), now, language, 0, selection, text, refined.Refinement, "",
            refined.Model, refined.Skipped, Source: HistoryEntry.SourceSelection);
        try
        {
            await _history.AddAsync(entry, CancellationToken.None);
            HistoryChanged?.Invoke();
        }
        catch (IOException)
        {
            _log.Event("history", ("status", "error"));
        }
    }
}
