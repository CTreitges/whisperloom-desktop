using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// Abnahme W2-UI1 Nr. 4 und UX-1: Kontrast der Farb-Tokens in hell, dunkel und Hochkontrast – Text ≥ 4,5:1 (Hochkontrast ≥ 7:1),
/// Rahmen, Fokus und Symbole ≥ 3:1 (WCAG 1.4.3, 1.4.6, 1.4.11); Werte aus der Loom-Palette (zielstruktur.md §5).
/// </summary>
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

    /// <summary>Rahmen, Fokusring, Schalter an und Symbole (WCAG 1.4.11, zielstruktur.md §5): ≥ 3:1.</summary>
    private static readonly (string Fore, string Back)[] UiPairs =
    [
        ("Loom.Outline", "Loom.Surface"), ("Loom.Outline", "Loom.SurfaceHigh"), ("Loom.Outline", "Loom.Background"),
        ("Loom.Focus", "Loom.Surface"), ("Loom.Focus", "Loom.Background"), ("Loom.Focus", "Loom.Sidebar"),
        ("Loom.Accent", "Loom.Surface"), ("Loom.Record", "Loom.Surface"), ("Loom.Warning", "Loom.Surface"),
        ("Loom.Accent", "Loom.Sidebar"),
    ];

    public static TheoryData<string> Variants => ["Light", "Dark", "HighContrast"];

    private static ThemeVariant VariantOf(string name) => name switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => SystemTheme.HighContrast,
    };

    private static List<string> Below((string Fore, string Back)[] pairs, ThemeVariant variant, double min) =>
        [.. pairs
            .Select(p => (p, ratio: Contrast(ColorOf(p.Fore, variant), ColorOf(p.Back, variant))))
            .Where(x => x.ratio < min)
            .Select(x => $"{x.p.Fore} auf {x.p.Back}: {x.ratio:F2}")];

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Text_tokens_have_at_least_4_5_to_1_and_7_to_1_in_high_contrast(string variantName)
    {
        var variant = VariantOf(variantName);
        Assert.Empty(Below(TextPairs, variant, variant == SystemTheme.HighContrast ? 7 : 4.5));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Outline_focus_and_symbols_have_at_least_3_to_1(string variantName) =>
        Assert.Empty(Below(UiPairs, VariantOf(variantName), 3));

    [AvaloniaFact]
    public void High_contrast_is_black_white_accent_and_yellow_focus()
    {
        var hc = SystemTheme.HighContrast;
        Assert.Equal(Colors.Black, ColorOf("Loom.Background", hc));
        Assert.Equal(Colors.Black, ColorOf("Loom.Surface", hc));
        Assert.Equal(Colors.White, ColorOf("Loom.Text", hc));
        Assert.Equal(Colors.White, ColorOf("Loom.TextSecondary", hc));
        Assert.Equal(Colors.White, ColorOf("Loom.Outline", hc));
        Assert.Equal(Color.Parse("#6FD9C7"), ColorOf("Loom.Accent", hc));
        Assert.Equal(Color.Parse("#FFFF00"), ColorOf("Loom.Focus", hc));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Fluent_borders_use_outline_and_the_focus_ring_uses_focus(string variantName)
    {
        var variant = VariantOf(variantName);
        var outline = ColorOf("Loom.Outline", variant);
        foreach (var key in new[] { "TextControlBorderBrush", "ComboBoxBorderBrush", "ToggleSwitchStrokeOff",
                     "ToggleSwitchKnobFillOff", "CheckBoxCheckBackgroundStrokeUnchecked", "RadioButtonOuterEllipseStroke" })
        {
            Assert.True(ColorOf(key, variant) == outline, $"{variantName}: {key}");
        }
        Assert.Equal(ColorOf("Loom.Focus", variant), ColorOf("SystemControlFocusVisualPrimaryBrush", variant));
        var onAccent = ColorOf("Loom.OnAccent", variant);
        foreach (var key in new[] { "AccentButtonForeground", "CheckBoxCheckGlyphForegroundChecked", "ToggleSwitchKnobFillOn" })
        {
            Assert.True(ColorOf(key, variant) == onAccent, $"{variantName}: {key}");
        }
    }

    [AvaloniaFact]
    public void Fluent_accent_is_the_loom_accent_in_dark_and_light()
    {
        // App.axaml, FluentTheme.Palettes: dunkel #6FD9C7 (Schalter an 9,8:1 statt 2,1:1 mit #0F5B50), hell #0F5B50.
        Assert.True(Application.Current!.TryFindResource("SystemAccentColor", ThemeVariant.Dark, out var dark));
        Assert.True(Application.Current!.TryFindResource("SystemAccentColor", ThemeVariant.Light, out var light));
        Assert.Equal(Color.Parse("#6FD9C7"), (Color)dark!);
        Assert.Equal(Color.Parse("#0F5B50"), (Color)light!);
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
