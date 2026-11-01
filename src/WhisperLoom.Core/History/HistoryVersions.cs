using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.History;

/// <summary>
/// Fassungen eines Verlaufseintrags (W2-OUT), Port von <c>History.setVersion/edit</c> und <c>HistoryEntry.versions</c> (Android).
/// Der Ursprung (<see cref="HistoryEntry.Raw"/>) wird nie überschrieben; wer ihn bearbeitet, bekommt die Fassung
/// <see cref="Edited"/>. Ersetzen behält den Platz der Fassung. <see cref="HistoryEntry.Text"/> folgt der damaligen Fassung,
/// damit Tray, „erneut einfügen“ und ältere Leser die bearbeitete Fassung sehen.
/// </summary>
public static class HistoryVersions
{
    /// <summary>Schlüssel der Fassung „bearbeiteter Ursprung“; keine Stufe, wird nie neu verarbeitet.</summary>
    public const string Edited = "edited";

    /// <summary>Schlüssel der damaligen Verarbeitung.</summary>
    public static string MainKey(HistoryEntry entry) => entry.Refinement.Mode.Key();

    /// <summary>Alle Fassungen in Entstehungsreihenfolge; ältere Einträge ohne Liste haben genau die damalige.</summary>
    public static IReadOnlyList<HistoryVersion> All(HistoryEntry entry) =>
        entry.Versions is { Count: > 0 } versions
            ? versions
            : [new HistoryVersion(MainKey(entry), entry.Text, entry.CreatedAt, entry.Model, entry.Skipped)];

    public static HistoryVersion? Find(HistoryEntry entry, string processing) =>
        All(entry).FirstOrDefault(v => v.Processing == processing);

    /// <summary>Fassung setzen oder (am selben Platz) ersetzen.</summary>
    public static HistoryEntry With(HistoryEntry entry, HistoryVersion version)
    {
        var all = All(entry).ToList();
        var index = all.FindIndex(v => v.Processing == version.Processing);
        if (index >= 0)
        {
            all[index] = version;
        }
        else
        {
            all.Add(version);
        }
        var text = version.Processing == MainKey(entry) ? version.Text : entry.Text;
        return entry with { Versions = all, Text = text };
    }

    /// <summary>
    /// Bearbeiten: <paramref name="text"/> ersetzt die Fassung <paramref name="processing"/> und markiert sie „bearbeitet“; Modell und
    /// Fehlergrund bleiben. Für den Ursprung <see cref="Edited"/> übergeben – <see cref="HistoryEntry.Raw"/> bleibt.
    /// </summary>
    public static HistoryEntry Edit(HistoryEntry entry, string processing, string text, DateTimeOffset now)
    {
        var old = Find(entry, processing);
        var version = old is null
            ? new HistoryVersion(processing, text, now, Edited: true)
            : old with { Text = text, CreatedAt = now, Edited = true };
        return With(entry, version);
    }

    /// <summary>Muss vor dem Ersetzen dieser Fassung gefragt werden (bearbeitete Fassung, Android „Bearbeitete Fassung ersetzen?“)?</summary>
    public static bool NeedsConfirmBeforeReplace(HistoryEntry entry, string processing) =>
        Find(entry, processing)?.Edited == true;
}
