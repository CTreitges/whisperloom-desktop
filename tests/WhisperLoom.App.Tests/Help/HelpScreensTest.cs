using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Views.Help;

namespace WhisperLoom.App.Tests.Help;

/// <summary>
/// Screenshots der Hilfe (W2-HELP Nr. 3). <c>tools/gen_help_screens.py</c> setzt <c>WLD_HELP_SCREENS_DIR</c> (Ziel, je
/// Sprache und Variante ein Ordner <c>&lt;de|en&gt;-&lt;light|dark&gt;</c>) und <c>WLD_HELP_SCREENS_SPEC</c> (die Liste samt
/// Markierungen); ohne diese Variablen wird nichts erzeugt. Ein zweiter Test baut jede Szene einmal in einen Temp-Ordner,
/// damit veraltete Markierungen (umbenanntes Steuerelement, entfernte Zeile) sofort auffallen.
/// </summary>
public sealed class HelpScreensTest
{
    private static readonly (string Name, ThemeVariant Theme)[] Themes = [("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark)];

    private static async Task RenderAsync(IReadOnlyList<HelpShot> shots, string dir, IEnumerable<string> languages, IEnumerable<(string Name, ThemeVariant Theme)> themes)
    {
        var before = Loc.Instance.Language;
        try
        {
            foreach (var lang in languages)
            {
                Loc.Instance.SetLanguage(lang);
                foreach (var (themeName, theme) in themes)
                {
                    foreach (var shot in shots)
                    {
                        var scene = await HelpShotScenes.BuildAsync(shot, lang, theme);
                        try
                        {
                            HelpShotRenderer.Save(scene, shot.Markers, Path.Combine(dir, $"{lang}-{themeName}", shot.Name + ".png"));
                        }
                        finally
                        {
                            scene.Window.Close();
                            scene.Cleanup?.Dispose();
                        }
                    }
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(before);
        }
    }

    [AvaloniaFact]
    public async Task Generate_help_screens_for_every_language_and_theme()
    {
        var dir = Environment.GetEnvironmentVariable("WLD_HELP_SCREENS_DIR");
        if (dir is null)
        {
            Assert.Skip("WLD_HELP_SCREENS_DIR nicht gesetzt (tools/gen_help_screens.py)");
            return;
        }
        var spec = Environment.GetEnvironmentVariable("WLD_HELP_SCREENS_SPEC");
        var shots = spec is null ? HelpShots.Load() : HelpShots.Parse(await File.ReadAllTextAsync(spec));
        await RenderAsync(shots, dir, Loc.Languages, Themes);
        foreach (var lang in Loc.Languages)
        {
            foreach (var (themeName, _) in Themes)
            {
                foreach (var shot in shots)
                {
                    Assert.True(File.Exists(Path.Combine(dir, $"{lang}-{themeName}", shot.Name + ".png")), shot.Name);
                }
            }
        }
    }

    [AvaloniaFact]
    public async Task Every_scene_builds_and_every_marker_resolves()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-help-" + Guid.NewGuid().ToString("N"));
        try
        {
            var shots = HelpShots.Load();
            await RenderAsync(shots, dir, ["de"], [Themes[1]]);
            var files = Directory.GetFiles(Path.Combine(dir, "de-dark"), "*.png");
            Assert.Equal(shots.Count, files.Length);
            // Breite der Hilfe-Spalte: höchstens 900 px, damit die Bilder ohne starkes Verkleinern lesbar bleiben.
            foreach (var file in files)
            {
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(file);
                Assert.InRange(bitmap.PixelSize.Width, 300, 900);
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
