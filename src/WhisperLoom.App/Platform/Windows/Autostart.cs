using System.Runtime.Versioning;
using Microsoft.Win32;
using WhisperLoom.App.Setup;

namespace WhisperLoom.App.Platform.Windows;

/// <summary>
/// Autostart unter Windows: Wert <c>WhisperLoom</c> unter <c>HKCU\…\Run</c> mit dem Pfad in Anführungszeichen.
/// Nur der eigene Benutzerzweig, keine Admin-Rechte. Tests nutzen einen eigenen Schlüssel unter <c>HKCU\Software</c>.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys (abgerufen 2026-11-01).
// Doku: learn.microsoft.com/dotnet/api/microsoft.win32.registrykey (abgerufen 2026-11-01).
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostart(string keyPath, string exePath) : IAutostart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string ValueName = "WhisperLoom";

    private readonly string _keyPath = keyPath;
    private readonly string _exePath = exePath;

    /// <summary>Befehlszeile des Eintrags: Pfad in Anführungszeichen (Leerzeichen in „Program Files“ o. ä.).</summary>
    public string Command => $"\"{_exePath}\"";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>
    /// Wert vorhanden, startet aber eine andere Datei. Verglichen wird der Pfad ohne Anführungszeichen, ohne Groß-/Kleinschreibung
    /// (Windows-Pfade); ein leerer oder nicht als Text gespeicherter Wert gilt ebenfalls als fremd.
    /// </summary>
    public bool PointsElsewhere
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
            var value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
            {
                return false;
            }
            return value is not string command || !string.Equals(command.Trim().Trim('"'), _exePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new IOException("Autostart-Eintrag nicht schreibbar", e);
        }
    }

    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new IOException("Autostart-Eintrag nicht entfernbar", e);
        }
    }
}
