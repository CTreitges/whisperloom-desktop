using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Styling;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Setup;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views.Help;

namespace WhisperLoom.App.Tests.Help;

/// <summary>
/// Inhalt der Hilfe (W2-HELP Nr. 1–3): jede Einstellungsseite der IA hat einen Abschnitt, alle Texte DE+EN, jedes
/// referenzierte Bild existiert (Illustration und Screenshot je Sprache/Variante), keine übrigen Bilder.
/// </summary>
public sealed class HelpCatalogTest
{
    private static readonly string[] Required =
        ["start", "dictation", "stages", "selection", "history", "output", "recognition", "providers", "company", "privacy", "hotkeys", "trouble", "it"];

    [Fact]
    public void Every_settings_page_of_the_ia_points_to_a_help_section()
    {
        foreach (var (page, _) in SettingsCatalog.Pages)
        {
            Assert.True(HelpCatalog.PageTopics.TryGetValue(page, out var topic), $"Seite {page} hat keinen Hilfe-Abschnitt");
            Assert.NotNull(HelpCatalog.Find(topic));
        }
        // Keine Leichen: jede zugeordnete Seite gibt es.
        Assert.All(HelpCatalog.PageTopics.Keys, page => Assert.Contains(SettingsCatalog.Pages, p => p.Id == page));
    }

    [Fact]
    public void Required_sections_exist_once()
    {
        Assert.All(Required, id => Assert.NotNull(HelpCatalog.Find(id)));
        Assert.Equal(HelpCatalog.Topics.Count, HelpCatalog.Topics.Select(t => t.Id).Distinct().Count());
        Assert.DoesNotContain(HelpCatalog.Topics, t => t.Id == HelpCatalog.Overview);
    }

    [Fact]
    public void Unknown_page_and_every_setup_step_have_a_section()
    {
        Assert.Equal(HelpCatalog.Overview, HelpCatalog.TopicForPage("gibt-es-nicht"));
        Assert.Equal(HelpCatalog.Overview, HelpCatalog.TopicForPage(null));
        foreach (var step in Enum.GetValues<SetupStep>())
        {
            Assert.NotNull(HelpCatalog.Find(HelpCatalog.TopicForSetup(step)));
        }
        Assert.Equal("company", HelpCatalog.TopicForSetup(SetupStep.Company));
    }

    [Fact]
    public void It_section_links_server_readme_and_provisioning()
    {
        var urls = HelpCatalog.Find("it")!.Links.Select(l => l.Url).ToList();
        Assert.Contains(urls, u => u.EndsWith("/server/README.md", StringComparison.Ordinal));
        Assert.Contains(urls, u => u.EndsWith("/docs/provisioning.md", StringComparison.Ordinal));
        Assert.All(HelpCatalog.Topics.SelectMany(t => t.Links), l => Assert.StartsWith("https://github.com/CTreitges/whisperloom-desktop/blob/main/", l.Url));
    }

