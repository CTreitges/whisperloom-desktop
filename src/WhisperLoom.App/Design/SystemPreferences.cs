namespace WhisperLoom.App.Design;

/// <summary>
/// Barrierefreiheits-Einstellungen des Systems, die Avalonia 12 nicht liefert: Textgröße (Faktor 1,0 = 100 %) und
/// „Animationen anzeigen“. Hochkontrast kommt aus <c>PlatformColorValues.ContrastPreference</c> (<see cref="SystemTheme"/>).
/// Doku: docs.avaloniaui.net/docs/app-development/accessibility (abgerufen 2026-11-02) – kein Textskalierungs-API.
/// </summary>
public interface ISystemPreferences
{
    /// <summary>Faktor der Systemtextgröße, 1,0 bis 2,25 (Windows erlaubt 100–225 %).</summary>
    double TextScale { get; }

    /// <summary>false, wenn das System Animationen abgeschaltet hat (Richtlinie 02 §2.7, reduzierte Bewegung).</summary>
    bool AnimationsEnabled { get; }
}

/// <summary>Feste Werte (Tests, unbekanntes System).</summary>
public sealed record FixedPreferences(double TextScale = 1.0, bool AnimationsEnabled = true) : ISystemPreferences;

public static class SystemPreferences
{
    public const double MinScale = 1.0;
    public const double MaxScale = 2.25;

    /// <summary>Begrenzt einen gelesenen Faktor auf 1,0–2,25; Unsinn (NaN, ≤ 0) gilt als 1,0.</summary>
    public static double Clamp(double factor) =>
        double.IsFinite(factor) && factor > 0 ? Math.Clamp(factor, MinScale, MaxScale) : MinScale;

    /// <summary>Leser für das laufende System.</summary>
    public static ISystemPreferences ForCurrentOs()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Platform.Windows.WindowsPreferences();
        }
        if (OperatingSystem.IsLinux())
        {
            return new Platform.Linux.LinuxPreferences();
        }
        return new FixedPreferences();
    }
}
