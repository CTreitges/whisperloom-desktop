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

    /// <summary>true für Dunkel und jede Variante, die von Dunkel erbt (Hochkontrast) – z. B. für Bilder je Variante.</summary>
    public static bool IsDark(ThemeVariant? variant)
    {
        for (var v = variant; v is not null; v = v.InheritVariant)
        {
            if (v == ThemeVariant.Dark)
            {
                return true;
            }
        }
        return false;
    }

    public static void Follow(Window window, IPlatformSettings? settings)
    {
        if (settings is not null)
        {
            Follow(window, new PlatformThemeSource(settings));
        }
    }

    /// <summary>
    /// Wie <see cref="VariantOf(PlatformThemeVariant, ColorContrastPreference)"/>, aber <paramref name="forceDark"/> (Bühne „Dunkel für
    /// Beamer“, Entscheidung 19) macht Hell zu Dunkel; das Kontrastdesign des Systems gewinnt immer.
    /// </summary>
    public static ThemeVariant VariantOf(PlatformThemeVariant variant, ColorContrastPreference contrast, bool forceDark) =>
        contrast == ColorContrastPreference.High ? HighContrast
        : forceDark ? ThemeVariant.Dark
        : VariantOf(variant, contrast);

    /// <summary>Setzt die Variante des Fensters nach <paramref name="source"/> und folgt Änderungen, bis es schließt.</summary>
    public static void Follow(Window window, IThemeSource source) => Follow(window, source, () => false);

    /// <summary>
    /// Wie <see cref="Follow(Window, IThemeSource)"/> mit Schalter „dunkel erzwingen“; die Rückgabe wendet die Variante neu an
    /// (nach einem Wechsel des Schalters).
    /// </summary>
    public static Action Follow(Window window, IThemeSource source, Func<bool> forceDark)
    {
        void Apply() => window.RequestedThemeVariant = VariantOf(source.Current, source.Contrast, forceDark());
        void OnChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
        Apply();
        source.Changed += OnChanged;
        window.Closed += (_, _) => source.Changed -= OnChanged;
        return Apply;
    }
}
