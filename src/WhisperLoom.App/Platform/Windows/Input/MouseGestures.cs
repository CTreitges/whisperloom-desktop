namespace WhisperLoom.App.Platform.Windows.Input;

public enum MouseKind
{
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    Wheel,
}

/// <summary>Ein Maus-Ereignis aus dem Hook (Bildschirm-Pixel). <paramref name="Own"/> = von uns selbst injiziert.</summary>
public readonly record struct MouseInput(MouseKind Kind, int X, int Y, bool Ctrl, long TimeMs, bool Own = false);

public enum MouseAction
{
    None,

    /// <summary>Strg+Rechtsklick (geschluckt): Markierung prüfen, sonst den Rechtsklick nachspielen.</summary>
    CtrlRightClick,

    /// <summary>Linke Taste nach Ziehen bzw. Doppelklick losgelassen: vielleicht wurde Text markiert (schwebender Knopf).</summary>
    SelectionGesture,

    /// <summary>Klick neben Knopf bzw. Wähler oder Mausrad: schließen.</summary>
    Dismiss,
}

public sealed record MouseDecision(bool Swallow, MouseAction Action = MouseAction.None, int X = 0, int Y = 0)
{
    public static readonly MouseDecision Pass = new(false);
}

/// <summary>Rechteck in Bildschirm-Pixeln (rechts/unten exklusiv).</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>
/// Zustandsmaschine des Maus-Hooks (W2-SEL), ohne Win32 und ohne blockierende Arbeit: entscheidet je Ereignis in
/// Mikrosekunden, ob es geschluckt wird und welche Aktion der UI-Thread danach prüft. Nur vom Hook-Thread aufrufen; Einstellungen
/// ändert <see cref="MouseHook.Update"/> unter Sperre.
/// <list type="bullet">
/// <item>Strg+Rechtsklick: Drücken und Loslassen werden geschluckt; ob es eine Markierung gibt, klärt der UI-Thread (UIA bzw.
/// Strg+C) und spielt sonst den Rechtsklick nach – das normale Kontextmenü erscheint.</item>
/// <item>Markier-Geste: Ziehen um mindestens <see cref="DragPixels"/> oder Doppelklick.</item>
/// <item>Sichtbarer Knopf bzw. Wähler (<see cref="Transient"/>): Klick daneben oder Mausrad schließt.</item>
/// </list>
/// </summary>
public sealed class MouseGestures
{
    public const int DragPixels = 8;
    public const int DoubleClickMs = 500;
    public const int DoubleClickPixels = 4;

    private bool _rightSwallowed;
    private (int X, int Y, long T)? _leftDown;
    private (int X, int Y, long T)? _lastClick;

    public bool CtrlRightClick { get; set; }

    public bool FloatingButton { get; set; }

    /// <summary>Sichtbarer Knopf bzw. Wähler; leer = nichts sichtbar.</summary>
    public IReadOnlyList<ScreenRect> Transient { get; set; } = [];

    public MouseDecision OnMouse(MouseInput e)
    {
        if (e.Own)
        {
            return MouseDecision.Pass;
        }
        switch (e.Kind)
        {
            case MouseKind.RightDown when CtrlRightClick && e.Ctrl && !InTransient(e):
                _rightSwallowed = true;
                return new MouseDecision(true, MouseAction.CtrlRightClick, e.X, e.Y);
            case MouseKind.RightUp when _rightSwallowed:
                _rightSwallowed = false;
                return new MouseDecision(true);
            case MouseKind.RightDown:
                return Outside(e);
            case MouseKind.Wheel:
                return Transient.Count > 0 ? new MouseDecision(false, MouseAction.Dismiss) : MouseDecision.Pass;
            case MouseKind.LeftDown:
                _leftDown = (e.X, e.Y, e.TimeMs);
                return Outside(e);
            case MouseKind.LeftUp:
                return OnLeftUp(e);
            default:
                return MouseDecision.Pass;
        }
    }

    private MouseDecision OnLeftUp(MouseInput e)
    {
        var down = _leftDown;
        _leftDown = null;
        if (!FloatingButton || down is not { } d || InTransient(e))
        {
            return MouseDecision.Pass;
        }
        var dragged = Math.Abs(e.X - d.X) >= DragPixels || Math.Abs(e.Y - d.Y) >= DragPixels;
        var doubleClick = !dragged && _lastClick is { } c && e.TimeMs - c.T <= DoubleClickMs
                          && Math.Abs(e.X - c.X) <= DoubleClickPixels && Math.Abs(e.Y - c.Y) <= DoubleClickPixels;
        _lastClick = dragged || doubleClick ? null : (e.X, e.Y, e.TimeMs);
        return dragged || doubleClick ? new MouseDecision(false, MouseAction.SelectionGesture, e.X, e.Y) : MouseDecision.Pass;
    }

    private MouseDecision Outside(MouseInput e) =>
        Transient.Count > 0 && !InTransient(e) ? new MouseDecision(false, MouseAction.Dismiss) : MouseDecision.Pass;

    private bool InTransient(MouseInput e) => Transient.Any(r => r.Contains(e.X, e.Y));
}
