using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static WhisperLoom.App.Platform.Windows.Input.Win32Native;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>Globaler Maus-Hook der Markierungs-Starter; Windows: <see cref="MouseHook"/>, Tests: Fake.</summary>
public interface IMouseHook : IDisposable
{
    /// <returns>false, wenn Windows den Hook verweigert.</returns>
    bool Start();

    /// <summary>Ändert die Zustandsmaschine threadsicher (Starter an/aus, sichtbare Fenster).</summary>
    void Update(Action<MouseGestures> change);
}

/// <summary>
/// Globaler Maus-Hook (<c>WH_MOUSE_LL</c>, W2-SEL) auf einem eigenen Thread mit Nachrichtenschleife – nur installiert, solange
/// ein Maus-Starter (schwebender Knopf, Strg+Rechtsklick) an ist. Der Callback tut nichts Blockierendes: Mausbewegungen gehen
/// sofort weiter, alles andere entscheidet <see cref="MouseGestures"/> (Mikrosekunden); Aktionen meldet
/// <paramref name="onAction"/> auf dem Hook-Thread – der Empfänger reicht sie per Post an den UI-Thread weiter. Sonst entfernt
/// Windows den Hook nach <c>LowLevelHooksTimeout</c>.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc und .../api/winuser/ns-winuser-msllhookstruct
// (Rückgabe ≠ 0 schluckt; Zeitgrenze; dwExtraInfo) (abgerufen 2026-11-01).
[SupportedOSPlatform("windows")]
public sealed class MouseHook(MouseGestures gestures, Action<MouseDecision> onAction) : IMouseHook
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;
    private const uint MouseeventfRightDown = 0x0008;
    private const uint MouseeventfRightUp = 0x0010;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _started = new();
    private LowLevelKeyboardProc? _proc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;

    public bool IsRunning => _hook != IntPtr.Zero;

    public bool Start()
    {
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "WhisperLoom mouse hook" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
        return IsRunning;
    }

    public void Update(Action<MouseGestures> change)
    {
        lock (_gate)
        {
            change(gestures);
        }
    }

    /// <summary>Rechtsklick an der aktuellen Mausposition nachspielen (keine Markierung → normales Kontextmenü).</summary>
    public static bool ReplayRightClick() => Send(
    [
        new InputRecord { Type = InputMouse, U = new InputUnion { Mouse = new Win32Native.MouseInput { Flags = MouseeventfRightDown, ExtraInfo = InjectionMarker } } },
        new InputRecord { Type = InputMouse, U = new InputUnion { Mouse = new Win32Native.MouseInput { Flags = MouseeventfRightUp, ExtraInfo = InjectionMarker } } },
    ]);

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
        _started.Dispose();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookExW(WhMouseLl, _proc!, GetModuleHandleW(null), 0);
        _started.Set();
        if (_hook == IntPtr.Zero)
        {
            return;
        }
        try
        {
            while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
        }
        finally
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var message = (int)wParam;
        if (nCode < 0 || message == WmMouseMove || Kind(message) is not { } kind)
        {
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
        var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
        var input = new MouseInput(kind, data.X, data.Y, IsKeyDown(Hotkey.VkControl), data.Time, data.ExtraInfo == InjectionMarker);
        MouseDecision decision;
        lock (_gate)
        {
            decision = gestures.OnMouse(input);
        }
        if (decision.Action != MouseAction.None)
        {
            onAction(decision);
        }
        return decision.Swallow ? 1 : CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static MouseKind? Kind(int message) => message switch
    {
        WmLButtonDown => MouseKind.LeftDown,
        WmLButtonUp => MouseKind.LeftUp,
        WmRButtonDown => MouseKind.RightDown,
        WmRButtonUp => MouseKind.RightUp,
        WmMouseWheel or WmMouseHWheel => MouseKind.Wheel,
        _ => null,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
