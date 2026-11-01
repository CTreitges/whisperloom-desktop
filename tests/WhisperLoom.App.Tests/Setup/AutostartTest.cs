using Microsoft.Win32;
using WhisperLoom.App.Platform.Linux;
using WhisperLoom.App.Platform.Windows;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>
/// Autostart-Schalter: schreibt und entfernt den Eintrag. Windows gegen <c>HKCU\Software\WhisperLoom-Test-WLD-P6b\Run</c>
/// (danach gelöscht), Linux gegen ein Temp-Verzeichnis. Nie gegen den echten Run-Schlüssel bzw. ~/.config/autostart.
/// </summary>
public sealed class AutostartTest
{
    private const string TestRoot = @"Software\WhisperLoom-Test-WLD-P6b";
    private const string TestKey = TestRoot + @"\Run";

    [Fact]
    public void Windows_schreibt_und_entfernt_den_Run_Eintrag()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("nur Windows");
            return;
        }
        var exe = @"C:\Users\Test User\AppData\Local\WhisperLoom\current\WhisperLoom.exe";
        var autostart = new WindowsAutostart(TestKey, exe);
        try
        {
            Assert.False(autostart.IsEnabled);
            autostart.Enable();
            Assert.True(autostart.IsEnabled);
            using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
            {
                Assert.Equal($"\"{exe}\"", key?.GetValue(WindowsAutostart.ValueName));
            }
            autostart.Enable(); // zweimal schadet nicht
            autostart.Disable();
            Assert.False(autostart.IsEnabled);
            using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
            {
                Assert.Null(key?.GetValue(WindowsAutostart.ValueName));
            }
            autostart.Disable(); // fehlender Eintrag: kein Fehler
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);
        }
        Assert.Null(Registry.CurrentUser.OpenSubKey(TestRoot));
    }

    [Fact]
    public void Linux_schreibt_und_entfernt_die_Desktop_Datei()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-autostart-" + Guid.NewGuid().ToString("N"), "autostart");
        try
        {
            var autostart = new LinuxAutostart(dir, "/home/test/Apps/WhisperLoom.AppImage");
            Assert.False(autostart.IsEnabled);
            autostart.Enable();
            Assert.True(autostart.IsEnabled);
            var text = File.ReadAllText(Path.Combine(dir, "whisperloom.desktop"));
            Assert.Contains("[Desktop Entry]", text);
            Assert.Contains("Type=Application", text);
            Assert.Contains("Name=WhisperLoom", text);
            Assert.Contains("Exec=\"/home/test/Apps/WhisperLoom.AppImage\"", text);
            autostart.Disable();
            Assert.False(autostart.IsEnabled);
            Assert.False(File.Exists(Path.Combine(dir, "whisperloom.desktop")));
            autostart.Disable();
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(dir)))
            {
                Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
            }
        }
    }

    [Fact]
    public void Linux_Exec_quotet_Sonderzeichen_nach_Spezifikation()
    {
        var autostart = new LinuxAutostart("/tmp/unused", "/opt/my apps/50%/Whisper\"Loom$1");
        Assert.Contains("Exec=\"/opt/my apps/50%%/Whisper\\\\\"Loom\\\\$1\"", autostart.Content());
    }
}
