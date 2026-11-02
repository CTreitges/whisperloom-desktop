using System.Diagnostics;
using System.Globalization;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Platform.Linux;

/// <summary>
/// Linux: KDE Plasma setzt die Schriftgröße über <c>forceFontDPI</c> in <c>~/.config/kcmfontsrc</c> bzw. <c>kdeglobals</c>
/// (Faktor = DPI / 96); GNOME und andere über <c>gsettings org.gnome.desktop.interface text-scaling-factor</c> und
/// <c>enable-animations</c>. Einmal je Abfrage gelesen; scheitert das Lesen, gilt 100 % und Animationen an.
/// </summary>
// Doku: gitlab.gnome.org/GNOME/gsettings-desktop-schemas (org.gnome.desktop.interface.gschema.xml, abgerufen 2026-11-02).
// Doku: invent.kde.org/plasma/plasma-workspace (kcms/fonts, forceFontDPI, abgerufen 2026-11-02).
public sealed class LinuxPreferences(string? configHome = null, Func<string, string?>? gsettings = null) : ISystemPreferences
{
    private readonly string _config = configHome ?? (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
        ? x
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"));

    private readonly Func<string, string?> _gsettings = gsettings ?? RunGsettings;

    public double TextScale =>
        KdeFontDpi(_config) is { } dpi ? SystemPreferences.Clamp(dpi / 96.0)
        : ParseDouble(_gsettings("text-scaling-factor")) is { } f ? SystemPreferences.Clamp(f)
        : 1.0;

    public bool AnimationsEnabled => _gsettings("enable-animations")?.Trim() != "false";

    /// <summary><c>forceFontDPI</c> aus <c>kcmfontsrc</c> oder <c>kdeglobals</c>; 0 oder fehlend = nicht gesetzt.</summary>
    public static int? KdeFontDpi(string configHome)
    {
        foreach (var file in new[] { "kcmfontsrc", "kdeglobals" })
        {
            var path = Path.Combine(configHome, file);
            if (!File.Exists(path))
            {
                continue;
            }
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0] == "forceFontDPI" && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var dpi) && dpi > 0)
                {
                    return dpi;
                }
            }
        }
        return null;
    }

    /// <summary>Ausgabe von <c>gsettings get</c> („1.25“, „uint32 1“) als Zahl.</summary>
    public static double? ParseDouble(string? output)
    {
        var token = output?.Trim().Split(' ').LastOrDefault();
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

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
