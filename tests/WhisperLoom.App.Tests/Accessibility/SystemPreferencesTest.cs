using Microsoft.Win32;
using WhisperLoom.App.Design;
using WhisperLoom.App.Platform.Linux;
using WhisperLoom.App.Platform.Windows;

namespace WhisperLoom.App.Tests.Accessibility;

/// <summary>Textgröße und „Animationen anzeigen“ des Systems lesen (UX-1). Windows nur unter einem eigenen Test-Schlüssel.</summary>
public sealed class SystemPreferencesTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-prefs-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = @"Software\WhisperLoom-Test-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
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
    public void Linux_prefers_kde_font_dpi_over_gsettings()
    {
        Directory.CreateDirectory(_dir);
        var gnome = new LinuxPreferences(_dir, key => key == "text-scaling-factor" ? "1.25\n" : "true\n");
        Assert.Equal(1.25, gnome.TextScale);
        Assert.True(gnome.AnimationsEnabled);

        File.WriteAllText(Path.Combine(_dir, "kdeglobals"), "[General]\nfont=Noto Sans,10\nforceFontDPI=144\n");
        Assert.Equal(1.5, new LinuxPreferences(_dir, _ => "1.25").TextScale);
    }

    [Fact]
    public void Linux_without_any_setting_is_100_percent_with_animations()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "kcmfontsrc"), "[General]\nforceFontDPI=0\n");
        var none = new LinuxPreferences(_dir, _ => null);
        Assert.Equal(1.0, none.TextScale);
        Assert.True(none.AnimationsEnabled);
        Assert.False(new LinuxPreferences(_dir, key => key == "enable-animations" ? "false\n" : null).AnimationsEnabled);
    }

    [Theory]
    [InlineData("1.25\n", 1.25)]
    [InlineData("uint32 2", 2.0)]
    [InlineData("'abc'", null)]
    [InlineData(null, null)]
    public void Gsettings_output_is_parsed(string? output, double? expected) =>
        Assert.Equal(expected, LinuxPreferences.ParseDouble(output));
}
