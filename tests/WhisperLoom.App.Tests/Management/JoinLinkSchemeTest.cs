using Microsoft.Win32;

namespace WhisperLoom.App.Tests.Management;

/// <summary>URL-Schema <c>whisperloom://</c> je Benutzer: Windows-Registry (eigener Testzweig), Linux-.desktop-Handler.</summary>
public sealed class JoinLinkSchemeTest
{
    [Fact]
    public void Windows_registriert_das_Schema_unter_HKCU_ohne_Admin()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("nur Windows");
            return;
        }
        var root = $@"Software\WhisperLoom-Test-W2-ENR-{Guid.NewGuid():N}\Classes";
        var scheme = new Platform.Windows.JoinLinkScheme(root, @"C:\Program Files\WhisperLoom\WhisperLoom.exe");
        try
        {
            Assert.False(scheme.IsRegistered);
            scheme.Register();
            Assert.True(scheme.IsRegistered);
            using var key = Registry.CurrentUser.OpenSubKey(root + @"\whisperloom");
            Assert.Equal("URL:WhisperLoom", key!.GetValue(null));
            Assert.Equal("", key.GetValue("URL Protocol"));
            using var command = Registry.CurrentUser.OpenSubKey(root + @"\whisperloom\shell\open\command");
            Assert.Equal("\"C:\\Program Files\\WhisperLoom\\WhisperLoom.exe\" \"%1\"", command!.GetValue(null));
            // Neuer Pfad (App umgezogen) gilt wieder als „nicht registriert“ und wird überschrieben.
            Assert.False(new Platform.Windows.JoinLinkScheme(root, @"D:\WL\WhisperLoom.exe").IsRegistered);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root.Split('\\')[0] + "\\" + root.Split('\\')[1], throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Linux_schreibt_den_Handler_und_setzt_ihn_als_Standard()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-scheme-" + Guid.NewGuid().ToString("N"));
        var calls = new List<string>();
        try
        {
            var scheme = new Platform.Linux.JoinLinkScheme(dir, "/opt/White Loom/$WhisperLoom", (file, args) =>
            {
                calls.Add(file + " " + string.Join(' ', args));
                return true;
            });
            Assert.True(scheme.Register());
            var text = File.ReadAllText(Path.Combine(dir, "whisperloom-join.desktop"));
            Assert.Contains("MimeType=x-scheme-handler/whisperloom;", text);
            Assert.Contains("Exec=\"/opt/White Loom/\\$WhisperLoom\" %u", text);
            Assert.Contains("NoDisplay=true", text);
            Assert.Equal(["xdg-mime default whisperloom-join.desktop x-scheme-handler/whisperloom"], calls);

            var without = new Platform.Linux.JoinLinkScheme(dir, "/opt/wl", (_, _) => false);
            Assert.False(without.Register()); // ohne xdg-mime: Datei liegt trotzdem
            Assert.True(File.Exists(without.FilePath));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
