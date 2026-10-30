using System.Text;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Text;

/// <summary>
/// Wandelt rohe Whisper-Ausgabe in sauberen Text um: Whitespace normalisieren, Füllwörter entfernen,
/// Leerzeichen vor Satzzeichen fixen, Sätze groß schreiben. Port von <c>TextPolisher.kt</c>.
/// Bewusst konservativ: nur eindeutige Disfluenzen ("ähm", "um") werden entfernt, keine echten Wörter.
/// </summary>
public sealed class TextPolisher : ITextPolisher
{
    // Nur eindeutige Füllsilben. Echte Wörter wie "like"/"halt" bleiben drin,
    // weil sie im Fließtext meist gewollt sind (false-positive-Vermeidung).
    private static readonly Dictionary<string, string[]> Fillers = new()
    {
        ["de"] = ["ähm", "äh", "öhm", "ähem", "hmm", "öh"],
        ["en"] = ["um", "uh", "uhm", "erm", "hmm"],
        ["es"] = ["eh", "este", "mmm"],
        ["fr"] = ["euh", "hmm"],
        ["it"] = ["ehm", "mmm"],
    };

    // Im auto-Modus NUR sprachübergreifend eindeutige Disfluenzen — niemals Wörter,
    // die in irgendeiner Sprache echt sind (z.B. dt. "um", span. "este").
    private static readonly string[] AutoFillers =
        ["ähm", "äh", "öhm", "ähem", "öh", "uh", "uhm", "erm", "euh", "ehm", "hmm", "mmm"];

    private static readonly Regex MultiWs = new(TextPatterns.Ws + "+");
    private static readonly Regex ManyBlankLines = new(@"\n{3,}");

    // Ein Punkt direkt vor Buchstabe oder Ziffer ist kein Satzzeichen, sondern Teil des
    // nächsten Worts (".log", ".env", ".5") — dort bleibt das Leerzeichen davor stehen.
    private static readonly Regex SpaceBeforePunct = new(TextPatterns.Ws + @"+([,;:!?…]|\.(?![\p{L}\p{N}]))");
    private static readonly Regex CommaBeforeEnd = new("," + TextPatterns.Ws + "*(?=[.!?…])");

