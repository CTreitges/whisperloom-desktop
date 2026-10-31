using Avalonia.Controls;

namespace WhisperLoom.App.Platform.Windows.Overlay;

/// <summary>
/// Fenster, die nie den Fokus nehmen (Overlay, Pegel-Pille, Hinweis): <c>ShowActivated=false</c>, die erweiterten
/// Stile <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST</c> und <c>WM_MOUSEACTIVATE → MA_NOACTIVATE</c>, damit auch
/// ein Klick auf einen Knopf das Zielfenster im Vordergrund lässt.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/winmsg/extended-window-styles und .../inputdev/wm-mouseactivate (abgerufen
// 2026-10-31); Avalonia Win32Properties.AddWindowStylesCallback/AddWndProcHookCallback (Avalonia.Controls.xml 12.1.4).
public static class NoActivate
{
    public const uint WsExTopmost = 0x00000008;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExAppWindow = 0x00040000;
    public const uint WsExNoActivate = 0x08000000;
    public const uint WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    /// <summary>Erweiterte Stile eines fokusfreien Fensters; WS_EX_APPWINDOW fällt weg (kein Taskleisten-Eintrag).</summary>
    public static uint ExStyle(uint exStyle) => (exStyle | WsExNoActivate | WsExToolWindow | WsExTopmost) & ~WsExAppWindow;

    /// <summary>WndProc-Hook: Mausklicks aktivieren das Fenster nicht.</summary>
    public static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }
        return IntPtr.Zero;
    }

    /// <summary>Richtet ein Avalonia-Fenster fokusfrei ein (vor dem ersten Show aufrufen).</summary>
    public static void Apply(Window window)
    {
        window.ShowActivated = false;
        window.Topmost = true;
        window.ShowInTaskbar = false;
        window.CanResize = false;
        window.WindowDecorations = WindowDecorations.None;
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWindowStylesCallback(window, (style, exStyle) => (style, ExStyle(exStyle)));
            Win32Properties.AddWndProcHookCallback(window, WndProc);
        }
    }
}
