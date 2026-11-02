using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Tests.Accessibility;

/// <summary>
/// Textgröße des Systems (UX-1): Faktor auf alle Schrift-Tokens und Fluent-Schriftgrößen, sofort wirksam, zurück auf 100 %.
/// Synchron (kein await zwischen Setzen und Zurücksetzen), damit keine anderen UI-Tests den Faktor sehen.
/// </summary>
public sealed partial class TextScaleTest
{
    [GeneratedRegex("x:Key=\"(Loom\\.Font\\.[A-Za-z]+)\"")]
    private static partial Regex FontKey();

    private static double Resource(string key) => (double)Application.Current!.FindResource(key)!;

    [AvaloniaFact]
    public void Every_font_token_and_the_fluent_sizes_scale_and_return()
    {
        var app = Application.Current!;
        var keys = TextScale.TokenKeys(app).Concat(TextScale.FluentKeys).ToList();
        var before = keys.ToDictionary(k => k, Resource);
        try
        {
            TextScale.Apply(app, 2.0);
            foreach (var key in keys)
            {
                Assert.Equal(before[key] * 2, Resource(key), 2);
            }
            Assert.Equal(2.0, TextScale.Current);
        }
        finally
        {
            TextScale.Apply(app, 1.0);
        }
        foreach (var key in keys)
        {
            Assert.Equal(before[key], Resource(key), 2);
        }
    }

    [AvaloniaFact]
    public void A_bound_text_grows_immediately()
    {
        var app = Application.Current!;
        var text = new TextBlock { Text = "Diktat" };
        text[!TextBlock.FontSizeProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Loom.Font.Body");
        var window = new Window { Content = text };
        window.Show();
        try
        {
            var normal = text.FontSize;
            TextScale.Apply(app, 1.5);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(normal * 1.5, text.FontSize, 2);
        }
        finally
        {
            TextScale.Apply(app, 1.0);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Token_keys_found_at_runtime_match_Tokens_axaml()
    {
        var tokens = File.ReadAllText(Path.Combine(RepoRoot(), "src/WhisperLoom.App/Design/Tokens.axaml"));
        var declared = FontKey().Matches(tokens).Select(m => m.Groups[1].Value).Order().ToList();
        Assert.Equal(declared, TextScale.TokenKeys(Application.Current!).Order().ToList());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WhisperLoom.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden");
    }
}
