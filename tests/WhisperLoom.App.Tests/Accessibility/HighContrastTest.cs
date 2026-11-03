using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Tests.Accessibility;

/// <summary>
/// Hochkontrast (UX-1, zielstruktur.md §5): Das Kontrastdesign des Systems schaltet Fenster mit dem Designsystem auf
/// <see cref="SystemTheme.HighContrast"/> – live, wie hell/dunkel. Was dort nicht festgelegt ist, kommt aus Dunkel.
/// </summary>
public sealed class HighContrastTest
{
    [Theory]
    [InlineData(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference, "Light")]
    [InlineData(PlatformThemeVariant.Dark, ColorContrastPreference.NoPreference, "Dark")]
    [InlineData(PlatformThemeVariant.Light, ColorContrastPreference.High, "HighContrast")]
    [InlineData(PlatformThemeVariant.Dark, ColorContrastPreference.High, "HighContrast")]
    public void System_contrast_wins_over_light_and_dark(PlatformThemeVariant variant, ColorContrastPreference contrast, string expected) =>
        Assert.Equal(expected, SystemTheme.VariantOf(variant, contrast).Key);

    [Fact]
    public void High_contrast_counts_as_dark()
    {
        Assert.True(SystemTheme.IsDark(SystemTheme.HighContrast));
        Assert.True(SystemTheme.IsDark(ThemeVariant.Dark));
        Assert.False(SystemTheme.IsDark(ThemeVariant.Light));
        Assert.False(SystemTheme.IsDark(ThemeVariant.Default));
        Assert.False(SystemTheme.IsDark(null));
    }

    [AvaloniaFact]
    public void A_window_switches_to_high_contrast_and_back_live()
    {
        var source = new FakeSource();
        var window = new Window();
        SystemTheme.Follow(window, source);
        window.Show();
        try
        {
            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);

            source.Switch(PlatformThemeVariant.Light, ColorContrastPreference.High);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SystemTheme.HighContrast, window.ActualThemeVariant);
            Assert.True(window.TryFindResource("Loom.Background", window.ActualThemeVariant, out var background));
            Assert.Equal(Colors.Black, ((ISolidColorBrush)background!).Color);

            source.Switch(PlatformThemeVariant.Dark, ColorContrastPreference.NoPreference);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resources_missing_in_high_contrast_fall_back_to_dark()
    {
        // Der innere Fokusring von Fluent und Loom.Tile.* stehen nicht im Hochkontrast-Wörterbuch.
        var app = Application.Current!;
        static Color Of(object? brush) => ((ISolidColorBrush)brush!).Color;
        Assert.True(app.TryFindResource("SystemControlFocusVisualSecondaryBrush", SystemTheme.HighContrast, out var hc));
        Assert.True(app.TryFindResource("SystemControlFocusVisualSecondaryBrush", ThemeVariant.Dark, out var dark));
        Assert.True(app.TryFindResource("SystemControlFocusVisualSecondaryBrush", ThemeVariant.Light, out var light));
        Assert.Equal(Of(dark), Of(hc));
        Assert.NotEqual(Of(light), Of(hc));
        Assert.True(app.TryFindResource("Loom.Tile.Teal", SystemTheme.HighContrast, out _));
    }

    [AvaloniaFact]
    public void Fluent_accent_in_high_contrast_is_the_loom_accent()
    {
        // FluentTheme.Palettes kennt nur Light/Dark; ohne die Farben in Tokens.axaml wäre es das Fluent-Blau #0078D7.
        Assert.True(Application.Current!.TryFindResource("SystemAccentColor", SystemTheme.HighContrast, out var accent));
        Assert.Equal(Color.Parse("#6FD9C7"), (Color)accent!);
    }

    private sealed class FakeSource : IThemeSource
    {
        public event Action? Changed;

        public PlatformThemeVariant Current { get; private set; } = PlatformThemeVariant.Light;

        public ColorContrastPreference Contrast { get; private set; } = ColorContrastPreference.NoPreference;

        public void Switch(PlatformThemeVariant variant, ColorContrastPreference contrast)
        {
            (Current, Contrast) = (variant, contrast);
            Changed?.Invoke();
        }
    }
}
