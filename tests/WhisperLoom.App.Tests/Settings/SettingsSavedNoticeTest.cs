using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// TP-WSETTINGS: Nach jedem Sofort-Speichern steht kurz „Gespeichert“ in der Seitenleiste (zentral in der Hülle), ein
/// Speicherfehler bleibt als Fehler sichtbar, und Screenreader sagen beides als Live-Region an.
/// TS-WFLAKY: Die Anzeigedauer läuft auf einer steuerbaren Uhr; Echtzeit (langsamer CI-Runner) blendet nichts aus.
/// </summary>
public sealed class SettingsSavedNoticeTest
{
    // Doku: learn.microsoft.com/dotnet/api/microsoft.extensions.time.testing.faketimeprovider (abgerufen 2026-11-11)
    private readonly FakeTimeProvider _time = new();

    private static readonly TimeSpan VisibleFor = TimeSpan.FromMilliseconds(10);

    private SettingsWindow Open(DictationController controller)
    {
        var window = new SettingsWindow(new SettingsViewModel(controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        window.Shell!.Time = _time;
        window.Shell.SavedVisibleFor = VisibleFor;
        return window;
    }

    private static T Named<T>(SettingsWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>Uhr bis kurz vor bzw. über die Anzeigedauer stellen und die Fortsetzung auf dem UI-Thread laufen lassen.</summary>
    private void Advance(TimeSpan by)
    {
        _time.Advance(by);
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
        // Langsamer Runner: echte Zeit weit über die Anzeigedauer hinaus darf den Hinweis nicht ausblenden.
        Thread.Sleep(50);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Gespeichert", text.Text);
        Assert.True(shell.IsSaved);
        Assert.True(Named<Avalonia.Controls.Control>(window, "SavedIcon").IsVisible);
        Assert.False(Named<Avalonia.Controls.Control>(window, "SaveErrorIcon").IsVisible);

        Advance(VisibleFor - TimeSpan.FromMilliseconds(1));
        Assert.Equal("Gespeichert", text.Text);

        Advance(TimeSpan.FromMilliseconds(2));
        Assert.Equal("", text.Text ?? "");
        Assert.False(shell.IsSaved);
        Assert.False(Named<Avalonia.Controls.Control>(window, "SavedIcon").IsVisible);

        // Die nächste Änderung meldet erneut „Gespeichert“ (gleicher Status-Text wie zuvor).
        shell.Session.Language = shell.Session.Languages.Single(c => c.Value == "fr");
        await shell.Session.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Gespeichert", text.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void The_default_notice_time_is_the_system_clock_for_2_5_seconds()
    {
        using var f = new ControllerFixture();
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));

        Assert.Same(TimeProvider.System, window.Shell!.Time);
        Assert.Equal(TimeSpan.FromSeconds(2.5), window.Shell.SavedVisibleFor);
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

        Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(Loc.Instance["Settings.SaveFailed"], text.Text);
        window.Close();
    }

    /// <summary>
    /// Folge-Fund TP-WSETTINGS: Nach einem gescheiterten Schreiben schreibt die nächste Änderung die gescheiterte mit, und eine
    /// schreibgeschützte Datei (UnauthorizedAccessException) zeigt denselben Fehler statt eines unbeobachtet gescheiterten Commits.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task After_a_failed_write_the_next_change_writes_the_failed_one_too(Type error)
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var fail = true;
        // Produktionsnaht: der Speicher-Delegat scheitert beim ersten Mal, danach schreibt er wie SettingsStore.Save.
        var controller = new DictationController(f.Recorder, f.Pipeline, f.Sink, f.History, f.Log, new WhisperLoomSettings(),
            s =>
            {
                if (fail)
                {
                    throw (Exception)Activator.CreateInstance(error, "write failed")!;
                }
                f.Store.Save(s);
            }, () => f.Now);
        var window = Open(controller);
        var shell = window.Shell!;
        var text = Named<TextBlock>(window, "SaveNoticeText");

        shell.Session.Language = shell.Session.Languages.Single(c => c.Value == "en");
        await shell.Session.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.SaveFailed);
        Assert.Equal(Loc.Instance["Settings.SaveFailed"], text.Text);

        fail = false;
        shell.Session.HistorySize = shell.Session.HistorySizes.First(s => s != shell.Session.HistorySize);
        await shell.Session.Pending;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("en", controller.Settings.Language);
        Assert.Equal("en", f.Store.Load().Language);
        Assert.Equal("Gespeichert", text.Text);
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
