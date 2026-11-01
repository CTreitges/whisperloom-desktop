using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>Abnahme W2-UI1 Nr. 2: Änderungen gelten sofort (ohne Speichern-Knopf), ungültige Werte inline und nicht gespeichert.</summary>
public sealed class SettingsAutoSaveTest
{
    private static (SettingsWindow Window, SettingsShellViewModel Shell, SettingsViewModel Vm) Open(ControllerFixture f)
    {
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        return (window, window.Shell!, window.Settings!);
    }

    private static string SavedJson(ControllerFixture f) => File.ReadAllText(f.Store.FilePath);

    [AvaloniaFact]
    public async Task A_choice_is_saved_immediately_without_a_save_button()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, _, vm) = Open(f);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "SaveButton");

        vm.Language = vm.Languages.Single(c => c.Value == "en");
        await vm.Pending;

        Assert.Equal("en", f.Controller.Settings.Language);
        Assert.Contains("\"language\": \"en\"", SavedJson(f), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_commit_switches_the_ui_language_only_when_it_changed()
    {
        // CI-Fund: Jeder Sofort-Commit setzte die Oberflächensprache neu; „Wie System“ sprang dabei auf die OS-Sprache.
        var other = Loc.OsLanguage() == "de" ? "en" : "de";
        Loc.Instance.SetLanguage(other);
        using var f = new ControllerFixture(); // desktop.uiLanguage = "" (Wie System)
        var (window, _, vm) = Open(f);

        vm.Language = vm.Languages.Single(c => c.Value == "fr");
        await vm.Pending;
        Assert.Equal(other, Loc.Instance.Language);

        vm.UiLanguage = vm.UiLanguages.Single(c => c.Value == Loc.OsLanguage());
        await vm.Pending;
        Assert.Equal(Loc.OsLanguage(), Loc.Instance.Language);
        window.Close();
        Loc.Instance.SetLanguage("de");
    }

    [AvaloniaFact]
    public async Task Former_file_only_settings_are_saved_from_their_page()
    {
        using var f = new ControllerFixture();
        var (window, shell, _) = Open(f);
        var cleanup = (CleanupPageViewModel)shell.Page("cleanup");
        var text = (TextPageViewModel)shell.Page("text");

        cleanup.RemoveFillers = false;
        cleanup.CustomFillers = "Halt, ÄH, halt";
        text.PolishCleanup = text.CleanupChoices.Single(c => c.Value == "readable");
        text.ParagraphsBeautify = false;
        await shell.Session.Pending;

        var s = f.Controller.Settings;
        Assert.False(s.Polish.RemoveFillers);
        Assert.Equal(["halt", "äh"], s.Polish.CustomFillers);
        Assert.Equal("readable", s.Refine.PolishCleanup);
        Assert.False(s.Refine.ParagraphsBeautify);
        window.Close();
    }

    [AvaloniaFact]
    public async Task An_invalid_url_shows_inline_and_is_not_saved_until_fixed()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, _, vm) = Open(f);
        SettingsUi.Show(window, "recognition");

        vm.SttBaseUrl = "ftp://irgendwo";
        await vm.Pending;
        Assert.Equal("", f.Controller.Settings.Stt.BaseUrl);
        Assert.Equal(Loc.Instance["Validation_Url"], vm.Errors["stt.baseUrl"]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Instance["Validation_Url"], SettingsUi.Row(window, "stt.baseUrl").Error);

        vm.SttBaseUrl = "https://eigener-server.example/v1";
        await vm.Pending;
        Assert.Equal("https://eigener-server.example/v1", f.Controller.Settings.Stt.BaseUrl);
        Assert.Equal("", vm.Errors["stt.baseUrl"]);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("5", false)]
    [InlineData("abc", false)]
    [InlineData("0", true)]
    [InlineData("1800", true)]
    [InlineData("1801", false)]
    public async Task Timeout_accepts_zero_or_the_range_only(string text, bool valid)
    {
        using var f = new ControllerFixture();
        var (window, shell, vm) = Open(f);
        var page = (RecognitionPageViewModel)shell.Page("recognition");

        page.ReadTimeout = "120";
        page.ReadTimeout = text;
        await vm.Pending;

        Assert.Equal(valid ? int.Parse(text) : 120, f.Controller.Settings.Stt.ReadTimeoutSec);
        Assert.Equal(valid, vm.Errors["stt.readTimeoutSec"] == "");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Models_come_from_the_catalog_with_a_custom_entry_and_follow_the_provider()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, shell, vm) = Open(f);
        var page = (RecognitionPageViewModel)shell.Page("recognition");
        var picker = page.SttModel;

        Assert.Equal("", picker.Items[0].Value); // Empfehlung
        Assert.Contains(picker.Items, c => c.Value == "gpt-transcribe");
        Assert.Equal(Loc.Instance["Model_Custom"], picker.Items[^1].Label);

        picker.Selected = picker.Items.Single(c => c.Value == "gpt-transcribe");
        await vm.Pending;
        Assert.Equal("gpt-transcribe", f.Controller.Settings.Stt.Model);

        // „Eigenes Modell …“ leer = ungültig, nicht gespeichert; mit Namen gespeichert.
        picker.Selected = picker.Items[^1];
        Assert.NotEqual("", picker.Error);
        await vm.Pending;
        Assert.Equal("gpt-transcribe", f.Controller.Settings.Stt.Model);
        picker.CustomText = "  mein-modell ";
        await vm.Pending;
        Assert.Equal("mein-modell", f.Controller.Settings.Stt.Model);
        Assert.Equal("", picker.Error);

        // Anbieterwechsel: Liste von Groq, das eigene Modell bleibt als „Eigenes“.
        vm.SttProvider = vm.SttProviders.Single(c => c.Value == "groq");
        Assert.Contains(picker.Items, c => c.Value == "whisper-large-v3-turbo");
        Assert.DoesNotContain(picker.Items, c => c.Value == "gpt-transcribe");
        Assert.True(picker.IsCustom);
        Assert.Equal("mein-modell", picker.CustomText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Turning_history_off_asks_first_and_only_then_deletes()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, _, vm) = Open(f);
        SettingsUi.Show(window, "privacy");

        vm.HistoryEnabled = false;
        await vm.Pending;
        Assert.True(vm.HistoryOffPending);
        Assert.True(f.Controller.Settings.History.Enabled);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(window.GetVisualDescendants().OfType<SettingRow>().Single(r => r.Name == "HistoryConfirm").IsVisible);

        vm.CancelHistoryOffCommand.Execute(null);
        Assert.True(vm.HistoryEnabled);
        Assert.True(f.Controller.Settings.History.Enabled);

        vm.HistoryEnabled = false;
        vm.ConfirmHistoryOffCommand.Execute(null);
        await vm.Pending;
        Assert.False(f.Controller.Settings.History.Enabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Only_changed_paths_are_written_so_changes_elsewhere_survive()
    {
        using var f = new ControllerFixture();
        var (window, _, vm) = Open(f);
        // Während das Fenster offen ist, ändern Tray und Bühne die Einstellungen.
        f.Controller.SetStage("beautify");
        var desktop = SettingsStore.Desktop(f.Controller.Settings);
        desktop.StageFontSize = 48;
        SettingsStore.SetDesktop(f.Controller.Settings, desktop);

        vm.Language = vm.Languages.Single(c => c.Value == "fr");
        await vm.Pending;

        Assert.Equal("fr", f.Controller.Settings.Language);
        Assert.Equal("beautify", f.Controller.Settings.Refine.Stage);
        Assert.Equal(48, SettingsStore.Desktop(f.Controller.Settings).StageFontSize);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_policy_violation_is_shown_at_the_field_and_not_saved()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = "openai";
        f.Controller.Policy = new PolicySettings { AllowedProviders = ["openai"] };
        var (window, _, vm) = Open(f);

        vm.SttBaseUrl = "https://privat-server.example/v1";
        await vm.Pending;

        Assert.Equal("", f.Controller.Settings.Stt.BaseUrl);
        Assert.Equal(Loc.Instance["Validation_Policy"], vm.Errors["stt.baseUrl"]);
        window.Close();
    }

    [Fact]
    public void Merge_writes_only_the_given_paths_and_removes_missing_ones()
    {
        var target = new WhisperLoomSettings { Language = "de" };
        target.Llm.StageModels["polish"] = "alt";
        target.Refine.Stage = "summarize";
        var source = new WhisperLoomSettings { Language = "en" };
        source.Refine.Stage = "off";

        var changed = SettingsMerge.Changed(target, source);
        Assert.Contains("language", changed);
        var merged = SettingsMerge.Apply(target, source, ["language", "llm.stageModels.polish"]);

        Assert.Equal("en", merged.Language);
        Assert.False(merged.Llm.StageModels.ContainsKey("polish"));
        Assert.Equal("summarize", merged.Refine.Stage); // nicht übertragen
    }
}
