using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>Abnahme W2-UI1 Nr. 4: Text-Kontrast der Farb-Tokens ≥ 4,5:1 (WCAG 2.x AA) in hell und dunkel; Werte aus der Loom-Palette.</summary>
public sealed class DesignTokensTest
{
    private static readonly (string Fore, string Back)[] TextPairs =
    [
        ("Loom.Text", "Loom.Background"), ("Loom.Text", "Loom.Surface"), ("Loom.Text", "Loom.Sidebar"), ("Loom.Text", "Loom.SurfaceHigh"),
        ("Loom.TextSecondary", "Loom.Background"), ("Loom.TextSecondary", "Loom.Surface"), ("Loom.TextSecondary", "Loom.Sidebar"),
        ("Loom.TextSecondary", "Loom.SurfaceHigh"),
        ("Loom.Accent", "Loom.Surface"), ("Loom.Accent", "Loom.Background"), ("Loom.OnAccent", "Loom.Accent"),
        ("Loom.OnSelection", "Loom.Selection"), ("Loom.Error", "Loom.Surface"), ("Loom.Error", "Loom.Background"),
        ("Loom.Error", "Loom.SurfaceHigh"), ("Loom.OnManaged", "Loom.Managed"),
    ];

    private static Color ColorOf(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryFindResource(key, variant, out var value), key);
        return ((ISolidColorBrush)value!).Color;
    }

    /// <summary>Relative Leuchtdichte nach WCAG 2.x.</summary>
    internal static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    internal static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Text_tokens_have_at_least_4_5_to_1(string variantName)
    {
        var variant = variantName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var failures = TextPairs
            .Select(p => (p, ratio: Contrast(ColorOf(p.Fore, variant), ColorOf(p.Back, variant))))
            .Where(x => x.ratio < 4.5)
            .Select(x => $"{x.p.Fore} auf {x.p.Back}: {x.ratio:F2}")
            .ToList();
        Assert.Empty(failures);
    }

    [AvaloniaFact]
    public void White_glyphs_on_sidebar_tiles_are_readable()
    {
        foreach (var tile in new[] { "Teal", "Blue", "Violet", "Red", "Green", "Amber", "Gray" })
        {
            var ratio = Contrast(Colors.White, ColorOf("Loom.Tile." + tile, ThemeVariant.Light));
            Assert.True(ratio >= 4.5, $"{tile}: {ratio:F2}");
        }
    }

    [AvaloniaFact]
    public void Dark_tokens_are_the_android_loom_palette()
    {
        // LoomPalette (Color.kt): background, surfaceContainer, onSurface, onSurfaceVariant, primary, error.
        Assert.Equal(Color.Parse("#0E1116"), ColorOf("Loom.Background", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#1A1F26"), ColorOf("Loom.Surface", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#E4E8EE"), ColorOf("Loom.Text", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#B8C0CC"), ColorOf("Loom.TextSecondary", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#6FD9C7"), ColorOf("Loom.Accent", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#FFB4AB"), ColorOf("Loom.Error", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#0F5B50"), ColorOf("Loom.Accent", ThemeVariant.Light)); // primaryContainer
    }

    [Fact]
    public void Contrast_formula_matches_known_values()
    {
        Assert.Equal(21, Contrast(Colors.Black, Colors.White), 2);
        Assert.Equal(1, Contrast(Colors.Gray, Colors.Gray), 5);
    }
}
