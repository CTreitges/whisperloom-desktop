using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace WhisperLoom.Core.Prompts;

/// <summary>
/// Lädt die Prompt-Fragmente (eingebettete <c>Prompts/&lt;de|en&gt;/&lt;name&gt;.txt</c>) und setzt sie zusammen.
/// Vorlagen-Syntax, nur was <c>RefinePrompt.kt</c> braucht:
/// <list type="bullet">
/// <item><c>{{FRAME}}</c> — das Fragment <c>frame.txt</c> derselben Sprache (Kotlin <c>$FRAME</c>).</item>
/// <item><c>{{p?A|B}}</c> — A mit Absätzen, sonst B; <c>{{f?A|B}}</c> dasselbe für smartFillers.</item>
/// <item>Eine Zeile, die nur aus Bedingungen bestand und leer ergibt, entfällt (Kotlin <c>listOfNotNull</c>).</item>
/// </list>
/// Die Texte sind 1:1 aus Kotlin; <c>tools/check_prompt_parity.py</c> prüft das.
/// </summary>
internal static class PromptTemplates
{
    private static readonly Regex Token = new(@"\{\{(?:(?<ref>FRAME)|(?<var>[pf])\?(?<a>[^|}]*)\|(?<b>[^}]*))\}\}");

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>Das Fragment ohne Ersetzungen; Zeilenenden als \n, ohne abschließenden Umbruch.</summary>
    public static string Load(string language, string name) => Cache.GetOrAdd($"{language}.{name}", static key =>
    {
        var resource = $"WhisperLoom.Core.Prompts.{key}.txt";
        using var stream = typeof(PromptTemplates).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Prompt-Ressource fehlt: {resource}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd('\n');
    });

    /// <summary>Fragment laden und die Bedingungen für Absätze (<paramref name="p"/>) und smartFillers (<paramref name="f"/>) einsetzen.</summary>
    public static string Render(string language, string name, bool p, bool f = false)
    {
        var lines = new List<string>();
        foreach (var line in Load(language, name).Split('\n'))
        {
            var rendered = Token.Replace(line, m =>
            {
                if (m.Groups["ref"].Success)
                {
                    return Load(language, "frame");
                }
                var on = m.Groups["var"].Value == "p" ? p : f;
                return on ? m.Groups["a"].Value : m.Groups["b"].Value;
            });
            if (rendered.Length > 0 || line.Length == 0)
            {
                lines.Add(rendered);
            }
        }
        return string.Join("\n", lines);
    }
}
