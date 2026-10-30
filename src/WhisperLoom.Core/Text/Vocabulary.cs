using System.Text.RegularExpressions;

namespace WhisperLoom.Core.Text;

/// <summary>
/// Ergebnis für den Erkenner: <see cref="Text"/> geht als <c>prompt</c> bzw. initial_prompt raus;
/// <see cref="Used"/> von <see cref="Total"/> Begriffen passten unter <see cref="Vocabulary.MaxPromptChars"/>;
/// <see cref="Cut"/> = ein einzelner Begriff war selbst zu lang und wurde gekürzt.
/// </summary>
public sealed record VocabularyPrompt(string Text, int Used, int Total, bool Cut = false)
{
    public bool Truncated => Used < Total || Cut;
}

/// <summary>
/// Vokabular für die Erkennung: Namen, Fachbegriffe, gewünschte Schreibweisen. Port von <c>Vocabulary.kt</c>.
/// Gespeichert wird die Liste als Text, ein Eintrag pro Zeile; ein Freitext aus früheren Versionen bleibt
/// ein einzelner Eintrag. Dazu kann eine .md/.txt-Datei verknüpft sein (<see cref="VocabularySource"/>).
/// </summary>
public static class Vocabulary
{
    /// <summary>
    /// Obergrenze für den mitgeschickten Kontext. Whisper beachtet nur die LETZTEN 224 Token; 800 Zeichen
    /// sind grob diese Menge. Weil vorn gekappt wird, stehen die eigenen Begriffe am ENDE.
    /// </summary>
    public const int MaxPromptChars = 800;

    /// <summary>Größte Datei, die gelesen wird — ein Vokabular hat nie Megabytes.</summary>
    public const int MaxFileBytes = 256 * 1024;

    /// <summary>Ende eines gekürzten Eintrags (siehe <see cref="Prompt"/>).</summary>
    public const string CutMark = "…";

    private const string Bom = "﻿";

    private static readonly Regex Separators = new("[,;\n]");

    private static readonly Regex Heading = new("^#{1,6}" + TextPatterns.Ws);

    /// <summary>Markdown-Zeilenanfänge: Zitat, Aufzählung (auch Checkbox), Nummerierung.</summary>
    private static readonly Regex LineMarker = new(
        "^(>" + TextPatterns.Ws + "*|[-*+]" + TextPatterns.Ws + @"+(\[[ xX]]" + TextPatterns.Ws + "+)?|[0-9]+[.)]" + TextPatterns.Ws + "+)");

    /// <summary>Hervorhebungen und Code-Zeichen am Rand eines Begriffs (**Name**, `Begriff`).</summary>
    private static readonly char[] Emphasis = ['*', '_', '`', '"'];

    /// <summary>Gespeicherte Einträge (eine Zeile = ein Eintrag).</summary>
    public static IReadOnlyList<string> Entries(string stored) =>
        stored.Split('\n').Select(e => e.Trim()).Where(e => e.Length > 0).ToList();

    public static string Serialize(IEnumerable<string> entries) => string.Join("\n", entries);

    /// <summary>
    /// Hängt Eingaben an: "Anna, Bernd; Carla" wird zu drei Einträgen. Was es (ohne Rücksicht auf
    /// Groß-/Kleinschreibung) schon gibt, kommt nicht doppelt.
    /// </summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> entries, string input) =>
        Dedupe(entries.Concat(Separators.Split(input).Select(e => e.Trim()).Where(e => e.Length > 0)));

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> entries, string entry) =>
        entries.Where(e => e != entry).ToList();

    /// <summary>
    /// Begriffe aus einer .md/.txt-Datei: pro Zeile, zusätzlich an Komma/Semikolon getrennt.
    /// Markdown-Zeichen am Zeilenanfang, Hervorhebungen, Codeblock-Zäune und Leerzeilen fallen weg.
    /// </summary>
    public static IReadOnlyList<string> ParseFile(string text)
    {
        var output = new List<string>();
        var inCodeBlock = false;
        var body = text.StartsWith(Bom, StringComparison.Ordinal) ? text[Bom.Length..] : text;
        foreach (var rawLine in body.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }
            // Überschriften gliedern die Datei ("# Namen", "## Technik") — sie sind keine Begriffe.
            // Tabellenzeilen ("| a | b |") ebenso wenig.
            if (inCodeBlock || line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal) ||
                line.StartsWith('|') || Heading.IsMatch(line))
            {
                continue;
            }
            var content = LineMarker.Replace(line, "");
            foreach (var part in content.Split(',', ';'))
            {
                var term = part.Trim().Trim(Emphasis).Trim();
                // Ohne Buchstabe/Ziffer ist es kein Begriff: "-" allein, leere Checkbox "[ ]", "***".
                if (term.Any(char.IsLetterOrDigit))
                {
                    output.Add(term);
                }
            }
        }
        return Dedupe(output);
    }

    /// <summary>
    /// Datei-Begriffe vorn, eigene Einträge am Ende (Whisper verwirft von vorn); Duplikate fallen weg,
    /// dabei gewinnt der eigene Eintrag. Wird es zu lang, fallen zuerst die vorderen Datei-Begriffe weg —
    /// gekürzt wird nur ganzheitlich, nie mitten in einem Begriff.
    /// </summary>
    public static VocabularyPrompt Prompt(
        IReadOnlyList<string> own,
        IReadOnlyList<string>? file = null,
        int maxChars = MaxPromptChars)
    {
        var ownTerms = Dedupe(own);
        var ownKeys = ownTerms.Select(t => t.ToLowerInvariant()).ToHashSet();
        var ordered = Dedupe(file ?? []).Where(t => !ownKeys.Contains(t.ToLowerInvariant())).Concat(ownTerms).ToList();
        if (ordered.Count == 0)
        {
            return new VocabularyPrompt("", 0, 0);
        }

        var last = ordered[^1];
        if (last.Length > maxChars)
        {
            // Ein einzelner langer Eintrag (z. B. Freitext aus v3.4): lieber gekürzt als gar nicht.
            // Das "…" markiert den Schnitt; ElevenLabs verwirft daran das angeschnittene Stück.
            var room = last[..(maxChars - CutMark.Length)];
            var space = room.LastIndexOf(' ');
            var head = (space >= 0 ? room[..space] : room) is { Length: > 0 } h ? h : room;
            return new VocabularyPrompt(head.TrimEnd() + CutMark, 1, ordered.Count, Cut: true);
        }
        // Von hinten auffüllen, solange es passt.
        var kept = new LinkedList<string>();
        var length = 0;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var term = ordered[i];
            var extra = kept.Count == 0 ? term.Length : term.Length + 2;
            if (length + extra > maxChars)
            {
                break;
            }
            kept.AddFirst(term);
            length += extra;
        }
        return new VocabularyPrompt(string.Join(", ", kept), kept.Count, ordered.Count);
    }

    private static List<string> Dedupe(IEnumerable<string> terms)
    {
        var seen = new HashSet<string>();
        return terms.Where(t => seen.Add(t.ToLowerInvariant())).ToList();
    }
}
