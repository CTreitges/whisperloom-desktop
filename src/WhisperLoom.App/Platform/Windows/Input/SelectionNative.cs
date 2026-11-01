using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Win32-Abfragen für „Markierung verarbeiten“ (W2-SEL): Schreibmarke bzw. Mausposition als Ankerpunkt des Wählers, Fenster unter
/// einem Punkt (Strg+Rechtsklick) und das letzte fremde Vordergrundfenster (Tray-Eintrag; der Klick ins Tray nimmt den Fokus).
/// </summary>
// Doku: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getguithreadinfo (rcCaret in Client-Koordinaten von hwndCaret),
// .../nf-winuser-windowfrompoint, .../nf-winuser-getancestor (GA_ROOT) (abgerufen 2026-11-01).
[SupportedOSPlatform("windows")]
internal static class SelectionNative
{
    private const uint GaRoot = 2;

    /// <summary>Taskleiste und Tray-Überlauf: nie Ziel einer Markierung.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "Progman", "WorkerW",
    };

    /// <summary>Unterkante der Schreibmarke im Zielfenster, sonst die Mausposition (Bildschirm-Pixel).</summary>
    public static PixelPoint? Anchor(IntPtr target)
    {
        var thread = Win32Native.GetWindowThreadProcessId(target, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (thread != 0 && GetGUIThreadInfo(thread, ref info) && info.Caret != IntPtr.Zero)
        {
            var point = new Point { X = info.CaretRect.Left, Y = info.CaretRect.Bottom };
            if (ClientToScreen(info.Caret, ref point))
            {
                return new PixelPoint(point.X, point.Y);
            }
        }
        return GetCursorPos(out var cursor) ? new PixelPoint(cursor.X, cursor.Y) : null;
    }

    /// <summary>Hauptfenster unter dem Punkt.</summary>
    public static IntPtr WindowAt(PixelPoint at)
    {
        var hwnd = WindowFromPoint(new Point { X = at.X, Y = at.Y });
        return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GaRoot);
    }

    /// <summary>Vordergrundfenster, wenn es weder zu uns noch zur Taskleiste gehört; sonst <see cref="IntPtr.Zero"/>.</summary>
    public static IntPtr ExternalForeground()
    {
        var hwnd = Win32Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        Win32Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return IntPtr.Zero;
        }
        var name = new System.Text.StringBuilder(128);
        return GetClassNameW(hwnd, name, name.Capacity) > 0 && ShellClasses.Contains(name.ToString()) ? IntPtr.Zero : hwnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public Win32Native.Rect CaretRect;
    }

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks.
    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder name, int max);
#pragma warning restore SYSLIB1054
}
