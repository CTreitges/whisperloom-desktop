using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Tastatur-Messungen: Tab-Reihenfolge (wie die Tab-Taste) und ob der Tastaturfokus sichtbar ist – per Pixelvergleich des
/// gerenderten Fensters ohne Fokus und mit Tastaturfokus im Bereich um das Element (WCAG 2.4.7).
/// Doku: api/avalonia/input/focusmanager (TryMoveFocus, Focus, abgerufen 2026-11-02).
/// </summary>
internal static class UxFocus
{
    /// <summary>
    /// Elemente in Tab-Reihenfolge, bis sich der Kreis schließt (höchstens 400 Schritte): echte Tab-Tastendrücke über die
    /// Headless-Eingabe, also derselbe Weg wie beim Nutzer (KeyboardNavigationHandler), ab einem Fenster ohne Fokus.
    /// </summary>
    public static List<IInputElement> TabOrder(Control root)
    {
        var top = TopLevel.GetTopLevel(root)!;
        var focus = top.FocusManager!;
        focus.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
        var order = new List<IInputElement>();
        for (var i = 0; i < 400; i++)
        {
            top.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            if (focus.GetFocusedElement() is not { } current || order.Contains(current))
            {
                break;
            }
            order.Add(current);
        }
        focus.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
        return order;
    }

    /// <summary>
    /// true, wenn Tastaturfokus auf <paramref name="control"/> im Umkreis von 4 px mindestens so viele Pixel ändert wie ein
    /// halber 1-px-Ring um das Element – verglichen mit dem Fenster ohne Fokus auf dem Element.
    /// </summary>
    public static bool FocusVisible(Window window, Control control)
    {
        var focus = window.FocusManager!;
        focus.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
        var without = Capture(window);
        focus.Focus(control, NavigationMethod.Tab, KeyModifiers.None);
        var with = Capture(window);
        focus.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
        var box = control.TranslatePoint(default, window) is { } p ? new Rect(p, control.Bounds.Size).Inflate(4) : default;
        var changed = 0;
        var x0 = Math.Max(0, (int)box.X);
        var y0 = Math.Max(0, (int)box.Y);
        var x1 = Math.Min(without.Width, (int)Math.Ceiling(box.Right));
        var y1 = Math.Min(without.Height, (int)Math.Ceiling(box.Bottom));
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = y * without.Stride + x * 4;
                if (Math.Abs(without.Data[i] - with.Data[i]) + Math.Abs(without.Data[i + 1] - with.Data[i + 1])
                    + Math.Abs(without.Data[i + 2] - with.Data[i + 2]) > 48)
                {
                    changed++;
                }
            }
        }
        return changed >= control.Bounds.Width + control.Bounds.Height;
    }

    private sealed record Frame(int Width, int Height, int Stride, byte[] Data);

    private static Frame Capture(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("kein Frame");
        using var buffer = bitmap.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, data, 0, data.Length);
        return new Frame(buffer.Size.Width, buffer.Size.Height, buffer.RowBytes, data);
    }
}
