using System.Text.RegularExpressions;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Farben nur als Rollen-Tokens (Richtlinie 02 §2.3, Nachher-Audit UX-6 N1/N2): keine Hex-Farbe in Ansichten und Styles.
/// Erlaubt sind die Token-Dateien selbst (<c>Design/Tokens.axaml</c>, erzeugte <c>Design/Icons.axaml</c>) und der
/// Fluent-Akzent in <c>ColorPaletteResources</c> (App.axaml), der die Loom-Akzentfarbe an Fluent weitergibt.
/// </summary>
public sealed partial class UxRawColorTest
{
    [Fact]
    public void No_hex_colors_outside_the_token_files()
    {
        var app = Path.Combine(RepoRoot(), "src", "WhisperLoom.App");
        var hits = Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Path.GetFileName(f) is not ("Tokens.axaml" or "Icons.axaml"))
            .SelectMany(f => WithoutComments(File.ReadAllText(f)).Split('\n').Select((line, i) => (f, line, i)))
            .Where(x => HexColor().IsMatch(x.line) && !x.line.Contains("<ColorPaletteResources", StringComparison.Ordinal))
            .Select(x => $"{Path.GetRelativePath(app, x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [GeneratedRegex(@"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    /// <summary>Kommentare nennen Farben zur Erklärung; sie zählen nicht. Zeilenumbrüche bleiben, damit die Zeilennummern stimmen.</summary>
    private static string WithoutComments(string xaml) =>
        XmlComment().Replace(xaml, m => new string('\n', m.Value.Count(c => c == '\n')));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WhisperLoom.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("WhisperLoom.slnx nicht gefunden");
    }
}
