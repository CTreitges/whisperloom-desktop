using System.Runtime.InteropServices;
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
internal sealed class ClipboardOutputSink(IProcessRunner runner) : IOutputSink
{
    public string Kind => "clipboard";

    public async Task WriteAsync(string text, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Clipboard.SetText(text);
            return;
        }
        var wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        (string File, string[] Args)[] tools = wayland
            ? [("wl-copy", []), ("xclip", ["-selection", "clipboard"])]
            : [("xclip", ["-selection", "clipboard"]), ("wl-copy", [])];
        foreach (var (file, args) in tools)
        {
            try
            {
                var r = await runner.RunAsync(file, args, text, ct).ConfigureAwait(false);
                if (r.ExitCode == 0)
                {
                    return;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Nicht installiert: nächstes Werkzeug.
            }
        }
        throw new IOException("Zwischenablage nicht erreichbar: wl-copy bzw. xclip fehlt oder scheitert.");
    }
}

// Doku: learn.microsoft.com/windows/win32/dataxchg/using-the-clipboard#copying-information-to-the-clipboard (abgerufen 2026-10-30).
[SupportedOSPlatform("windows")]
internal static class Win32Clipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public static void SetText(string text)
    {
        // Ein anderes Programm kann die Zwischenablage kurz halten: einige Versuche.
        for (var attempt = 0; !OpenClipboard(IntPtr.Zero); attempt++)
        {
            if (attempt == 10)
            {
                throw new IOException("Zwischenablage ist belegt.");
            }
            Thread.Sleep(50);
        }
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes);
            var target = GlobalLock(handle);
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0);
            GlobalUnlock(handle);
            if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
            {
                throw new IOException($"Zwischenablage: Schreiben fehlgeschlagen (Fehler {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);
#pragma warning restore SYSLIB1054
}
