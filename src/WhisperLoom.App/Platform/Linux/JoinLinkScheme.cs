using System.Diagnostics;

namespace WhisperLoom.App.Platform.Linux;

/// <summary>
/// Einladungslinks <c>whisperloom://</c> unter Linux (W2-ENR): <c>whisperloom-join.desktop</c> in
/// <c>$XDG_DATA_HOME/applications</c> (Standard <c>~/.local/share/applications</c>) mit
/// <c>MimeType=x-scheme-handler/whisperloom</c>, danach <c>xdg-mime default</c>. Nur das eigene Benutzerkonto.
/// Ohne xdg-utils bleibt die Eingabe von Server und Code im Assistenten.
/// </summary>
// Doku: specifications.freedesktop.org/desktop-entry-spec/latest/exec-variables.html (abgerufen 2026-11-01).
// Doku: specifications.freedesktop.org/mime-apps-spec/latest/ (x-scheme-handler, abgerufen 2026-11-01).
public sealed class JoinLinkScheme(string directory, string exePath, Func<string, string[], bool>? run = null)
{
    public const string FileName = "whisperloom-join.desktop";

    public const string MimeType = "x-scheme-handler/" + Management.JoinLink.Scheme;

    public string FilePath { get; } = Path.Combine(directory, FileName);

    public static string DefaultDirectory()
    {
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(data))
        {
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }
        return Path.Combine(data, "applications");
    }

    /// <summary>Inhalt der Datei; <c>%u</c> = der angeklickte Link.</summary>
    public string Content() =>
        "[Desktop Entry]\n" +
        "Type=Application\n" +
        "Name=WhisperLoom\n" +
        "NoDisplay=true\n" +
        $"Exec={Quote(exePath)} %u\n" +
        $"MimeType={MimeType};\n";

    /// <summary>Schreibt die Datei und setzt sie als Standard für das Schema.</summary>
    /// <returns>false, wenn <c>xdg-mime</c> fehlt oder scheitert (Datei liegt trotzdem).</returns>
    /// <exception cref="IOException">Datei nicht schreibbar.</exception>
    public bool Register()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Content());
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException("Datei für Einladungslinks nicht schreibbar", e);
        }
        return (run ?? Run)("xdg-mime", ["default", FileName, MimeType]);
    }

    /// <summary>Exec-Regel der Desktop-Spezifikation: in Anführungszeichen, <c>" ` $ \</c> mit Backslash.</summary>
    internal static string Quote(string path)
    {
        var escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        return $"\"{escaped}\"";
    }

    private static bool Run(string file, string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args)
            {
                info.ArgumentList.Add(a);
            }
            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }
            if (!process.WaitForExit(5000))
            {
                process.Kill(); // nur der eigene Kindprozess
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
