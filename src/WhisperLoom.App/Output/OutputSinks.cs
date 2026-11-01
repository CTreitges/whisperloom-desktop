using System.Runtime.Versioning;
using WhisperLoom.App.Secrets;

namespace WhisperLoom.App.Output;

/// <summary>Wohin ein fertiges Diktat geht. Phase 2: Zwischenablage oder (Tests) eine Datei.</summary>
public interface IOutputSink
{
    /// <summary>Anzeige für den Status ("clipboard", "file").</summary>
    string Kind { get; }

    /// <exception cref="IOException">Ausgabe gescheitert.</exception>
    Task WriteAsync(string text, CancellationToken ct);
}

/// <summary>Test-Senke <c>--output-file</c>: überschreibt die Datei mit dem letzten Ergebnis (UTF-8).</summary>
public sealed class FileOutputSink(string path) : IOutputSink
{
    public string Kind => "file";

    public async Task WriteAsync(string text, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Zwischenablage ohne offenes Fenster (die App läuft meist nur im Tray): Windows über die Win32-Zwischenablage,
/// Linux über <c>wl-copy</c> (Wayland) bzw. <c>xclip</c> (X11). Diese Hilfsprogramme halten den Inhalt auch,
/// wenn kein Avalonia-Fenster offen ist; die Avalonia-Zwischenablage braucht ein TopLevel.
/// </summary>
internal sealed class ClipboardOutputSink(
    IProcessRunner runner,
    IReadOnlyList<(string File, string[] Args)>? tools = null,
    TimeSpan? timeout = null) : IOutputSink
{
    /// <summary>Zeitgrenze je Werkzeug; danach gilt die Ausgabe als gescheitert (Diktat bleibt im Verlauf/auf der Bühne).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public string Kind => "clipboard";

    /// <param name="ct">Abbruch; die Zeitgrenze gilt zusätzlich, auch bei <see cref="CancellationToken.None"/>.</param>
    public async Task WriteAsync(string text, CancellationToken ct)
    {
        if (tools is null && OperatingSystem.IsWindows())
        {
            await WindowsCopyAsync(text).ConfigureAwait(false);
            return;
        }
        foreach (var (file, args) in tools ?? LinuxTools())
        {
            try
            {
                // xclip/wl-copy lassen ein Kind mit offenen Handles zurück: nur auf den Exit des Elternprozesses warten.
                if (await runner.RunUntilExitAsync(file, args, text, timeout ?? DefaultTimeout, ct).ConfigureAwait(false) == 0)
                {
                    return;
                }
            }
            catch (TimeoutException e)
            {
                throw new IOException($"Zwischenablage: {file} antwortet nicht.", e);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Nicht installiert: nächstes Werkzeug.
            }
        }
        throw new IOException("Zwischenablage nicht erreichbar: wl-copy bzw. xclip fehlt oder scheitert.");
    }

    /// <summary>
    /// Windows: über die Win32-Zwischenablage (sofort gerendert, nicht OLE mit verzögertem Rendern wie Avalonia), mit
    /// Wiederholungen solange ein anderes Programm sie hält und Gegenprobe (W2-OUT, Ursache „Kopieren unzuverlässig“).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static async Task WindowsCopyAsync(string text)
    {
        var clipboard = new Platform.Windows.TextOutput.Win32TextClipboard();
        var copier = new ClipboardCopier(t =>
        {
            clipboard.SetText(t, @private: false);
            return Task.CompletedTask;
        }, () => Task.FromResult(clipboard.GetText()));
        if (!await copier.CopyAsync(text).ConfigureAwait(false))
        {
            throw new IOException("Zwischenablage ist belegt.");
        }
    }

    private static (string File, string[] Args)[] LinuxTools() =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            ? [("xclip", ["-selection", "clipboard"]), ("wl-copy", [])]
            : [("wl-copy", []), ("xclip", ["-selection", "clipboard"])];
}
