using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WhisperLoom.App.Features.Selection;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Markierter Text des fokussierten Elements über UI Automation TextPattern (W2-SEL): <c>GetSelection</c> und den Text jedes
/// Bereichs lesen. Ohne TextPattern (Browser-Inhalte melden oft nur „Pane“, eigene Oberflächen) keine Auskunft → die Erfassung
/// fällt auf Strg+C zurück. Nur die benötigten Methoden sind deklariert, in der Vtable-Reihenfolge aus UIAutomationClient.idl
/// (wie <see cref="UiaCaretSource"/>).
/// </summary>
// Doku: learn.microsoft.com/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-getselection und
// .../nf-uiautomationclient-iuiautomationtextrange-gettext (maxLength -1 = ganzer Bereich) (abgerufen 2026-11-01).
[SupportedOSPlatform("windows")]
internal sealed class UiaSelectionSource : ISelectionTextSource
{
    private const int TextPatternId = 10014;

    // Doku: learn.microsoft.com/windows/win32/winauto/uiauto-automation-element-propids (UIA_IsPasswordPropertyId, abgerufen 2026-11-01).
    private const int IsPasswordPropertyId = 30019;

    /// <summary>Obergrenze je Bereich; mehr verarbeitet niemand am Stück, und der Speicher bleibt begrenzt.</summary>
    public const int MaxChars = 200_000;

    public UiaSelection? Selection()
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
            // W2-FIX V-3: Passwortfeld vor jedem Lesen ausschließen, auch wenn die Fokus-Prüfung davor „unbekannt“ meldete
            // (eigenes Passwort-Control mit TextPattern). Der Leser bricht dann ab, ohne Strg+C.
            if (element.GetCurrentPropertyValue(IsPasswordPropertyId, out var password) == 0 && password is true)
            {
                return new UiaSelection(HasTextPattern: false, "", IsPassword: true);
            }
            if (element.GetCurrentPattern(TextPatternId, out var pattern) != 0 || pattern is not IUIAutomationTextPattern text)
            {
                return new UiaSelection(HasTextPattern: false, "");
            }
            com.Add(text);
            if (text.GetSelection(out var ranges) != 0 || ranges is null || ranges.get_Length(out var count) != 0)
            {
                return new UiaSelection(HasTextPattern: false, "");
            }
            com.Add(ranges);
            var parts = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if (ranges.GetElement(i, out var range) != 0 || range is null)
                {
                    continue;
                }
                com.Add(range);
                if (range.GetText(MaxChars, out var part) == 0 && !string.IsNullOrEmpty(part))
                {
                    parts.Add(part);
                }
            }
            return new UiaSelection(HasTextPattern: true, string.Join("\n", parts));
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

        [PreserveSig]
        int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);

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
        void Clone();

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
    }
#pragma warning restore SYSLIB1096
}
