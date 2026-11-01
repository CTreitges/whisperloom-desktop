using System.Globalization;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.History;

namespace WhisperLoom.App.Features.History;

/// <summary>
/// Eine Zeile der Verlaufsliste (W2-OUT, wie Android <c>HistoryListScreen</c>): Vorschau, Meta (Uhrzeit · Dauer · Erkennung),
/// Stufen-Etikett; die erste Zeile eines Tages trägt die Tages-Überschrift (Heute, Gestern, Datum).
/// </summary>
public sealed record HistoryRow(HistoryEntry Entry, bool FirstOfDay, string DayLabel)
{
    public const int PreviewLength = 140;

    public string Preview
    {
        get
        {
            var line = string.Join(' ', Entry.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return line.Length <= PreviewLength ? line : line[..PreviewLength] + "…";
        }
    }

    public string Meta
    {
        get
        {
            var time = Entry.CreatedAt.ToLocalTime().ToString("t", Culture);
            var seconds = Math.Max(1, (int)Math.Round(Entry.DurationMs / 1000.0));
            var duration = seconds < 60 ? $"{seconds} s" : $"{seconds / 60}:{seconds % 60:00} min";
            return Entry.SttLabel.Length > 0 ? $"{time} · {duration} · {Entry.SttLabel}" : $"{time} · {duration}";
        }
    }

    public string StageLabel => Loc.Instance["Stage_" + HistoryVersions.MainKey(Entry)];

    /// <summary>Weitere Fassungen außer der damaligen bzw. bearbeitet: kleines Zeichen in der Liste.</summary>
    public bool HasMore => HistoryVersions.All(Entry).Count > 1 || HistoryVersions.All(Entry).Any(v => v.Edited);

    internal static CultureInfo Culture => CultureInfo.GetCultureInfo(Loc.Instance.Language == "en" ? "en-GB" : "de-DE");

    /// <summary>Zeilen mit Tages-Überschriften, neueste zuerst.</summary>
    public static List<HistoryRow> Build(IEnumerable<HistoryEntry> newestFirst, DateTimeOffset now)
    {
        var rows = new List<HistoryRow>();
        DateOnly? day = null;
        foreach (var entry in newestFirst)
        {
            var d = DateOnly.FromDateTime(entry.CreatedAt.ToLocalTime().Date);
            rows.Add(new HistoryRow(entry, d != day, Day(d, DateOnly.FromDateTime(now.ToLocalTime().Date))));
            day = d;
        }
        return rows;
    }

    public static string Day(DateOnly day, DateOnly today) =>
        day == today ? Loc.Instance["History_Today"]
        : day == today.AddDays(-1) ? Loc.Instance["History_Yesterday"]
        : day.ToString(Loc.Instance.Language == "en" ? "dddd, d MMMM yyyy" : "dddd, d. MMMM yyyy", Culture);

    /// <summary>Volltextsuche: Ursprung, damalige Fassung und alle Fassungen, ohne Groß-/Kleinschreibung.</summary>
    public static bool Matches(HistoryEntry entry, string query)
    {
        var q = query.Trim();
        if (q.Length == 0)
        {
            return true;
        }
        return entry.Raw.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || entry.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || HistoryVersions.All(entry).Any(v => v.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }
}
