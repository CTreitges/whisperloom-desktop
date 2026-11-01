using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Zeichen vor der Schreibmarke über UI Automation TextPattern: Auswahl holen, auf ihren Anfang zusammenziehen, ein Zeichen
/// zurück, dieses eine Zeichen lesen. Mehr Text wird nie gelesen; ohne TextPattern (Browser melden „Pane“, viele eigene
/// Oberflächen) gibt es keine Antwort. Nur die benötigten Methoden sind deklariert, in der Vtable-Reihenfolge aus
/// UIAutomationClient.idl.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/api/uiautomationclient/nn-uiautomationclient-iuiautomationtextpattern,
// .../nn-uiautomationclient-iuiautomationtextrange (MoveEndpointByRange, MoveEndpointByUnit, GetText) (abgerufen 2026-11-01).
[SupportedOSPlatform("windows")]
internal sealed class UiaCaretSource : ICaretSource
{
    private const int TextPatternId = 10014;
    private const int EndpointStart = 0;
    private const int EndpointEnd = 1;
    private const int UnitCharacter = 0;

    public char? CharBeforeCaret()
    {
        var automation = (IUIAutomation)new CUIAutomation();
        var com = new List<object> { automation };
        try
        {
            if (automation.GetFocusedElement(out var element) != 0 || element is null)
            {
                return null;
            }
            com.Add(element);
            if (element.GetCurrentPattern(TextPatternId, out var pattern) != 0 || pattern is not IUIAutomationTextPattern text)
            {
                return null;
            }
            com.Add(text);
            if (text.GetSelection(out var ranges) != 0 || ranges is null)
            {
                return null;
            }
            com.Add(ranges);
            if (ranges.get_Length(out var count) != 0 || count < 1 || ranges.GetElement(0, out var selection) != 0 || selection is null)
            {
                return null;
            }
            com.Add(selection);
            if (selection.Clone(out var probe) != 0 || probe is null)
            {
                return null;
            }
            com.Add(probe);
            // Ende auf den Anfang der Auswahl ziehen (leerer Bereich an der Schreibmarke), dann den Anfang ein Zeichen zurück.
            if (probe.MoveEndpointByRange(EndpointEnd, selection, EndpointStart) != 0
                || probe.MoveEndpointByUnit(EndpointStart, UnitCharacter, -1, out var moved) != 0 || moved == 0)
            {
                return null;
            }
            return probe.GetText(1, out var before) == 0 && before is { Length: > 0 } ? before[0] : null;
        }
        finally
        {
            foreach (var o in com)
            {
                Marshal.ReleaseComObject(o);
            }
        }
    }

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

        void GetRuntimeId();

        void FindFirst();

        void FindAll();

        void FindFirstBuildCache();

        void FindAllBuildCache();

        void BuildUpdatedCache();

        void GetCurrentPropertyValue();

        void GetCurrentPropertyValueEx();

        void GetCachedPropertyValue();

        void GetCachedPropertyValueEx();

        void GetCurrentPatternAs();

        void GetCachedPatternAs();

        [PreserveSig]
        int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
    }

    [ComImport]
    [Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        void RangeFromPoint();

        void RangeFromChild();

        [PreserveSig]
        int GetSelection(out IUIAutomationTextRangeArray? ranges);
    }

    [ComImport]
    [Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        [PreserveSig]
        int get_Length(out int length);

        [PreserveSig]
        int GetElement(int index, out IUIAutomationTextRange? range);
    }

    [ComImport]
    [Guid("a543cc6a-f4ae-494b-8239-c814481187a8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        [PreserveSig]
        int Clone(out IUIAutomationTextRange? clone);

        void Compare();

        void CompareEndpoints();

        void ExpandToEnclosingUnit();

        void FindAttribute();

        void FindText();

        void GetAttributeValue();

        void GetBoundingRectangles();

        void GetEnclosingElement();

        [PreserveSig]
        int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string? text);

        void Move();

        [PreserveSig]
        int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);

        [PreserveSig]
        int MoveEndpointByRange(int endpoint, IUIAutomationTextRange range, int targetEndpoint);
    }
#pragma warning restore SYSLIB1096
}
