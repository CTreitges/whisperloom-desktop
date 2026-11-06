using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// TP-WSETTINGS: Nach jedem Sofort-Speichern steht kurz „Gespeichert“ in der Seitenleiste (zentral in der Hülle), ein
/// Speicherfehler bleibt als Fehler sichtbar, und Screenreader sagen beides als Live-Region an.
/// </summary>
public sealed class SettingsSavedNoticeTest
{
    private static SettingsWindow Open(DictationController controller)
    {
        var window = new SettingsWindow(new SettingsViewModel(controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        window.Shell!.SavedVisibleFor = TimeSpan.FromSeconds(1);
        return window;
    }

    private static T Named<T>(SettingsWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>Wartet, bis die Ausblend-Verzögerung abgelaufen ist und ihre Fortsetzung auf dem UI-Thread lief.</summary>
    private static async Task WaitPastNotice()
    {
        await Task.Delay(1500);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task A_change_shows_Saved_briefly_then_hides_it()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = Open(f.Controller);
        var shell = window.Shell!;
        var text = Named<TextBlock>(window, "SaveNoticeText");
        Assert.Equal("", shell.SaveNotice);

        shell.Session.Language = shell.Session.Languages.Single(c => c.Value == "en");
        await shell.Session.Pending;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Gespeichert", text.Text);
        Assert.True(shell.IsSaved);
        Assert.True(Named<Avalonia.Controls.Control>(window, "SavedIcon").IsVisible);
        Assert.False(Named<Avalonia.Controls.Control>(window, "SaveErrorIcon").IsVisible);

        await WaitPastNotice();

        Assert.Equal("", text.Text ?? "");
        Assert.False(shell.IsSaved);
        Assert.False(Named<Avalonia.Controls.Control>(window, "SavedIcon").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_failed_save_shows_an_error_instead_of_Saved_and_keeps_it()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        // Produktionsnaht: der Speicher-Delegat des Controllers (sonst SettingsStore.Save) scheitert wie bei voller Platte.
        var controller = new DictationController(f.Recorder, f.Pipeline, f.Sink, f.History, f.Log, new WhisperLoomSettings(),
            _ => throw new IOException("disk full"), () => f.Now);
        var window = Open(controller);
        var shell = window.Shell!;
        var text = Named<TextBlock>(window, "SaveNoticeText");

        shell.Session.Language = shell.Session.Languages.Single(c => c.Value == "en");
        await shell.Session.Pending;
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.SaveFailed);
        Assert.False(shell.IsSaved);
        Assert.Equal(Loc.Instance["Settings.SaveFailed"], text.Text);
        Assert.DoesNotContain("Gespeichert", text.Text, StringComparison.Ordinal);
        Assert.Contains("error", text.Classes);
        Assert.True(Named<Avalonia.Controls.Control>(window, "SaveErrorIcon").IsVisible);
        Assert.NotEqual("en", controller.Settings.Language);

        await WaitPastNotice();
        Assert.Equal(Loc.Instance["Settings.SaveFailed"], text.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void The_notice_is_a_polite_live_region()
    {
        using var f = new ControllerFixture();
        var window = Open(f.Controller);

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Named<TextBlock>(window, "SaveNoticeText")));
        window.Close();
    }
}
