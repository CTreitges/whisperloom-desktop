using WhisperLoom.App.Setup;

namespace WhisperLoom.App.Platform.Linux;

/// <summary>
/// Autostart unter Linux nach der XDG-Autostart-Spezifikation: <c>whisperloom.desktop</c> in
/// <c>$XDG_CONFIG_HOME/autostart</c> (Standard <c>~/.config/autostart</c>). KDE und GNOME lesen ihn beim Anmelden.
/// </summary>
// Doku: specifications.freedesktop.org/autostart-spec/latest/ (abgerufen 2026-11-01).
// Doku: specifications.freedesktop.org/desktop-entry-spec/latest/exec-variables.html (abgerufen 2026-11-01).
public sealed class LinuxAutostart(string directory, string exePath) : IAutostart
{
    public const string FileName = "whisperloom.desktop";

    private readonly string _exePath = exePath;

    public string FilePath { get; } = Path.Combine(directory, FileName);

    public static string DefaultDirectory()
    {
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(config))
        {
            config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(config, "autostart");
    }

    public bool IsEnabled => File.Exists(FilePath);

    /// <summary>Datei vorhanden, aber ihre Exec-Zeile startet eine andere Datei (z. B. ein verschobenes AppImage).</summary>
    public bool PointsElsewhere
    {
        get
        {
            try
            {
                return File.Exists(FilePath) && !File.ReadAllLines(FilePath).Contains($"Exec={QuoteExec(_exePath)}", StringComparer.Ordinal);
            }
            catch (UnauthorizedAccessException e)
            {
                throw new IOException("Autostart-Datei nicht lesbar", e);
            }
        }
    }

    public void Enable()
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
            throw new IOException("Autostart-Datei nicht schreibbar", e);
        }
    }

    public void Disable()
    {
        try
        {
            File.Delete(FilePath); // fehlt sie, passiert nichts
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException("Autostart-Datei nicht entfernbar", e);
        }
    }

    /// <summary>Inhalt der .desktop-Datei; der Pfad steht gequotet nach Exec-Regeln der Spezifikation.</summary>
    public string Content() =>
        "[Desktop Entry]\n" +
        "Type=Application\n" +
        "Name=WhisperLoom\n" +
        "Comment=Dictation in the background\n" +
        $"Exec={QuoteExec(_exePath)}\n" +
        "Icon=whisperloom\n" +
        "Terminal=false\n" +
        "X-GNOME-Autostart-enabled=true\n";

    /// <summary>
    /// Exec-Argument: in doppelte Anführungszeichen, darin <c>" ` $ \</c> mit Backslash maskiert; danach verdoppelt die
    /// String-Regel der Datei jeden Backslash. <c>%</c> wird zu <c>%%</c> (sonst Feldcode).
    /// </summary>
    internal static string QuoteExec(string arg)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in arg)
        {
            switch (c)
            {
                case '"' or '`' or '$' or '\\':
                    sb.Append(@"\\").Append(c == '\\' ? @"\\" : c.ToString());
                    break;
                case '%':
                    sb.Append("%%");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
