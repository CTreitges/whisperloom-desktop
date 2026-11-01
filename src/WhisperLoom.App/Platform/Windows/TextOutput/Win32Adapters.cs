using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WhisperLoom.App.Platform.Windows.Input;
using static WhisperLoom.App.Platform.Windows.Input.Win32Native;

namespace WhisperLoom.App.Platform.Windows.TextOutput;

/// <summary>SendInput mit eigener dwExtraInfo-Kennung (der eigene Hook lässt sie durch).</summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32KeySender : IKeySender
{
    private static readonly int[] Modifiers =
        [Hotkey.VkControl, Hotkey.VkMenu, Hotkey.VkShift, Hotkey.VkLWin, Hotkey.VkRWin];

    public async Task WaitForModifiersReleasedAsync(TimeSpan max, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        while (Modifiers.Any(IsKeyDown) && watch.Elapsed < max)
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    public bool SendCtrlV() =>
        Send([Key(Hotkey.VkControl, false), Key('V', false), Key('V', true), Key(Hotkey.VkControl, true)]);

    public bool SendEnter() => Send([Key(Hotkey.VkReturn, false), Key(Hotkey.VkReturn, true)]);

    // Doku: learn.microsoft.com/windows/win32/api/winuser/ns-winuser-keybdinput (KEYEVENTF_UNICODE, abgerufen 2026-10-31).
    public bool Type(string text)
    {
        var inputs = new List<InputRecord>(text.Length * 2);
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            if (c == '\r')
            {
                continue;
            }
            if (c == '\n')
            {
                // Umschalt+Enter: Zeilenumbruch, ohne dass Chats die Nachricht abschicken.
                inputs.AddRange([Key(Hotkey.VkShift, false), Key(Hotkey.VkReturn, false), Key(Hotkey.VkReturn, true), Key(Hotkey.VkShift, true)]);
                continue;
            }
            inputs.Add(Unicode(c, false));
            inputs.Add(Unicode(c, true));
        }
        return Send([.. inputs]);
    }
}

/// <summary>Zielfenster: Vordergrund, Existenz, UIPI-Schutz, nach vorn holen.</summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32Windows : IWindowAdapter
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;
    private const int SwRestore = 9;

    public IntPtr Foreground() => GetForegroundWindow();

    public bool Exists(IntPtr window) => IsWindow(window);

    // Doku: learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation (TokenElevation,
    // abgerufen 2026-10-31). Nicht lesbarer Token eines fremden Prozesses gilt als geschützt.
    public bool IsProtected(IntPtr window)
    {
        if (Elevated(Process.GetCurrentProcess().Handle) == true)
        {
            return false;
        }
        GetWindowThreadProcessId(window, out var pid);
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error() == 5;
        }
        try
        {
            return Elevated(process) ?? true;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public bool Activate(IntPtr window)
    {
        if (GetForegroundWindow() == window)
        {
            return true;
        }
        if (IsIconic(window))
        {
            ShowWindow(window, SwRestore);
        }
        // Eine eigene Eingabe macht uns zum Prozess mit der letzten Eingabe; erst dann erlaubt Windows den Wechsel.
        Send([Key(Hotkey.VkMaskKey, false), Key(Hotkey.VkMaskKey, true)]);
        SetForegroundWindow(window);
        for (var i = 0; i < 10 && GetForegroundWindow() != window; i++)
        {
            Thread.Sleep(20);
        }
        return GetForegroundWindow() == window;
    }

    public string ProcessName(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return "";
        }
    }

    // Doku: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getguithreadinfo (abgerufen 2026-10-31).
    public string FocusClass(IntPtr window)
    {
        var thread = GetWindowThreadProcessId(window, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (thread == 0 || !GetGUIThreadInfo(thread, ref info) || info.Focus == IntPtr.Zero)
        {
            return "";
        }
        var name = new System.Text.StringBuilder(256);
        return GetClassNameW(info.Focus, name, name.Capacity) > 0 ? name.ToString() : "";
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
        public Rect CaretRect;
    }

    private static bool? Elevated(IntPtr process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token))
        {
            return Marshal.GetLastWin32Error() == 5 ? true : null;
        }
        try
        {
            return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) ? elevated != 0 : null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder name, int max);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
#pragma warning restore SYSLIB1054
}

/// <summary>Fehler der Win32-Zwischenablage als <see cref="IOException"/> (wie die übrige Ausgabe).</summary>
internal sealed class ClipboardBusyException(string message) : IOException(message)
{
    public static ClipboardBusyException FromLastError(string what) =>
        new($"Zwischenablage: {what} ({new Win32Exception(Marshal.GetLastWin32Error()).NativeErrorCode})");
}
