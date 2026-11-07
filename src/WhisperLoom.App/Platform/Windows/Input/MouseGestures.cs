namespace WhisperLoom.App.Platform.Windows.Input;

public enum MouseKind
{
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    Wheel,
}

/// <summary>Mauszeiger beim Drücken: verrät, ob die Geste auf Text begann.</summary>
public enum PressCursor
{
    /// <summary>Eigener Zeiger des Programms bzw. keine Auskunft.</summary>
    Unknown,

    /// <summary>System-Textzeiger (I-Beam): Druck auf Text.</summary>
    Text,

    /// <summary>Anderer System-Zeiger (Pfeil, Hand, Größe ändern …): Titelleiste, Datei, Scrollbalken – kein Text.</summary>
    Other,
}

/// <summary>Ein Maus-Ereignis aus dem Hook (Bildschirm-Pixel). <paramref name="Own"/> = von uns selbst injiziert.</summary>
/// <param name="Cursor">Mauszeiger beim Ereignis (nur bei <see cref="MouseKind.LeftDown"/> erhoben).</param>
public readonly record struct MouseInput(MouseKind Kind, int X, int Y, bool Ctrl, long TimeMs, bool Own = false,
    PressCursor Cursor = PressCursor.Unknown);

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

/// <param name="TextCursor">Nur bei <see cref="MouseAction.SelectionGesture"/>: die Geste begann mit dem Textzeiger.</param>
public sealed record MouseDecision(bool Swallow, MouseAction Action = MouseAction.None, int X = 0, int Y = 0, bool TextCursor = false)
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
/// <item>Markier-Geste: Ziehen um mindestens <see cref="DragPixels"/> oder Doppelklick – nicht, wenn beim Drücken ein anderer
/// System-Zeiger als der Textzeiger sichtbar war (<see cref="PressCursor.Other"/>, TP-WBUTTON).</item>
/// <item>Sichtbarer Knopf bzw. Wähler (<see cref="Transient"/>): Klick daneben oder Mausrad schließt.</item>
/// </list>
/// </summary>
public sealed class MouseGestures
{
    public const int DragPixels = 8;
    public const int DoubleClickMs = 500;
    public const int DoubleClickPixels = 4;

    private bool _rightSwallowed;
    private (int X, int Y, long T, PressCursor Cursor)? _leftDown;
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
                _leftDown = (e.X, e.Y, e.TimeMs, e.Cursor);
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
        // Mit Pfeil, Hand o. Ä. begonnen (Titelleiste, Datei, Scrollbalken): keine Markierung, auch wenn ein Textfeld mit alter
        // Markierung den Fokus hat (TP-WBUTTON).
        if (!(dragged || doubleClick) || d.Cursor == PressCursor.Other)
        {
            return MouseDecision.Pass;
        }
        return new MouseDecision(false, MouseAction.SelectionGesture, e.X, e.Y, TextCursor: d.Cursor == PressCursor.Text);
    }

    private MouseDecision Outside(MouseInput e) =>
        Transient.Count > 0 && !InTransient(e) ? new MouseDecision(false, MouseAction.Dismiss) : MouseDecision.Pass;

    private bool InTransient(MouseInput e) => Transient.Any(r => r.Contains(e.X, e.Y));
}