    [AvaloniaFact]
    public void Every_text_exists_in_german_and_english()
    {
        var before = Loc.Instance.Language;
        try
        {
            foreach (var lang in Loc.Languages)
            {
                Loc.Instance.SetLanguage(lang);
                var help = new HelpViewModel();
                foreach (var section in help.Sections)
                {
                    var texts = section.IsOverview
                        ? [section.Title, section.Intro]
                        : section.SearchLines().Concat(section.Links.Select(l => l.Label)).Append(section.GroupTitle ?? "x").ToList();
                    Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t) || t.StartsWith('!'), $"{lang}/{section.Id}: „{t}“"));
                    if (!section.IsOverview)
                    {
                        Assert.True(section.Lines.Count >= 3, $"{lang}/{section.Id}: zu wenig Text");
                        Assert.False(string.IsNullOrWhiteSpace(section.ImageAlt), $"{lang}/{section.Id}: Bildbeschreibung");
                    }
                }
                help.Dispose();
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(before);
        }
    }

    [AvaloniaFact]
    public void Every_illustration_exists_and_uses_only_loom_tokens()
    {
        var app = Application.Current!;
        foreach (var topic in HelpCatalog.Topics)
        {
            var shapes = HelpIllustration.Load(topic.Illustration);
            Assert.True(shapes.Count > 3, topic.Id);
            foreach (var token in shapes.SelectMany(s => new[] { s.Fill, s.Stroke }).OfType<string>().Distinct())
            {
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Assert.True(app.TryFindResource("Loom." + token, theme, out var brush) && brush is Avalonia.Media.IBrush, $"{topic.Id}: Loom.{token}");
                }
            }
        }
    }

    [AvaloniaFact]
    public void Every_section_has_a_screenshot_and_every_screenshot_file_exists()
    {
        var shots = HelpShots.Load();
        Assert.All(HelpCatalog.Topics, t => Assert.Contains(shots, s => s.Section == t.Id));
        Assert.All(shots, s => Assert.NotNull(HelpCatalog.Find(s.Section)));
        var expected = new HashSet<string>();
        foreach (var shot in shots)
        {
            Assert.NotEmpty(shot.Markers);
            foreach (var lang in Loc.Languages)
            {
                foreach (var theme in new[] { "light", "dark" })
                {
                    var path = HelpShot.AssetPath(shot.Name, lang, theme);
                    Assert.True(AssetLoader.Exists(new Uri(path)), $"fehlt: {path} – tools/gen_help_screens.py laufen lassen");
                    expected.Add(path);
                }
            }
        }
        // Übrige Screenshots (Eintrag entfernt, Datei geblieben) fallen ebenso auf.
        var present = AssetLoader.GetAssets(new Uri("avares://WhisperLoom/Assets/Help/screens/"), null)
            .Select(u => u.ToString()).Where(u => u.EndsWith(".png", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(present, p => !expected.Contains(p));
    }

    [AvaloniaFact]
    public void Every_screenshot_has_caption_and_one_legend_line_per_marker()
    {
        var before = Loc.Instance.Language;
        try
        {
            foreach (var lang in Loc.Languages)
            {
                Loc.Instance.SetLanguage(lang);
                foreach (var shot in HelpShots.Load())
                {
                    Assert.False(Loc.Instance[shot.CaptionKey].StartsWith('!'), $"{lang}: {shot.CaptionKey}");
                    for (var i = 1; i <= shot.Markers.Count; i++)
                    {
                        Assert.False(Loc.Instance[shot.LegendKey(i)].StartsWith('!'), $"{lang}: {shot.LegendKey(i)}");
                    }
                    Assert.StartsWith("!", Loc.Instance[shot.LegendKey(shot.Markers.Count + 1)]);
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(before);
        }
    }

    [Fact]
    public void Body_lines_are_parsed_into_headings_bullets_and_paragraphs()
    {
        var lines = HelpSectionViewModel.Parse("## Titel\n• Punkt eins\nAbsatz\n\n  • Punkt zwei  ");
        Assert.Equal(4, lines.Count);
        Assert.True(lines[0].IsHeading);
        Assert.Equal("Titel", lines[0].Text);
        Assert.True(lines[1].IsBullet);
        Assert.Equal("Punkt eins", lines[1].Text);
        Assert.True(lines[2].IsParagraph);
        Assert.Equal("Punkt zwei", lines[3].Text);
        Assert.Empty(HelpSectionViewModel.Parse(""));
    }

    [Fact]
    public void Search_snippet_keeps_the_match_visible()
    {
        Assert.Equal("Kurz und Proxy", HelpViewModel.Snippet("• Kurz und Proxy", "proxy"));
        var long_ = string.Join(' ', Enumerable.Repeat("Wort", 20)) + " Proxy am Ende " + string.Join(' ', Enumerable.Repeat("mehr", 20));
        var snippet = HelpViewModel.Snippet(long_, "Proxy");
        Assert.StartsWith("… ", snippet);
        Assert.Contains("Proxy", snippet);
        Assert.True(snippet.Length <= 90);
        Assert.Equal("Ohne Treffer", HelpViewModel.Snippet("Ohne Treffer", "xyz"));
    }

    [Fact]
    public void Svg_transform_is_scale_then_translate()
    {
        var m = HelpIllustration.ParseTransform("translate(10 20) scale(2)")!.Value;
        Assert.Equal(new Point(12, 22), new Point(1, 1).Transform(m));
        Assert.Null(HelpIllustration.ParseTransform(null));
        Assert.Equal(new Point(4, 1), new Point(1, 1).Transform(HelpIllustration.ParseTransform("translate(3)")!.Value));
    }

    [AvaloniaFact]
    public void Svg_parser_reads_tokens_and_stroke_width()
    {
        var svg = XDocument.Parse("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 240">
              <path d="M0,0 h10 v10 z" fill="#0F5B50" data-fill="Accent"/>
              <path d="M0,0 L5,5" fill="none" stroke="#B8C0CC" data-stroke="Separator" stroke-width="2.5"/>
            </svg>
            """);
        var shapes = HelpIllustration.Parse(svg);
        Assert.Equal(2, shapes.Count);
        Assert.Equal("Accent", shapes[0].Fill);
        Assert.Null(shapes[0].Stroke);
        Assert.Equal("Separator", shapes[1].Stroke);
        Assert.Equal(2.5, shapes[1].StrokeWidth);
    }
}
