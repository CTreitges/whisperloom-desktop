using System.Text;
using System.Text.RegularExpressions;

namespace WhisperLoom.Core.Text;

/// <summary>
/// Teilt einen langen Transkript-Text heuristisch in Absätze. Port von <c>Paragrapher.kt</c>.
/// Absatzregel (UX-Spec §2.9): innerhalb eines Stücks beginnt nach 3 Sätzen oder sobald 350 Zeichen
/// erreicht sind ein neuer Absatz. Nie mitten im Satz trennen, Wörter nie anfassen (die Absätze mit
/// Leerzeichen zusammengefügt ergeben wieder den Eingabetext), Absatzwechsel bevorzugt vor
/// Diskursmarkern ("Also", "Außerdem", "Dann", "Okay" …).
/// </summary>
public static class Paragrapher
{
    /// <summary>Nach so vielen Sätzen beginnt ein neuer Absatz (Spec: 3).</summary>
    private const int MaxSentences = 3;

    /// <summary>Sobald ein Absatz so viele Zeichen erreicht hat, beginnt der nächste (Spec: 350).</summary>
    private const int MaxChars = 350;

    /// <summary>Ein Diskursmarker darf früher trennen — aber erst ab so vielen Sätzen/Zeichen.</summary>
    private const int MinSentences = 2;

    private const int MinChars = 120;

    /// <summary>Ein so kurzer letzter Einzelsatz ("Tschüss.") hängt sich an den vorigen Absatz.</summary>
    private const int ShortTailChars = 60;

    /// <summary>Satzgrenze: Satzzeichen (optional Anführungszeichen/Klammer) und dann Whitespace.</summary>
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?…][""»“”')\]]?)" + TextPatterns.Ws + "+");

    private static readonly Regex MultiWs = new(TextPatterns.Ws + "+");

    /// <summary>
    /// Wörter, die vor einem Punkt KEIN Satzende bedeuten. Einzelbuchstaben ("z. B.") und reine Zahlen
    /// ("am 3. Oktober") werden zusätzlich programmatisch erkannt.
    /// </summary>
    private static readonly HashSet<string> Abbreviations =
    [
        "bzw", "usw", "ca", "evtl", "ggf", "inkl", "exkl", "zzgl", "vgl", "dr", "nr", "str",
        "prof", "vs", "etc", "mr", "mrs", "ms", "st", "tel", "max", "min",
    ];

    /// <summary>Satzanfänge, vor denen ein Absatzwechsel natürlich wirkt (klein geschrieben).</summary>
    private static readonly string[] Markers =
    [
        "also", "außerdem", "ausserdem", "dann", "und dann", "okay", "ok", "zweitens", "drittens",
        "noch was", "noch etwas", "übrigens", "jedenfalls", "ach so", "ach ja", "genau", "so",
        "anyway", "then", "secondly", "another thing", "by the way", "also,",
    ];

    public static IReadOnlyList<string> Split(string text)
    {
        var sentences = Sentences(text);
        if (sentences.Count == 0)
        {
            return [];
        }

        var paragraphs = new List<List<string>>();
        var current = new List<string>();
        var currentChars = 0;

        foreach (var s in sentences)
        {
            var full = current.Count >= MaxSentences || currentChars >= MaxChars;
            var marker = current.Count >= MinSentences && currentChars >= MinChars && StartsWithMarker(s);
            if (full || marker)
            {
                paragraphs.Add(current);
                current = [];
                currentChars = 0;
            }
            current.Add(s);
            currentChars += s.Length + (current.Count > 1 ? 1 : 0);
        }
        paragraphs.Add(current);

        // Ein kurzer Einzelsatz am Ende ("Tschüss.") wirkt als eigener Absatz verloren —
        // bewusste Ausnahme von der 3-Sätze-Regel.
        if (paragraphs.Count > 1)
        {
            var tail = paragraphs[^1];
            if (tail.Count == 1 && tail[0].Length < ShortTailChars)
            {
                paragraphs.RemoveAt(paragraphs.Count - 1);
                paragraphs[^1].AddRange(tail);
            }
        }
        return paragraphs.Select(p => string.Join(" ", p)).ToList();
    }

    /// <summary>Zerlegt in Sätze; Abkürzungen, Einzelbuchstaben und Ordnungszahlen bleiben verbunden.</summary>
    public static IReadOnlyList<string> Sentences(string text)
    {
        var normalized = MultiWs.Replace(text.Trim(), " ");
        if (normalized.Length == 0)
        {
            return [];
        }
        var pieces = SentenceEnd.Split(normalized).Where(p => p.Length > 0);

        var output = new List<string>();
        var pending = new StringBuilder();
        foreach (var piece in pieces)
        {
            if (pending.Length > 0)
            {
                pending.Append(' ');
            }
            pending.Append(piece);
            if (!EndsWithAbbreviation(piece))
            {
                output.Add(pending.ToString());
                pending.Clear();
            }
        }
        if (pending.Length > 0)
        {
            output.Add(pending.ToString());
        }
        return output;
    }

    /// <summary>
    /// Endet <paramref name="piece"/> mit einer Abkürzung, einem Einzelbuchstaben oder einer Ordnungszahl
    /// samt Punkt ("z. B.", "ca.", "am 3.")? Dann ist der Punkt kein Satzende. Auch
    /// <see cref="TextPolisher"/> nutzt das, damit nach "z. B." kein Groß-Buchstabe folgt.
    /// </summary>
    public static bool EndsWithAbbreviation(string piece)
    {
        if (!piece.EndsWith('.'))
        {
            return false;
        }
        var lastWord = AfterLast(piece[..^1], ' ').TrimStart('(', '"', '„', '«');
        if (lastWord.Length == 0)
        {
            return false;
        }
        // "z.B." ohne Leerzeichen: der Teil nach dem letzten Punkt zählt wie ein Einzelbuchstabe.
        var tail = AfterLast(lastWord, '.');
        if (tail.Length == 1 && char.IsLetter(tail[0]))
        {
            return true;
        }
        if (lastWord.All(char.IsDigit))
        {
            return true;
        }
        return Abbreviations.Contains(lastWord.ToLowerInvariant());
    }

    /// <summary>Kotlins <c>substringAfterLast</c>: ohne Trenner der ganze Text.</summary>
    private static string AfterLast(string s, char delimiter) => s[(s.LastIndexOf(delimiter) + 1)..];

    private static bool StartsWithMarker(string sentence)
    {
        var s = sentence.ToLowerInvariant();
        return Markers.Any(m =>
            s.StartsWith(m, StringComparison.Ordinal) && (s.Length == m.Length || !char.IsLetter(s[m.Length])));
    }
}
