using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Platform.Windows;

/// <summary>
/// Windows: „Textgröße“ (Einstellungen → Barrierefreiheit) steht als DWORD <c>TextScaleFactor</c> (100–225) unter
/// <c>HKCU\Software\Microsoft\Accessibility</c>; fehlt der Wert, gilt 100 %. „Animationseffekte“ liefert
/// <c>SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)</c>. Tests geben einen eigenen Schlüssel unter <c>HKCU\Software</c> an.
/// </summary>
// Doku: learn.microsoft.com/uwp/api/windows.ui.viewmanagement.uisettings.textscalefactor (abgerufen 2026-11-02).
// Doku: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow (SPI_GETCLIENTAREAANIMATION, abgerufen 2026-11-02).
[SupportedOSPlatform("windows")]
public sealed class WindowsPreferences(string keyPath = WindowsPreferences.AccessibilityKey) : ISystemPreferences
{
    public const string AccessibilityKey = @"Software\Microsoft\Accessibility";
    public const string ValueName = "TextScaleFactor";
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public double TextScale
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(ValueName) is int percent ? SystemPreferences.Clamp(percent / 100.0) : 1.0;
        }
    }

    public bool AnimationsEnabled => !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0) || enabled;

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks (wie ProcessGuard.cs).
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);
#pragma warning restore SYSLIB1054
}
