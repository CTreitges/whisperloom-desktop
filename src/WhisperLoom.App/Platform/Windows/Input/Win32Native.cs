using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>Win32-Aufrufe für Hook, SendInput, Fenster und Zwischenablage (Phase 3).</summary>
// Doku: learn.microsoft.com/windows/win32/api/winuser/ (SetWindowsHookExW, LowLevelKeyboardProc, SendInput, INPUT,
// KEYBDINPUT, KBDLLHOOKSTRUCT, GetForegroundWindow, SetForegroundWindow), abgerufen 2026-10-31.
[SupportedOSPlatform("windows")]
internal static class Win32Native
{
    /// <summary>dwExtraInfo-Kennung eigener Eingaben ("WLOM"); der Hook lässt sie unverändert durch.</summary>
    public static readonly IntPtr InjectionMarker = 0x574C4F4D;

    public const int WhKeyboardLl = 13;
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;
    public const int WmQuit = 0x0012;
    public const int WmApp = 0x8000;
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    public const uint InputMouse = 0;
    public const uint InputKeyboard = 1;
    public const uint KeyeventfExtendedKey = 0x0001;
    public const uint KeyeventfKeyUp = 0x0002;
    public const uint KeyeventfUnicode = 0x0004;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeybdInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InputRecord
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static InputRecord Key(int vk, bool up, bool extended = false) => new()
    {
        Type = InputKeyboard,
        U = new InputUnion
        {
            Keyboard = new KeybdInput
            {
                Vk = (ushort)vk,
                Flags = (up ? KeyeventfKeyUp : 0) | (extended ? KeyeventfExtendedKey : 0),
                ExtraInfo = InjectionMarker,
            },
        },
    };

    public static InputRecord Unicode(char c, bool up) => new()
    {
        Type = InputKeyboard,
        U = new InputUnion
        {
            Keyboard = new KeybdInput { Scan = c, Flags = KeyeventfUnicode | (up ? KeyeventfKeyUp : 0), ExtraInfo = InjectionMarker },
        },
    };

    /// <summary>Schickt die Eingaben; true, wenn alle angenommen wurden.</summary>
    public static bool Send(InputRecord[] inputs) =>
        inputs.Length == 0 || SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<InputRecord>()) == inputs.Length;

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, InputRecord[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
#pragma warning restore SYSLIB1054
}
