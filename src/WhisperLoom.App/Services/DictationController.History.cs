using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.History;

namespace WhisperLoom.App.Services;

/// <summary>Ergebnis von „Neu verarbeiten“ im Verlauf (Android <c>HistoryJobs.Finished</c>).</summary>
/// <param name="Code">"saved", "skipped" (ohne KI, nichts gespeichert), "kept" (Fassung wurde inzwischen bearbeitet – bleibt),
/// "gone" (Eintrag weg), "stage-locked", "error".</param>
public sealed record ReprocessOutcome(string Code, HistoryEntry? Entry = null, string? Reason = null);

/// <summary>
/// Verlauf mit Fassungen (W2-OUT): Neu verarbeiten im Hintergrund als zusätzliche Fassung desselben Eintrags (Port von
/// <c>History.reprocess</c>), Bearbeiten, Kürzen. Der Rohtext (Ursprung) wird nie überschrieben; ohne Ausgabe ins Ziel und
/// ohne den Diktat-Zustand zu ändern – ein Diktat kann parallel laufen.
/// </summary>
public sealed partial class DictationController
{
    /// <summary>Anzahl der Einträge (Rückfrage beim Verkleinern); 0 bei ausgeschaltetem Verlauf.</summary>
    /// <exception cref="IOException">Verlauf nicht lesbar.</exception>
    public async Task<int> HistoryCountAsync() => (await HistoryIfEnabledAsync()).Count;

    /// <summary>Nach dem Verkleinern sofort kürzen (sonst erst beim nächsten Diktat).</summary>
    public async Task TrimHistoryAsync()
    {
        if (_history is not FileHistoryStore store)
        {
            return;
        }
        try
        {
            var removed = await store.TrimAsync(CancellationToken.None);
            if (removed > 0)
            {
                _log.Event("history.trimmed", ("count", removed));
                HistoryChanged?.Invoke();
            }
        }
        catch (IOException)
        {
            _log.Event("history.trimmed", ("status", "error"));
        }
    }

    /// <summary>
    /// Fassung <paramref name="stage"/> aus dem Rohtext neu rechnen und im Eintrag speichern (ersetzt eine vorhandene Fassung
    /// am Platz). Nur eine Fassung mit KI wird gespeichert (bei „Aus“ die Regeln-Fassung). Ersetzt wird nur, was beim Start da
    /// war: wurde die Fassung während der Rechnung bearbeitet, bleibt die Bearbeitung ("kept").
    /// </summary>
    /// <exception cref="ArgumentException">Unbekannte oder nicht wählbare Stufe.</exception>
    public async Task<ReprocessOutcome> ReprocessVersionAsync(string id, string stage, CancellationToken ct = default)
    {
        var target = RefineModes.FromKey(stage);
        if (target.Key() != stage || !RefineModes.SettingsFor(Settings.Refine.PromptEnabled).Contains(target))
        {
            throw new ArgumentException($"Stufe nicht wählbar: {stage}", nameof(stage));
        }
        if (StageLocked && target != Stage)
        {
            _log.Event("stage.locked", ("reprocess", true));
            return new ReprocessOutcome("stage-locked");
        }
        var entry = await TryGetAsync(id);
        if (entry is null)
        {
            return new ReprocessOutcome("gone");
        }
        var before = HistoryVersions.Find(entry, stage);
        RefinedText refined;
        try
        {
            refined = await _pipeline.RefineAsync(entry.Raw, entry.Language, RefinementOf(WithStage(Settings.Refine, stage)), ct);
        }
        catch (Exception e) when (e is ApiHttpException or ApiNetworkException or ApiNotConfiguredException or LocalNotAvailableException
                                      or IOException)
        {
            _log.Event("reprocess", ("status", "error"), ("errorClass", e.GetType().Name), ("background", true));
            return new ReprocessOutcome("error", Reason: ErrorKey(e));
        }
        if (target != RefineMode.Off && !refined.Refined)
        {
            _log.Event("reprocess", ("status", "skipped"), ("stage", stage), ("background", true));
            return new ReprocessOutcome("skipped", Reason: refined.Skipped);
        }
        var current = await TryGetAsync(id);
        if (current is null)
        {
            return new ReprocessOutcome("gone");
        }
        if (HistoryVersions.Find(current, stage) != before)
        {
            return new ReprocessOutcome("kept", current);
        }
        var updated = HistoryVersions.With(current, new HistoryVersion(stage, refined.Text, _now(), refined.Model, refined.Skipped));
        await SaveEntryAsync(updated);
        _log.Event("reprocess", ("status", "ok"), ("stage", stage), ("model", refined.Model), ("background", true));
        return new ReprocessOutcome("saved", updated);
    }

    /// <summary>Bearbeiten: Fassung <paramref name="processing"/> (bzw. <see cref="HistoryVersions.Edited"/> für den Ursprung) ersetzen.</summary>
    /// <returns>Der geänderte Eintrag; null = nicht mehr da.</returns>
    public async Task<HistoryEntry?> EditHistoryAsync(string id, string processing, string text)
    {
        var entry = await TryGetAsync(id);
        if (entry is null)
        {
            return null;
        }
        var updated = HistoryVersions.Edit(entry, processing, text, _now());
        await SaveEntryAsync(updated);
        _log.Event("history.edited", ("chars", text.Length));
        return updated;
    }

    private async Task<HistoryEntry?> TryGetAsync(string id)
    {
        try
        {
            return Settings.History.Enabled ? await _history.GetAsync(id, CancellationToken.None) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Schreibt den Eintrag unter seiner Id (überschreibt); Tray und „erneut einfügen“ sehen danach die neue Fassung.</summary>
    private async Task SaveEntryAsync(HistoryEntry entry)
    {
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

    /// <summary>Neuer Verlaufseintrag für ein ausgegebenes Diktat (je Diktat einer, W3-NEXT: auch im Hintergrund).</summary>
    private async Task AddHistoryAsync(DictationResult result)
    {
        if (result.Raw.Length == 0)
        {
            return;
        }
        var now = _now();
        var entry = new HistoryEntry(
            FileHistoryStore.NewId(now),
            now,
            result.Language,
            result.DurationMs,
            result.Raw,
            result.Text,
            result.Result.Refinement,
            result.SttLabel,
            result.Result.Model,
            result.Result.Skipped);
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
