using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;

namespace WhisperLoom.App.Design;

/// <summary>Hell/Dunkel und Kontrast des Systems; eigene Schnittstelle, weil <see cref="IPlatformSettings"/> nicht nachbaubar ist (Tests).</summary>
public interface IThemeSource
{
    PlatformThemeVariant Current { get; }

    /// <summary>Kontrastdesign des Systems (Windows „Kontrastdesigns“); Quellen ohne Angabe melden „keine Vorgabe“.</summary>
    ColorContrastPreference Contrast => ColorContrastPreference.NoPreference;

    event Action? Changed;
}

/// <summary>Hell/Dunkel und Kontrast aus den Plattform-Einstellungen von Avalonia.</summary>
public sealed class PlatformThemeSource : IThemeSource
{
    private readonly IPlatformSettings _settings;

    public PlatformThemeSource(IPlatformSettings settings)
    {
        _settings = settings;
        settings.ColorValuesChanged += (_, _) => Changed?.Invoke();
    }

    public event Action? Changed;

    public PlatformThemeVariant Current => _settings.GetColorValues().ThemeVariant;

    public ColorContrastPreference Contrast => _settings.GetColorValues().ContrastPreference;
}

/// <summary>
/// Hell/Dunkel/Hochkontrast nach dem System für ein Fenster. Die App steht global auf Dunkel (Bühne, Overlay, Verlauf bleiben so);
/// Fenster mit dem Designsystem folgen dem System und wechseln live mit.
/// Doku: docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-theme-variants (abgerufen 2026-11-01);
/// eigene Variante mit Rückfall: docs.avaloniaui.net/docs/how-to/theme-switching-how-to (Custom theme variants, abgerufen 2026-11-03);
/// api/avalonia/platform/platformcolorvalues (ContrastPreference, abgerufen 2026-11-03).
/// </summary>
public static class SystemTheme
{
    /// <summary>
    /// Hochkontrast (zielstruktur.md §5): Schwarz, Weiß, Akzent #6FD9C7, Gelb nur für den Fokus. Was <c>Tokens.axaml</c> hier nicht
    /// festlegt, kommt aus Dunkel – auch das Fluent-Theme.
    /// </summary>
    public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);

    public static ThemeVariant VariantOf(PlatformThemeVariant variant, ColorContrastPreference contrast = ColorContrastPreference.NoPreference) =>
        contrast == ColorContrastPreference.High ? HighContrast
        : variant == PlatformThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    public static void Follow(Window window, IPlatformSettings? settings)
    {
        if (settings is not null)
        {
            Follow(window, new PlatformThemeSource(settings));
        }
    }

    /// <summary>Setzt die Variante des Fensters nach <paramref name="source"/> und folgt Änderungen, bis es schließt.</summary>
    public static void Follow(Window window, IThemeSource source)
    {
        void Apply() => window.RequestedThemeVariant = VariantOf(source.Current, source.Contrast);
        void OnChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
        Apply();
        source.Changed += OnChanged;
        window.Closed += (_, _) => source.Changed -= OnChanged;
    }
}
