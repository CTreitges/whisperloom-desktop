using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// Eigenes Symbol-Set (UX-1, zielstruktur.md §8): 55 Symbole aus design/icons, alle im 24-px-Raster mit 2 px Rand, jeder
/// verwendete Schlüssel existiert, keine Material-Glyphen und kein <c>PathIcon</c> mehr; <see cref="LoomIcon"/> zeichnet die Kontur.
/// </summary>
public sealed partial class IconSetTest
{
    [GeneratedRegex("x:Key=\"(Loom\\.Icon\\.[A-Za-z]+)\"")]
    private static partial Regex DeclaredKey();

    [GeneratedRegex("Loom\\.Icon\\.([A-Za-z]+)")]
    private static partial Regex UsedKey();

    private static readonly string Root = RepoRoot();
    private static readonly string AppDir = Path.Combine(Root, "src", "WhisperLoom.App");

    private static List<string> Declared() =>
        [.. DeclaredKey().Matches(File.ReadAllText(Path.Combine(AppDir, "Design", "Icons.axaml"))).Select(m => m.Groups[1].Value)];

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(AppDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [AvaloniaFact]
    public void Every_source_svg_is_a_geometry_inside_the_24_px_grid()
    {
        var declared = Declared();
        Assert.Equal(55, declared.Count);
        Assert.Equal(Directory.GetFiles(Path.Combine(Root, "design", "icons"), "*.svg").Length, declared.Count);
        foreach (var key in declared)
        {
            Assert.True(Application.Current!.TryFindResource(key, out var value), key);
            var bounds = Assert.IsAssignableFrom<Geometry>(value).Bounds;
            Assert.True(bounds.X >= 1.9 && bounds.Y >= 1.9 && bounds.Right <= 22.1 && bounds.Bottom <= 22.1, $"{key}: {bounds}");
        }
    }

    [Fact]
    public void Every_used_icon_key_exists()
    {
        var declared = Declared().ToHashSet();
        var missing = Sources()
            .Where(f => !f.EndsWith("Icons.axaml", StringComparison.Ordinal))
            .SelectMany(f => UsedKey().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Value)))
            .Where(u => !declared.Contains(u.Key))
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct()
            .ToList();
        // Seiten-Symbole nennen nur den Namen („System“); die prüft A_settings_page_icon_name_resolves.
        Assert.Empty(missing);
    }

    [Fact]
    public void No_material_glyphs_and_no_PathIcon_remain()
    {
        var hits = Sources()
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => x.line.Contains("Material", StringComparison.OrdinalIgnoreCase) || x.line.Contains("<PathIcon", StringComparison.Ordinal))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(hits);
    }

    [AvaloniaFact]
    public void A_settings_page_icon_name_resolves()
    {
        using var f = new ControllerFixture();
        var vm = new ViewModels.SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        using var shell = new ViewModels.Settings.SettingsShellViewModel(vm);
        foreach (var page in shell.Pages)
        {
            Assert.True(Application.Current!.TryFindResource(page.Icon, out var value) && value is Geometry, page.Icon);
        }
    }

    [AvaloniaFact]
    public void LoomIcon_draws_the_outline_in_the_foreground_colour()
    {
        var icon = new LoomIcon { Width = 48, Height = 48, Foreground = Brushes.Red, Data = (Geometry)Application.Current!.FindResource("Loom.Icon.Check")! };
        var window = new Window { Width = 64, Height = 64, Background = Brushes.White, Content = icon };
        window.Show();
        try
        {
            var drawn = RedPixels(window);
            Assert.True(drawn > 60, $"Häkchen nicht gezeichnet ({drawn} rote Pixel)");
            icon.Data = null;
            Assert.Equal(0, RedPixels(window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LoomIcon_measures_24_px_without_explicit_size()
    {
        var icon = new LoomIcon();
        icon.Measure(new Size(100, 100));
        Assert.Equal(new Size(24, 24), icon.DesiredSize);
    }

    private static int RedPixels(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("kein Frame");
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, data, 0, data.Length);
        var count = 0;
        for (var i = 0; i + 3 < data.Length; i += 4)
        {
            // Kräftiges Rot, wenig Grün und Blau (BGRA oder RGBA, je nach Plattform).
            var (a, g, b) = (data[i], data[i + 1], data[i + 2]);
            if (g < 120 && Math.Max(a, b) > 180 && Math.Min(a, b) < 120)
            {
                count++;
            }
        }
        return count;
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