    public string Polish(string raw, PolishOptions options)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            return "";
        }

        text = NormalizeWhitespace(text, options.KeepLineBreaks);

        if (options.RemoveFillers)
        {
            var disabled = options.DisabledFillers ?? new HashSet<string>();
            var fillers = BuiltinFillers(options.Language).Where(f => !disabled.Contains(f))
                .Concat(options.CustomFillers ?? []);
            foreach (var filler in fillers)
            {
                if (string.IsNullOrWhiteSpace(filler))
                {
                    continue;
                }
                // (?<!\p{L}) ... (?!\p{L}) = ganze-Wort-Grenze, unicode-tauglich (ä, ö ...).
                // Optionales folgendes Komma mitnehmen, damit keine ", ," Reste bleiben.
                var p = new Regex(
                    @"(?<!\p{L})" + Regex.Escape(filler.Trim()) + @"(?!\p{L}),?",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                text = RemoveFiller(text, p);
            }
            // "gut, ähm." -> "gut, ." -> "gut." — ein Komma direkt vor dem Satzende ist nie gewollt.
            text = CommaBeforeEnd.Replace(text, "");
            text = NormalizeWhitespace(text, options.KeepLineBreaks);
        }

        text = SpaceBeforePunct.Replace(text, "$1");
        text = NormalizeWhitespace(text, options.KeepLineBreaks);

        if (options.AutoCapitalize)
        {
            text = CapitalizeSentences(text);
        }
        return text;
    }

    /// <summary>Eingebaute Liste je Sprache (klein geschrieben) — für das Bearbeiten im UI.</summary>
    public IReadOnlyList<string> BuiltinFillers(string language) =>
        language == "auto" ? AutoFillers : Fillers.GetValueOrDefault(language) ?? [];

    /// <summary>
    /// Jeden Treffer von <paramref name="filler"/> durch ein Leerzeichen ersetzen. Stand ein Treffer groß am
    /// Satzanfang (Textanfang oder nach . ! ? und Leerraum), übernimmt das Folgewort die Groß-Schreibung:
    /// aus "2021. Ähm, dann" wird "2021. Dann". Die Satzanfang-Regel könnte das nicht — hinter einer Zahl
    /// oder Abkürzung setzt sie bewusst keinen Satzanfang.
    /// </summary>
    private static string RemoveFiller(string text, Regex filler)
    {
        var sb = new StringBuilder(text.Length);
        var last = 0;
        var capitalize = false;
        for (var m = filler.Match(text); m.Success; m = m.NextMatch())
        {
            capitalize = AppendCapitalized(sb, text, last, m.Index, capitalize);
            if (char.IsUpper(text[m.Index]) && StartsSentence(text, m.Index))
            {
                capitalize = true;
            }
            sb.Append(' ');
            last = m.Index + m.Length;
        }
        AppendCapitalized(sb, text, last, text.Length, capitalize);
        return sb.ToString();
    }

    /// <summary>text[from, to) anhängen, mit <paramref name="capitalize"/> das erste Wortzeichen groß.</summary>
    /// <returns>ob die Groß-Schreibung noch aussteht</returns>
    private static bool AppendCapitalized(StringBuilder sb, string text, int from, int to, bool capitalize)
    {
        var pending = capitalize;
        for (var i = from; i < to; i++)
        {
            var ch = text[i];
            if (pending && char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
                pending = false;
            }
            else
            {
                sb.Append(ch);
            }
        }
        return pending;
    }

    /// <summary>Beginnt bei <paramref name="index"/> ein Satz? Am Textanfang oder nach . ! ? mit Leerraum dazwischen.</summary>
    private static bool StartsSentence(string text, int index)
    {
        var before = text[..index].TrimEnd();
        return before.Length == 0 || (before.Length < index && before[^1] is '.' or '!' or '?');
    }

    /// <summary>Alles auf eine Zeile — oder je Zeile normalisieren und höchstens eine Leerzeile lassen.</summary>
    private static string NormalizeWhitespace(string text, bool keepLineBreaks)
    {
        if (!keepLineBreaks)
        {
            return MultiWs.Replace(text, " ").Trim();
        }
        var lines = string.Join("\n", text.Split('\n').Select(l => MultiWs.Replace(l, " ").Trim()));
        return ManyBlankLines.Replace(lines, "\n\n").Trim();
    }

    /// <summary>
    /// Erster Buchstabe + jeder Satzanfang groß. Ein Satz endet erst mit . ! ? UND folgendem Leerraum;
    /// Zeichen dazwischen, die weder Buchstabe noch Ziffer sind (Anführungszeichen, Klammern, Emojis,
    /// Sternchen), ändern daran nichts. Sonst würde aus "config.yaml" "config.Yaml". Beginnt ein Satz mit
    /// einer Ziffer, bleibt das folgende Wort, wie es ist ("- 12 people"). Ein Punkt nach einer Abkürzung
    /// oder Ordnungszahl beendet keinen Satz — dieselbe Regel wie beim Absatz-Teilen
    /// (<see cref="Paragrapher.EndsWithAbbreviation"/>).
    /// </summary>
    private static string CapitalizeSentences(string text)
    {
        var sb = new StringBuilder(text.Length);
        var capitalizeNext = true;
        var sentenceEnd = false;
        var wordStart = 0;
        foreach (var ch in text)
        {
            if (capitalizeNext && char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(ch);
            }

            if (ch == '.')
            {
                sentenceEnd = !Paragrapher.EndsWithAbbreviation(sb.ToString(wordStart, sb.Length - wordStart));
            }
            else if (ch is '!' or '?')
            {
                sentenceEnd = true;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                sentenceEnd = false;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (sentenceEnd)
                {
                    capitalizeNext = true;
                }
                sentenceEnd = false;
                wordStart = sb.Length;
            }
        }
        return sb.ToString();
    }
}
