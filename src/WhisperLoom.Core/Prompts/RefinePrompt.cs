using System.Text;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Prompts;

/// <summary>
/// Baut die Anweisung für die Textverbesserung. Port von <c>api/RefinePrompt.kt</c>: die Fragmente liegen
/// als eingebettete Ressourcen je Stufe und Sprache (<see cref="PromptTemplates"/>) und werden wie in Kotlin
/// zusammengesetzt — Stufentext, ggf. Kurz-Zusatz, Schlussformel. Nur portieren, nicht verbessern;
/// Abweichungen nur über <c>docs/parity-exceptions.md</c>.
/// </summary>
public sealed class RefinePrompt : IRefinePromptBuilder
{
    /// <summary>
    /// Unter so vielen Wörtern bleibt der Prompt Fließtext und die Zusammenfassung das bereinigte Diktat
    /// (Schutz gegen Aufblähen).
    /// </summary>
    public const int ShortWords = 15;

    private const string TagDe = "diktat";
    private const string TagEn = "dictation";

    private static readonly Regex Whitespace = new(TextPatterns.Ws + "+");

    public string Build(RefineMode mode, bool german, bool smartFillers, bool paragraphs = true, bool isShort = false)
    {
        if (mode == RefineMode.Off)
        {
            throw new ArgumentException("RefineMode.Off hat keine Anweisung", nameof(mode));
        }
        var lang = german ? "de" : "en";
        var sb = new StringBuilder(mode switch
        {
            RefineMode.Polish => PromptTemplates.Render(lang, "polish", paragraphs, smartFillers),
            RefineMode.Readable => PromptTemplates.Render(lang, "readable", paragraphs),
            RefineMode.Beautify => PromptTemplates.Render(lang, "beautify", paragraphs),
            RefineMode.Summarize => PromptTemplates.Render(lang, "summarize", paragraphs),
            RefineMode.Prompt => PromptTemplates.Load(lang, "prompt"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        });
        // Trenner wie in Kotlin: "\n" vor Kurz-Zusatz und Schlussformel, bei "Prompt" eine Leerzeile.
        if (isShort && mode == RefineMode.Summarize)
        {
            sb.Append('\n').Append(PromptTemplates.Load(lang, "short-summary"));
        }
        if (isShort && mode == RefineMode.Prompt)
        {
            sb.Append("\n\n").Append(PromptTemplates.Load(lang, "short-prompt"));
        }
        sb.Append(mode switch
        {
            RefineMode.Summarize => "\n" + PromptTemplates.Load(lang, "reply-summary"),
            RefineMode.Prompt => "\n\n" + PromptTemplates.Load(lang, "reply-prompt"),
            _ => "\n" + PromptTemplates.Load(lang, "reply"),
        });
        return sb.ToString();
    }

    /// <summary>
    /// Was als Nutzer-Nachricht rausgeht: das Diktat zwischen Markierungen. Es ist oft selbst eine Frage oder
    /// Bitte — ohne klare Grenze beantwortet das Modell sie, statt sie zu bearbeiten (Spotlighting).
    /// </summary>
    public string UserText(string raw, bool german)
    {
        var tag = german ? TagDe : TagEn;
        return $"<{tag}>\n{raw}\n</{tag}>";
    }

    public static int WordCount(string text) => Whitespace.Split(text).Count(w => w.Length > 0);

    public static bool IsShort(string raw) => WordCount(raw) < ShortWords;
}
