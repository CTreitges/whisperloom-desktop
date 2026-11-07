using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Welcher Mauszeiger beim Drücken sichtbar ist (TP-WBUTTON): der System-Textzeiger (I-Beam) heißt „auf Text“, andere
/// System-Zeiger (Pfeil, Hand, Größe ändern, Warten …) heißen „kein Text“ – Titelleiste, Datei, Scrollbalken. Programme mit eigenem
/// Zeiger bleiben „unbekannt“. Ein Aufruf von <c>GetCursorInfo</c> ohne Warten; darf im Maus-Hook-Callback laufen.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getcursorinfo, .../ns-winuser-cursorinfo (CURSOR_SHOWING = 1)
// und .../nf-winuser-loadcursorw (IDC_*-Werte; mit hInstance NULL geteilte System-Zeiger, gleicher Handle) (abgerufen 2026-11-06).
public static class PressCursorProbe
{
    private const int IdcIBeam = 32513;

    // IDC_ARROW, WAIT, CROSS, UPARROW, SIZENWSE, SIZENESW, SIZEWE, SIZENS, SIZEALL, NO, HAND, APPSTARTING, HELP.
    private static readonly int[] OtherIds = [32512, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651];

    private static (IntPtr Text, HashSet<IntPtr> Other)? _system;

    /// <summary>Ordnet einen Zeiger-Handle ein; <see cref="IntPtr.Zero"/> bzw. unsichtbar = unbekannt.</summary>
    public static PressCursor Classify(IntPtr cursor, bool showing, IntPtr textCursor, IReadOnlySet<IntPtr> otherCursors)
    {
        if (!showing || cursor == IntPtr.Zero)
        {
            return PressCursor.Unknown;
        }
        if (cursor == textCursor)
        {
            return PressCursor.Text;
        }
        return otherCursors.Contains(cursor) ? PressCursor.Other : PressCursor.Unknown;
    }

    /// <summary>Zeiger jetzt (Hook-Thread, bei Linksdruck).</summary>
    [SupportedOSPlatform("windows")]
    public static PressCursor Current()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info))
        {
            return PressCursor.Unknown;
        }
        var system = _system ??= (LoadCursorW(IntPtr.Zero, IdcIBeam), [.. OtherIds.Select(id => LoadCursorW(IntPtr.Zero, id)).Where(h => h != IntPtr.Zero)]);
        return Classify(info.Cursor, (info.Flags & 1) != 0, system.Text, system.Other);
    }

    /// <summary>Geteilter System-Textzeiger (Tests).</summary>
    [SupportedOSPlatform("windows")]
    public static IntPtr SystemTextCursor() => LoadCursorW(IntPtr.Zero, IdcIBeam);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public int X;
        public int Y;
    }

#pragma warning disable SYSLIB1054 // DllImport reicht hier (wie Win32Native); LibraryImport bräuchte AllowUnsafeBlocks.
    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr instance, int cursorName);
#pragma warning restore SYSLIB1054
}
