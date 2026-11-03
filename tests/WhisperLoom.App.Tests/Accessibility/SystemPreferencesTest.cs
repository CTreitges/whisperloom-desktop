using Microsoft.Win32;
using WhisperLoom.App.Design;
using WhisperLoom.App.Platform.Linux;
using WhisperLoom.App.Platform.Windows;

namespace WhisperLoom.App.Tests.Accessibility;

/// <summary>Textgröße und „Animationen anzeigen“ des Systems lesen (UX-1). Windows nur unter einem eigenen Test-Schlüssel.</summary>
public sealed class SystemPreferencesTest : IDisposable
{
    private readonly string _key = @"Software\WhisperLoom-Test-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
        }
    }

    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(2.0, 2.0)]
    [InlineData(3.0, 2.25)]
    [InlineData(0.5, 1.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(double.NaN, 1.0)]
    public void Clamp_keeps_the_factor_between_100_and_225_percent(double read, double expected) =>
        Assert.Equal(expected, SystemPreferences.Clamp(read));

    [Fact]
    public void Windows_reads_TextScaleFactor_percent_and_defaults_to_100()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Registry nur unter Windows");
            return;
        }
        var prefs = new WindowsPreferences(_key);
        Assert.Equal(1.0, prefs.TextScale); // Schlüssel fehlt
        using (var key = Registry.CurrentUser.CreateSubKey(_key))
        {
            key.SetValue(WindowsPreferences.ValueName, 175, RegistryValueKind.DWord);
        }
        Assert.Equal(1.75, prefs.TextScale);
        using (var key = Registry.CurrentUser.CreateSubKey(_key))
        {
            key.SetValue(WindowsPreferences.ValueName, 400, RegistryValueKind.DWord);
        }
        Assert.Equal(2.25, prefs.TextScale);
    }

    [Fact]
    public void Linux_leaves_text_size_to_avalonia_even_with_a_desktop_factor()
    {
        // GNOME 125 % bzw. KDE forceFontDPI stecken schon in Xft.dpi, mit dem Avalonia die ganze Oberfläche skaliert.
        var prefs = new LinuxPreferences(key => key == "text-scaling-factor" ? "1.25\n" : "true\n");
        Assert.Equal(1.0, prefs.TextScale);
    }

    [Fact]
    public void Linux_reads_animations_once_and_defaults_to_on()
    {
        var calls = 0;
        var off = new LinuxPreferences(key =>
        {
            calls++;
            return key == "enable-animations" ? "false\n" : null;
        });
        Assert.False(off.AnimationsEnabled);
        Assert.False(off.AnimationsEnabled);
        Assert.Equal(1, calls);
        Assert.True(new LinuxPreferences(_ => null).AnimationsEnabled);
        Assert.True(new LinuxPreferences(_ => "true\n").AnimationsEnabled);
    }
}
