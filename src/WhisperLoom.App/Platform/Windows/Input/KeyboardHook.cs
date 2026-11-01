using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static WhisperLoom.App.Platform.Windows.Input.Win32Native;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Globaler Tastatur-Hook (<c>WH_KEYBOARD_LL</c>) auf einem eigenen Thread mit Nachrichtenschleife. Jedes Ereignis geht
/// durch die <see cref="HotkeyStateMachine"/>; Aktionen meldet <paramref name="onAction"/> (auf dem Hook-Thread — der
/// Empfänger reicht sie selbst an den UI-Thread weiter und kehrt sofort zurück, sonst entfernt Windows den Hook).
/// </summary>
// Doku: learn.microsoft.com/windows/win32/winmsg/lowlevelkeyboardproc (abgerufen 2026-10-31): Rückgabe ≠ 0 schluckt
// das Ereignis; LLKHF_INJECTED; Zeitgrenze LowLevelHooksTimeout.
[SupportedOSPlatform("windows")]
public sealed class KeyboardHook : IDisposable
{
    private readonly HotkeyStateMachine _machine;
    private readonly Action<HotkeyAction, Hotkey?> _onAction;
    private readonly LowLevelKeyboardProc _proc;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;

    public KeyboardHook(HotkeyStateMachine machine, Action<HotkeyAction, Hotkey?> onAction)
    {
        _machine = machine;
        _onAction = onAction;
        _proc = Callback;
    }

    public bool IsRunning => _hook != IntPtr.Zero;

    /// <summary>Startet den Hook-Thread; false, wenn Windows den Hook verweigert.</summary>
    public bool Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "WhisperLoom keyboard hook" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
        return IsRunning;
    }

    /// <summary>Ändert die Zustandsmaschine threadsicher (Kombis, Overlay-Tasten, Rekorder).</summary>
    public void Update(Action<HotkeyStateMachine> change)
    {
        lock (_gate)
        {
            change(_machine);
        }
    }

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
        _hook = SetWindowsHookExW(WhKeyboardLl, _proc, GetModuleHandleW(null), 0);
        _started.Set();
        if (_hook == IntPtr.Zero)
        {
            return;
        }
        try
        {
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.Message == WmApp)
                {
                    // Nachgespieltes Loslassen von Win, davor die Maskentaste (nach dem Rücksprung aus dem Hook).
                    Send([Key(Hotkey.VkMaskKey, false), Key(Hotkey.VkMaskKey, true), Key((int)msg.WParam, true, extended: true)]);
                }
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
        if (nCode < 0)
        {
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var message = (int)wParam;
        var down = message is WmKeyDown or WmSysKeyDown;
        var vk = (int)data.VkCode;
        HookDecision decision;
        var resync = HotkeyAction.None;
        lock (_gate)
        {
            var own = data.ExtraInfo == InjectionMarker;
            if (!own)
            {
                resync = _machine.Resync(IsKeyDown, vk);
            }
            decision = _machine.OnKey(new KeyInput(vk, down, own));
        }
        if (resync != HotkeyAction.None)
        {
            _onAction(resync, null);
        }
        if (decision.Action != HotkeyAction.None)
        {
            _onAction(decision.Action, decision.Recorded);
        }
        if (decision.ReplayWinUpMasked)
        {
            PostThreadMessageW(_threadId, WmApp, vk, IntPtr.Zero);
        }
        return decision.Swallow ? 1 : CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}
