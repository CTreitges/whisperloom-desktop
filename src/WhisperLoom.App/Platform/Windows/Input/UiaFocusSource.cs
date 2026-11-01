using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Fokussiertes Element über UI Automation (UIAutomationCore, COM): Control-Type, IsPassword und ValuePattern („nur lesen“).
/// Liest nie Werte oder Namen. Nur die benötigten Methoden sind deklariert; die Plätze davor halten die Vtable-Reihenfolge
/// aus UIAutomationClient.idl.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-getfocusedelement,
// learn.microsoft.com/windows/win32/winauto/uiauto-automation-element-propids (abgerufen 2026-11-01).
[SupportedOSPlatform("windows")]
internal sealed class UiaFocusSource : IFocusSource
{
    private const int ProcessIdProperty = 30002;
    private const int ControlTypeProperty = 30003;
    private const int IsPasswordProperty = 30019;
    private const int IsValuePatternAvailableProperty = 30043;
    private const int ValueIsReadOnlyProperty = 30046;

    public FocusInfo? Focused()
    {
        var automation = (IUIAutomation)new CUIAutomation();
        try
        {
            if (automation.GetFocusedElement(out var element) != 0 || element is null)
            {
                return null;
            }
            try
            {
                var valueAvailable = Bool(element, IsValuePatternAvailableProperty);
                return new FocusInfo(
                    Int(element, ControlTypeProperty),
                    Bool(element, IsPasswordProperty),
                    valueAvailable,
                    valueAvailable && Bool(element, ValueIsReadOnlyProperty),
                    Int(element, ProcessIdProperty),
                    RuntimeId(element));
            }
            finally
            {
                Marshal.ReleaseComObject(element);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(automation);
        }
    }

    private static object? Value(IUIAutomationElement element, int property) =>
        element.GetCurrentPropertyValue(property, out var value) == 0 ? value : null;

    private static bool Bool(IUIAutomationElement element, int property) => Value(element, property) is true;

    /// <summary>W2-FIX M-4: RuntimeId (SAFEARRAY von int) als Text; eindeutig, solange das Element lebt.</summary>
    // Doku: learn.microsoft.com/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-getruntimeid (abgerufen 2026-11-01).
    private static string? RuntimeId(IUIAutomationElement element) =>
        element.GetRuntimeId(out var id) == 0 && id is { Length: > 0 } ? string.Join(".", id) : null;

    private static int Int(IUIAutomationElement element, int property) => Value(element, property) is int n ? n : 0;

    [ComImport]
    [Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation;

#pragma warning disable SYSLIB1096 // [ComImport] statt GeneratedComInterface: kein AllowUnsafeBlocks, kein Source-Generator nötig.
    [ComImport]
    [Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements();

        void CompareRuntimeIds();

        void GetRootElement();

        void ElementFromHandle();

        void ElementFromPoint();

        [PreserveSig]
        int GetFocusedElement(out IUIAutomationElement? element);
    }

    [ComImport]
    [Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();

        [PreserveSig]
        int GetRuntimeId([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] out int[]? runtimeId);

        void FindFirst();

        void FindAll();

        void FindFirstBuildCache();

        void FindAllBuildCache();

        void BuildUpdatedCache();

        [PreserveSig]
        int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
    }
#pragma warning restore SYSLIB1096
}
