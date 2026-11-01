using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WhisperLoom.App.Platform.Windows;

/// <summary>
/// URL-Schema <c>whisperloom://</c> für Einladungslinks (W2-ENR) unter <c>HKCU\Software\Classes\whisperloom</c>: nur der
/// eigene Benutzerzweig, keine Admin-Rechte. Ein Klick startet <c>"&lt;exe&gt;" "&lt;link&gt;"</c>; die App öffnet den
/// Assistenten mit Server und Code. Tests nutzen einen eigenen Schlüssel unter <c>HKCU\Software</c>.
/// </summary>
// Doku: learn.microsoft.com/previous-versions/windows/internet-explorer/ie-developer/platform-apis/aa767914(v=vs.85) (abgerufen 2026-11-01).
// Doku: learn.microsoft.com/windows/win32/shell/fa-intro (Per-User-Klassen unter HKCU\Software\Classes, abgerufen 2026-11-01).
[SupportedOSPlatform("windows")]
public sealed class JoinLinkScheme(string classesKey, string exePath)
{
    public const string ClassesKey = @"Software\Classes";

    public string Command => $"\"{exePath}\" \"%1\"";

    private string SchemeKey => $@"{classesKey}\{Management.JoinLink.Scheme}";

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(SchemeKey + @"\shell\open\command");
            return key?.GetValue(null) as string == Command;
        }
    }

    /// <summary>Schreibt bzw. erneuert den Eintrag (z. B. nach Umzug der App).</summary>
    /// <exception cref="IOException">Zweig nicht schreibbar.</exception>
    public void Register()
    {
        try
        {
            using (var scheme = Registry.CurrentUser.CreateSubKey(SchemeKey, writable: true))
            {
                scheme.SetValue(null, "URL:WhisperLoom", RegistryValueKind.String);
                scheme.SetValue("URL Protocol", "", RegistryValueKind.String);
            }
            using var command = Registry.CurrentUser.CreateSubKey(SchemeKey + @"\shell\open\command", writable: true);
            command.SetValue(null, Command, RegistryValueKind.String);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new IOException("URL-Schema nicht registrierbar", e);
        }
    }

    public void Unregister() => Registry.CurrentUser.DeleteSubKeyTree(SchemeKey, throwOnMissingSubKey: false);
}
