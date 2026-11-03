using System.Diagnostics;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Platform.Linux;

/// <summary>
/// Linux: Die Textgröße des Desktops (GNOME <c>text-scaling-factor</c>, KDE <c>forceFontDPI</c>) landet in <c>Xft.dpi</c>, und
/// Avalonia skaliert unter X11/XWayland die ganze Oberfläche schon mit <c>Xft.dpi / 96</c>. Ein eigener Faktor würde die Schrift
/// doppelt vergrößern (125 % → 156 %), darum gilt hier immer 1,0 (Review UX-1). „Animationen“ kommen aus
/// <c>gsettings org.gnome.desktop.interface enable-animations</c>, einmal gelesen und gemerkt; scheitert das Lesen, gelten sie als an.
/// </summary>
// Doku: gitlab.gnome.org/GNOME/gsettings-desktop-schemas (org.gnome.desktop.interface.gschema.xml, abgerufen 2026-11-02).
// Doku: github.com/AvaloniaUI/Avalonia src/Avalonia.X11/Screens/X11Screens.Scaling.cs (XrdbScalingProvider, Xft.dpi; Review 2026-11-03).
public sealed class LinuxPreferences(Func<string, string?>? gsettings = null) : ISystemPreferences
{
    private readonly Lazy<bool> _animations = new(() => (gsettings ?? RunGsettings)("enable-animations")?.Trim() != "false");

    public double TextScale => 1.0;

    public bool AnimationsEnabled => _animations.Value;

    private static string? RunGsettings(string key)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("gsettings", ["get", "org.gnome.desktop.interface", key])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return null;
            }
            if (!process.WaitForExit(1000))
            {
                process.Kill(); // nur der eigene Kindprozess
                return null;
            }
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // gsettings fehlt (kein GNOME-Schema installiert)
        }
    }
}
