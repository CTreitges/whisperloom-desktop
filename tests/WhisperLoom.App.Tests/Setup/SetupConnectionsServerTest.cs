using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Tests.Settings;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>
/// TP-WSETUP: Der eigene Server steht auf „Verbindungen“ ganz oben unter seinem Namen. Der Knopf legt einen aufgeklappten
/// Eintrag „Eigener Server“ an (OpenAI-kompatibel, Key optional); mit gültiger Adresse ist er bei der Erkennung wählbar.
/// Mit <c>WLD_SCREENSHOT_DIR</c> entsteht zusätzlich ein Bild der Seite (≤ 1200 px).
/// </summary>
public sealed class SetupConnectionsServerTest
{
    [AvaloniaFact]
    public async Task Eigener_Server_ist_oben_sichtbar_und_legt_einen_offenen_Eintrag_an()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        var window = new SettingsWindow(new SettingsShellViewModel(vm)) { Width = 980, Height = 1100 };
        window.Show();
        SettingsUi.Show(window, "connections");
        Dispatcher.UIThread.RunJobs();
        var page = (ConnectionsPageViewModel)window.Shell!.SelectedPage!;
        var texts = UiTest.TextsOf(window).ToList();
        Assert.Contains(Loc.Instance["Setup_OwnServerTitle"], texts);
        Assert.True(texts.IndexOf(Loc.Instance["Setup_OwnServerTitle"]) < texts.IndexOf(Loc.Instance["G_CustomProviders"]));
        var add = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AddOwnServerButton");
        Assert.True(add.IsEffectivelyVisible);

        await page.AddServerCommand.ExecuteAsync(null);
        var row = Assert.Single(page.Rows);
        Assert.Equal(Loc.Instance["Setup_OwnServerName"], row.Name);
        Assert.True(row.KeyOptional);
        Assert.True(row.IsExpanded);
        row.BaseUrl = "http://192.168.1.20:8000/v1";
        Assert.Equal("", row.Error);
        Assert.Contains(vm.SttProviders, p => p.Value == row.Id);

        await page.AddServerCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["Setup_OwnServerName"] + " 2", page.Rows[1].Name);

        if (Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR") is { } dir)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, "connections-own-server-de.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        window.Close();
    }
}
