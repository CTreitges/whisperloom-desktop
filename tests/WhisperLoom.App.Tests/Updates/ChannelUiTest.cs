using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Settings;
using WhisperLoom.App.Updates;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.ViewModels.Updates;
using WhisperLoom.App.Views;
using WhisperLoom.App.Views.Updates;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>
/// W3-CHAN, Oberfläche: Einstellung „Update-Kanal“ auf „Allgemein“ (speichert sofort, Richtlinie sperrt), Fenster
/// „Version wählen“, Tray-Eintrag. Screenshots DE/EN nur mit <c>WLD_SCREENSHOT_DIR</c>.
/// </summary>
public sealed class ChannelUiTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static SettingsWindow Open(ControllerFixture f, Action? openVersions = null)
    {
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()) { AutoSave = true, OpenVersions = openVersions };
        var window = new SettingsWindow(new SettingsShellViewModel(vm)) { Width = 980, Height = 1100 };
        window.Show();
        SettingsUi.Show(window, "general");
        return window;
    }

    [AvaloniaFact]
    public async Task Kanal_und_Vorab_Schalter_werden_gespeichert()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var opened = 0;
        var window = Open(f, () => opened++);
        var page = (GeneralPageViewModel)window.Shell!.SelectedPage!;
        Assert.Equal("stable", page.UpdateChannel.Value);
        Assert.Equal(["Stabil", "Beta", "Test"], page.UpdateChannels.Select(c => c.Label));
        Assert.False(page.IsPrereleaseChannel);
        Assert.False(SettingsUi.Row(window, "desktop.updatePrereleaseAuto").IsEnabled);

        SettingsUi.Named<ComboBox>(window, "UpdateChannelBox").SelectedItem = page.UpdateChannels[1];
        Assert.True(page.IsPrereleaseChannel);
        SettingsUi.Named<ToggleSwitch>(window, "UpdatePrereleaseAutoBox").IsChecked = true;
        await window.Settings!.Pending;
        var saved = SettingsStore.Desktop(f.Store.Load());
        Assert.Equal(("beta", true), (saved.UpdateChannel, saved.UpdatePrereleaseAuto));
        Assert.Contains("\"updateChannel\": \"beta\"", File.ReadAllText(f.Store.FilePath));

        SettingsUi.Named<Button>(window, "OpenVersionsButton").Command!.Execute(null);
        Assert.Equal(1, opened);

        // Sprachwechsel: Beschriftung neu, Auswahl bleibt.
        Loc.Instance.SetLanguage("en");
        Assert.Equal(("beta", "Beta"), (page.UpdateChannel.Value, page.UpdateChannel.Label));
        Assert.Equal("Stable", page.UpdateChannels[0].Label);
        window.Close();
        Loc.Instance.SetLanguage("de");
    }

    [AvaloniaFact]
    public void Richtlinie_sperrt_Kanal_Schalter_und_Versionsliste()
    {
        Loc.Instance.SetLanguage("de");
        var settings = new Core.Contracts.Settings.WhisperLoomSettings();
        SettingsStore.SetDesktop(settings, new DesktopSettings { UpdateChannel = "beta", UpdatePrereleaseAuto = true });
        using var f = new ControllerFixture(settings);
        f.Controller.Policy = new PolicySettings { UpdatesEnabled = false };
        var opened = 0;
        var window = Open(f, () => opened++);
        var page = (GeneralPageViewModel)window.Shell!.SelectedPage!;
        Assert.False(page.UpdatesAllowed);
        Assert.False(page.IsPrereleaseChannel);
        Assert.False(page.OpenVersionsCommand.CanExecute(null));
        Assert.False(SettingsUi.Named<ComboBox>(window, "UpdateChannelBox").IsEffectivelyEnabled);
        Assert.True(SettingsUi.Named<Control>(window, "UpdatesDisabledHint").IsVisible);
        page.UpdateChannel = page.UpdateChannels[0];
        page.UpdatePrereleaseAuto = false;
        Assert.Equal(("beta", true), (window.Settings!.Desktop.UpdateChannel, window.Settings.Desktop.UpdatePrereleaseAuto));
        Assert.Equal(0, opened);
        window.Close();
    }

    [AvaloniaFact]
    public void Tray_hat_Eintrag_Version_waehlen()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var opened = new List<string>();
        var tray = new Tray.TrayMenu(f.Controller, opened.Add);
        var item = tray.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Version wählen …");
        Assert.True(item.IsEnabled);
        item.Command!.Execute(null);
        Assert.Equal(["versions"], opened);

        f.Controller.Policy = new PolicySettings { UpdatesEnabled = false };
        tray.Refresh();
        Assert.False(item.IsEnabled);
    }

    private static VersionListViewModel ListVm(string current, PolicySettings? policy = null)
    {
        var client = new FakeUpdateClient { CurrentVersion = current };
        client.Feeds[FakeUpdateClient.Stable] = ["1.0.0", "1.0.1"];
        client.Feeds[FakeUpdateClient.Beta] = ["1.1.0-beta.1", "1.1.0-beta.2"];
        client.Feeds[FakeUpdateClient.Test] = ["1.1.0-test.12.feat-channels"];
        var dir = Path.Combine(Path.GetTempPath(), "wl-chan-ui-" + Guid.NewGuid().ToString("N"));
        return new VersionListViewModel(client, new AppLog(dir), () => policy ?? PolicySettings.None, (_, _) => Task.FromResult(false));
    }

    [AvaloniaFact]
    public async Task Versionsliste_zeigt_Kanal_Version_Datum_installiert()
    {
        Loc.Instance.SetLanguage("de");
        var vm = ListVm("1.1.0-beta.1");
        await vm.LoadAsync();
        var window = new VersionListWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = UiTest.TextsOf(window);
        Assert.Contains("Version wählen", texts);
        Assert.Contains("1.1.0-test.12.feat-channels", texts);
        Assert.Contains("installiert", texts);
        Assert.Contains("2026-11-02", texts);
        var install = window.GetLogicalDescendants().OfType<Button>().Where(b => b.IsVisible && Equals(b.Content, "Installieren")).ToList();
        Assert.Equal(4, install.Count);
        // Klick fragt nach (Antwort im Test: nein) und installiert nichts.
        install[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "BackToStableButton").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Versionsliste_gesperrt_durch_Richtlinie()
    {
        Loc.Instance.SetLanguage("en");
        var vm = ListVm("1.0.0", new PolicySettings { UpdatesEnabled = false });
        await vm.LoadAsync();
        var window = new VersionListWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Updates are turned off by your IT.", UiTest.TextsOf(window));
        Assert.False(window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "BackToStableButton").IsEnabled);
        window.Close();
        Loc.Instance.SetLanguage("de");
    }

    [AvaloniaFact]
    public void Rueckfrage_Downgrade_mit_eigenem_Text()
    {
        Loc.Instance.SetLanguage("de");
        var window = UpdatePrompt.CreateInstall("1.0.1", downgrade: true, new TaskCompletionSource<bool>());
        window.Show();
        Assert.Contains(UiTest.TextsOf(window), t => t.Contains("1.0.1") && t.Contains("Downgrade"));
        window.Close();
        var normal = UpdatePrompt.CreateInstall("1.1.0-beta.2", downgrade: false, new TaskCompletionSource<bool>());
        normal.Show();
        Assert.DoesNotContain(UiTest.TextsOf(normal), t => t.Contains("Downgrade"));
        normal.Close();
    }

    [AvaloniaFact]
    public async Task Screenshots_Kanal_und_Versionsliste_de_en()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        foreach (var lang in new[] { "de", "en" })
        {
            Loc.Instance.SetLanguage(lang);
            var settings = new Core.Contracts.Settings.WhisperLoomSettings();
            SettingsStore.SetDesktop(settings, new DesktopSettings { UpdateChannel = "beta" });
            using var f = new ControllerFixture(settings);
            var window = Open(f);
            var group = window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "UpdateChannelBox");
            group.BringIntoView();
            Save(window, $"channel-setting-{lang}.png");
            window.Close();

            var vm = ListVm("1.1.0-beta.1");
            await vm.LoadAsync();
            var list = new VersionListWindow(vm);
            list.Show();
            Save(list, $"channel-versions-{lang}.png");
            list.Close();
        }
        Loc.Instance.SetLanguage("de");
    }

    private static void Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Thread.Sleep(400);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
