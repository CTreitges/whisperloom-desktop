namespace WhisperLoom.App.Setup;

/// <summary>
/// Autostart beim Anmelden, nur für den aktuellen Benutzer und nur per Schalter (einzige erlaubte Systemänderung):
/// Windows <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, Linux <c>~/.config/autostart/whisperloom.desktop</c>.
/// </summary>
public interface IAutostart
{
    /// <summary>Reine Existenzprüfung: Eintrag vorhanden, egal welche Datei er startet.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Eintrag vorhanden, startet aber eine andere Datei als diese App (z. B. einen alten Dev-Build nach der Installation).
    /// Fehlt der Eintrag, ist das false.
    /// </summary>
    bool PointsElsewhere { get; }

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
    /// Beim App-Start: Ein vorhandener Eintrag, der eine andere Datei startet, wird auf diese App umgeschrieben; ein eigener
    /// oder fehlender Eintrag bleibt, wie er ist (der Schalter bleibt Sache des Benutzers). Nur für eine installierte App
    /// (<paramref name="installed"/>): sonst holte ein Dev-Build den Eintrag der Installation zu sich.
    /// </summary>
    /// <returns>"repaired", "unchanged" oder "failed" (Eintrag nicht schreibbar); nur Metadaten fürs Log.</returns>
    public static string Repair(IAutostart? autostart, bool installed)
    {
        if (autostart is null || !installed)
        {
            return "unchanged";
        }
        try
        {
            if (!autostart.PointsElsewhere)
            {
                return "unchanged";
            }
            autostart.Enable();
            return "repaired";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "failed";
        }
    }

    /// <summary>Deinstallation (Velopack-Hook): Eintrag entfernen; ein Fehler darf die Deinstallation nicht aufhalten.</summary>
    public static void RemoveOnUninstall(IAutostart? autostart)
    {
        try
        {
            autostart?.Disable();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Der Eintrag bleibt dann stehen; die Deinstallation läuft weiter.
        }
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
