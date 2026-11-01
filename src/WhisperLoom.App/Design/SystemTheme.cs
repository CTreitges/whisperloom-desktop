using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;

namespace WhisperLoom.App.Design;

/// <summary>Hell/Dunkel des Systems; eigene Schnittstelle, weil <see cref="IPlatformSettings"/> nicht nachbaubar ist (Tests).</summary>
public interface IThemeSource
{
    PlatformThemeVariant Current { get; }

    event Action? Changed;
}

/// <summary>Hell/Dunkel aus den Plattform-Einstellungen von Avalonia.</summary>
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
}

/// <summary>
/// Hell/Dunkel nach dem System für ein Fenster. Die App steht global auf Dunkel (Bühne, Overlay, Verlauf bleiben so);
/// Fenster mit dem Designsystem folgen dem System und wechseln live mit.
/// Doku: docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-theme-variants (abgerufen 2026-11-01).
/// </summary>
public static class SystemTheme
{
    public static ThemeVariant VariantOf(PlatformThemeVariant variant) =>
        variant == PlatformThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

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
        void Apply() => window.RequestedThemeVariant = VariantOf(source.Current);
        void OnChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
        Apply();
        source.Changed += OnChanged;
        window.Closed += (_, _) => source.Changed -= OnChanged;
    }
}
