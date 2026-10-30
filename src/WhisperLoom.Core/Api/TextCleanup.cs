using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Prompts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Nacharbeit der Modell-Antwort — online wie lokal (Port von <c>TextRefiner.finish/cleanText/cleanPrompt</c>):
/// Nachdenken, Markierung, Vorrede und Verpackung weg; eine erfüllte Bitte statt der Bearbeitung wird abgelehnt.
/// </summary>
/// <param name="fillerSyllables">Füllsilben, die beim Vergleich „hat der Sprecher die erste Zeile selbst gesagt?“ nicht
/// zählen — auf Android <c>TextPolisher.builtinFillers("de") + ("en")</c>, siehe <see cref="For"/>.</param>
public sealed partial class TextCleanup(IEnumerable<string> fillerSyllables)
{
    private const int MaxGrowth = 2;
    private const int GrowthSlack = 30;
    private const string Quotes = "\"„“”«»";
    private const string FenceMark = "```";

    private readonly HashSet<string> _fillers = [.. fillerSyllables.Select(f => f.ToLowerInvariant())];

    /// <summary>Mit den eingebauten Füllwörtern (de + en) des Polishers aus WLD-P1a.</summary>
    public static TextCleanup For(ITextPolisher polisher) =>
        new(polisher.BuiltinFillers("de").Concat(polisher.BuiltinFillers("en")));

    // Kotlin-Regex 1:1; matchEntire → \A…\z.
    [GeneratedRegex(@"^\s*<think>.*?</think>\s*", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex("</?(diktat|dictation)>", RegexOptions.IgnoreCase)]
    private static partial Regex Marker();

    [GeneratedRegex(@"^(hier|here|sure|klar|gerne?|natürlich|okay|ok|certainly)\b[^\n]*\b(dein|deine|deines|der|die|ihr|your|the)\b[^\n]*\bprompts?\s*:[ \t]*\r?\n", RegexOptions.IgnoreCase)]
    private static partial Regex Preamble();

    [GeneratedRegex(@"^(hier|here|sure|klar|gerne?|natürlich|okay|ok|certainly)\b[^\n]*\b(dein|deine|der|die|das|ihr|your|the)\b[^\n]*\b(text|fassung|version|zusammenfassung|summary|transkript|transcript)\w*\s*:[ \t]*\r?\n", RegexOptions.IgnoreCase)]
    private static partial Regex TextPreamble();

    [GeneratedRegex(@"^prompt\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PromptLabel();

    [GeneratedRegex(@"\A```[\w-]*\r?\n(.*)\r?\n```\z", RegexOptions.Singleline)]
    private static partial Regex Fence();

    [GeneratedRegex(@"\A[""„“«»](.*)[""“”»«]\z", RegexOptions.Singleline)]
    private static partial Regex Quoted();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\r\n|\n|\r")]
    private static partial Regex LineBreak();

    /// <summary>Wörter wie im Prompt-Builder (<see cref="RefinePrompt.WordCount"/>).</summary>
    public static int WordCount(string text) => RefinePrompt.WordCount(text);

    public static bool IsShort(string raw) => RefinePrompt.IsShort(raw);

    /// <summary>Qwen3 &amp; Co. schreiben ihr Nachdenken als &lt;think&gt;…&lt;/think&gt; in den Text — das gehört nie ins Diktat.</summary>
    public static string StripThinking(string content) => ThinkBlock().Replace(content, "", 1);

    /// <summary>Keine oder leere Antwort gilt als gescheitert (<see cref="TextRefiner.MsgEmpty"/>).</summary>
    /// <exception cref="RefineRejectedException">Unplausibel lange oder leere Ausgabe.</exception>
    public string Finish(string raw, RefineMode mode, string? output)
    {
        var text = output is null ? null : StripThinking(output).Trim();
        if (text is not null) text = mode == RefineMode.Prompt ? CleanPrompt(raw, text) : CleanText(raw, text);
        if (string.IsNullOrWhiteSpace(text)) throw new RefineRejectedException(TextRefiner.MsgEmpty);
        return text;
    }

    /// <summary>
    /// Stufe „Prompt“: Vorrede, „Prompt:“-Label, Markierung, Anführungszeichen oder Codeblock um alles weg. Weit länger als
    /// das Diktat heißt: das Modell hat die Bitte erfüllt.
    /// </summary>
    public string CleanPrompt(string raw, string output)
    {
        var text = Marker().Replace(output, "").Trim();
        text = StripPreamble(text, Preamble());
        text = Unwrap(text);
        text = Unwrap(PromptLabel().Replace(text, "", 1).Trim());
        RequirePlausibleLength(raw, text, "Modell hat geantwortet, statt einen Prompt zu formulieren");
        return text;
    }

    /// <summary>
    /// Übrige Stufen: Markierung, eine Vorrede („Hier ist der überarbeitete Text:“) und Verpackung um den GANZEN Text weg.
    /// Stand das Diktat selbst in Anführungszeichen, bleiben sie.
    /// </summary>
    public string CleanText(string raw, string output)
    {
        var text = Marker().Replace(output, "").Trim();
        if (!SaidFirst(raw, text)) text = StripPreamble(text, TextPreamble());
        if (!Quoted().IsMatch(raw.Trim())) text = Unwrap(text);
        RequirePlausibleLength(raw, text, "Modell hat geantwortet, statt den Text zu bearbeiten");
        return text;
    }

    /// <summary>Hat der Sprecher die erste Zeile selbst gesagt? Füllsilben zählen nicht mit.</summary>
    private bool SaidFirst(string raw, string text)
    {
        var first = Words(LineBreak().Split(text)[0]);
        var spoken = Words(raw);
        return first.Count > 0 && spoken.Count >= first.Count && spoken.Take(first.Count).SequenceEqual(first);
    }

    private List<string> Words(string s) =>
        [.. NonWord().Split(s.ToLowerInvariant()).Where(w => w.Length > 0 && !_fillers.Contains(w))];

    private static void RequirePlausibleLength(string raw, string text, string message)
    {
        if (WordCount(text) > MaxGrowth * WordCount(raw) + GrowthSlack) throw new RefineRejectedException(message);
    }

    /// <summary>Bleibt nach der Vorrede nur Material („&lt;text&gt;…“) übrig, war sie die Anweisung selbst.</summary>
    private static string StripPreamble(string text, Regex preamble)
    {
        var match = preamble.Match(text);
        if (!match.Success) return text;
        var rest = text[(match.Index + match.Length)..].Trim();
        return rest.Length == 0 || rest.StartsWith('<') ? text : rest;
    }

    /// <summary>Codeblock oder Anführungszeichen um den GANZEN Text weg — innen stehende bleiben.</summary>
    private static string Unwrap(string text)
    {
        var fence = Fence().Match(text);
        if (fence.Success)
        {
            var inner = fence.Groups[1].Value;
            return inner.Contains(FenceMark, StringComparison.Ordinal) ? text : inner.Trim();
        }
        var quoted = Quoted().Match(text);
        if (!quoted.Success) return text;
        var content = quoted.Groups[1].Value;
        return content.Any(c => Quotes.Contains(c, StringComparison.Ordinal)) ? text : content.Trim();
    }
}
