using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Rückfall über MSAA (IAccessible), wenn UI Automation nur „unbekannt“ liefert: Chrome und Edge melden ihren Webinhalt per UIA
/// als Pane (gemessen W2-OUT, Suite input), beantworten aber MSAA. Gelesen werden nur Rolle und Zustand des Objekts mit Fokus
/// (<c>STATE_SYSTEM_PROTECTED</c> = Passwortfeld), nie Name oder Wert. Ergebnis als <see cref="FocusInfo"/> in UIA-Begriffen.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/api/oleacc/nn-oleacc-iaccessible, .../winauto/object-state-constants und
// .../winauto/object-roles (abgerufen 2026-11-01).
[SupportedOSPlatform("windows")]
internal sealed class MsaaFocusSource : IFocusSource
{
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const int MaxDepth = 8;
    private static readonly Guid IidAccessible = new("618736e0-3c3d-11cf-810c-00aa00389b71");

    public FocusInfo? Focused()
    {
        var hwnd = FocusWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }
        var iid = IidAccessible;
        if (AccessibleObjectFromWindow(hwnd, ObjIdClient, ref iid, out var acc) != 0 || acc is null)
        {
            return null;
        }
        object child = 0;
        // Dem Fokus folgen, bis ein Objekt sich selbst (CHILDID_SELF) oder ein einfaches Kind meldet.
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            object? focus;
            try
            {
                focus = acc.get_accFocus();
            }
            catch (COMException)
            {
                break;
            }
            if (focus is IAccessible deeper && !ReferenceEquals(deeper, acc))
            {
                acc = deeper;
                child = 0;
                continue;
            }
            if (focus is int id)
            {
                child = id;
            }
            break;
        }
        var role = acc.get_accRole(child) is int r ? r : 0;
        var state = acc.get_accState(child) is int s ? s : 0;
        return FocusProbe.FromMsaa(role, state);
    }

    private static IntPtr FocusWindow()
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0, ref info) ? info.Focus : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

#pragma warning disable SYSLIB1054, SYSLIB1096 // DllImport/ComImport reichen; LibraryImport bzw. GeneratedComInterface bräuchten AllowUnsafeBlocks.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);

    /// <summary>IAccessible (oleacc.idl), Vtable-Reihenfolge bis get_accFocus.</summary>
    [ComImport]
    [Guid("618736e0-3c3d-11cf-810c-00aa00389b71")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IAccessible
    {
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object get_accParent();

        int get_accChildCount();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object get_accChild([MarshalAs(UnmanagedType.Struct)] object child);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_accName([MarshalAs(UnmanagedType.Struct)] object child);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_accValue([MarshalAs(UnmanagedType.Struct)] object child);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_accDescription([MarshalAs(UnmanagedType.Struct)] object child);

        [return: MarshalAs(UnmanagedType.Struct)]
        object get_accRole([MarshalAs(UnmanagedType.Struct)] object child);

        [return: MarshalAs(UnmanagedType.Struct)]
        object get_accState([MarshalAs(UnmanagedType.Struct)] object child);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_accHelp([MarshalAs(UnmanagedType.Struct)] object child);

        int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string helpFile, [MarshalAs(UnmanagedType.Struct)] object child);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_accKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object child);

        [return: MarshalAs(UnmanagedType.Struct)]
        object? get_accFocus();
    }
#pragma warning restore SYSLIB1054, SYSLIB1096
}
