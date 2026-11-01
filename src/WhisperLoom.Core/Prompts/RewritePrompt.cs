using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Prompts;

/// <summary>
/// Desktop-eigener Prompt „Umschreiben nach Anweisung“ (WLD-P5b, <c>Prompts/desktop/&lt;de|en&gt;/rewrite.txt</c>, Ausnahme in
/// <c>docs/parity-exceptions.md</c>). System-Prompt = die Regeln; Nutzer-Nachricht = die gesprochene Anweisung und die
/// Markierung als zwei abgegrenzte Datenblöcke. Anweisungen in der Markierung gelten als Material (Spotlighting):
/// Markierungs-Tags im Text werden entschärft, damit er seinen Block nicht schließen kann.
/// Die Stufe (<c>mode</c>) spielt keine Rolle; die Pipeline ruft den Builder mit <see cref="RefineMode.Prompt"/>, damit die
/// Regeln danach nichts am Ergebnis ändern (keine Füllwort-Liste, keine Groß-Schreibung).
/// </summary>
public sealed partial class RewritePrompt(string instruction) : IRefinePromptBuilder
{
    public const string InstructionTagDe = "anweisung";
    public const string SelectionTagDe = "auswahl";
    public const string InstructionTagEn = "instruction";
    public const string SelectionTagEn = "selection";

    public string Instruction { get; } = instruction;

    // Auch „< /auswahl>“ und Tags mit Attributen: Modelle lesen sie oft trotzdem als Tag.
    [GeneratedRegex(@"<\s*/?\s*(?:anweisung|auswahl|instruction|selection)\b[^<>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"\A\s*<(auswahl|selection)>\s*(.*?)\s*</\1>\s*\z", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WrappedOutput();

    public string Build(RefineMode mode, bool german, bool smartFillers, bool paragraphs = true, bool isShort = false) =>
        PromptTemplates.Load(german ? "desktop.de" : "desktop.en", "rewrite");

    /// <summary>Anweisung und Markierung (<paramref name="raw"/>) als Datenblöcke.</summary>
    public string UserText(string raw, bool german)
    {
        var (instructionTag, selectionTag) = german ? (InstructionTagDe, SelectionTagDe) : (InstructionTagEn, SelectionTagEn);
        return $"{Block(instructionTag, Instruction)}\n\n{Block(selectionTag, raw)}";
    }

    /// <summary>Entschärft die vier Block-Tags (beide Sprachen) in einem Text: „&lt;auswahl&gt;“ → „‹auswahl›“.</summary>
    public static string Neutralize(string text) => BlockTag().Replace(text, m => "‹" + m.Value[1..^1] + "›");

    /// <summary>Hat das Modell das Ergebnis doch in einen Block-Tag gepackt, kommt er weg.</summary>
    public static string CleanOutput(string output)
    {
        var match = WrappedOutput().Match(output);
        return match.Success ? match.Groups[2].Value : output.Trim();
    }

    private static string Block(string tag, string text) => $"<{tag}>\n{Neutralize(text)}\n</{tag}>";
}
