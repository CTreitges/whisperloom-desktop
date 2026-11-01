using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Features.Reinsert;

/// <summary>Die letzten Diktate fürs Tray-Menü: höchstens <see cref="Count"/>, neueste zuerst, je eine kurze Zeile.</summary>
public static class RecentHistory
{
    public const int Count = 10;

    /// <summary>Länge der Vorschau im Menü (ohne „…“).</summary>
    public const int LabelLength = 40;

    /// <param name="newestFirst">Wie <see cref="IHistoryStore.ListAsync"/>.</param>
    public static IReadOnlyList<HistoryEntry> Take(IReadOnlyList<HistoryEntry> newestFirst) => [.. newestFirst.Take(Count)];

    /// <summary>Eine Zeile: Leerraum zusammengezogen, nach <see cref="LabelLength"/> Zeichen „…“; leer → „…“.</summary>
    public static string Label(HistoryEntry entry)
    {
        var line = string.Join(' ', entry.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0)
        {
            return "…";
        }
        return line.Length <= LabelLength ? line : line[..LabelLength] + "…";
    }
}
