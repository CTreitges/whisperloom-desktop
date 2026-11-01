using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;

namespace WhisperLoom.App.Design;

/// <summary>
/// Hell/Dunkel nach dem System für ein Fenster. Die App steht global auf Dunkel (Bühne, Overlay, Verlauf bleiben so);
/// Fenster mit dem Designsystem folgen dem System und wechseln live mit.
/// Doku: docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-theme-variants (abgerufen 2026-11-01).
/// </summary>
public static class SystemTheme
{
    public static ThemeVariant VariantOf(PlatformThemeVariant variant) =>
        variant == PlatformThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    /// <summary>Setzt die Variante des Fensters nach <paramref name="settings"/> und folgt Änderungen, bis es schließt.</summary>
    public static void Follow(Window window, IPlatformSettings? settings)
    {
        if (settings is null)
        {
            return;
        }
        void Apply() => window.RequestedThemeVariant = VariantOf(settings.GetColorValues().ThemeVariant);
        void OnChanged(object? sender, PlatformColorValues e) => Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
        Apply();
        settings.ColorValuesChanged += OnChanged;
        window.Closed += (_, _) => settings.ColorValuesChanged -= OnChanged;
    }
}
