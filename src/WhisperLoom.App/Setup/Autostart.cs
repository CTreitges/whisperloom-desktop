namespace WhisperLoom.App.Setup;

/// <summary>
/// Autostart beim Anmelden, nur für den aktuellen Benutzer und nur per Schalter (einzige erlaubte Systemänderung):
/// Windows <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, Linux <c>~/.config/autostart/whisperloom.desktop</c>.
/// </summary>
public interface IAutostart
{
    bool IsEnabled { get; }

    /// <summary>Schreibt den Eintrag (überschreibt einen alten, z. B. nach Umzug der App).</summary>
    /// <exception cref="IOException">Eintrag nicht schreibbar.</exception>
    void Enable();

    /// <summary>Entfernt den Eintrag; fehlt er, passiert nichts.</summary>
    void Disable();
}

public static class Autostart
{
    /// <summary>Der Autostart des Betriebssystems für die laufende App; null, wenn das OS keinen kennt oder der Pfad fehlt.</summary>
    public static IAutostart? ForCurrentOs()
    {
        var exe = ExecutablePath();
        if (exe is null)
        {
            return null;
        }
        if (OperatingSystem.IsWindows())
        {
            return new Platform.Windows.WindowsAutostart(Platform.Windows.WindowsAutostart.RunKey, exe);
        }
        if (OperatingSystem.IsLinux())
        {
            return new Platform.Linux.LinuxAutostart(Platform.Linux.LinuxAutostart.DefaultDirectory(), exe);
        }
        return null;
    }

    /// <summary>
    /// Pfad, den der Autostart startet. Ein AppImage läuft aus einem temporären Mount: dann zählt <c>$APPIMAGE</c>.
    /// Velopack startet aus <c>%LocalAppData%\WhisperLoom\current</c>, der Pfad bleibt über Updates gleich.
    /// </summary>
    // Doku: docs.appimage.org/packaging-guide/environment-variables.html (abgerufen 2026-11-01).
    internal static string? ExecutablePath()
    {
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        return OperatingSystem.IsLinux() && !string.IsNullOrEmpty(appImage) ? appImage : Environment.ProcessPath;
    }
}
