using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests;

/// <summary>Einstellungsfenster mit Richtlinie (gesperrte Felder ausgegraut) und der Abschnitt Import/Export.</summary>
public class SyncUiTest
{
    private static (SettingsWindow Window, SettingsViewModel Vm) Open(ControllerFixture f)
    {
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        var window = new SettingsWindow(vm);
        window.Show();
        foreach (var tab in window.GetLogicalDescendants().OfType<TabItem>().ToList())
        {
            tab.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
        }
        return (window, vm);
    }

    [AvaloniaFact]
    public void Locked_fields_are_disabled_and_the_hint_is_shown()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Policy = new PolicySettings { Locked = ["stt.providerId", "llm", "secrets.stt"], HistoryEnabled = false };
        var (window, vm) = Open(f);

        Assert.False(vm.Editable["stt.providerId"]);
        Assert.False(vm.Editable["llm.stageModels.polish"]);
        Assert.False(vm.Editable["history.enabled"]);
        Assert.True(vm.Editable["stt.model"]);
        Assert.False(vm.SttKey.Editable);
        Assert.True(vm.SttFallbackKey.Editable);

        var combos = window.GetLogicalDescendants().OfType<ComboBox>().ToList();
        // Dokumentreihenfolge: erst der Erkennungs-Anbieter, dann der Fallback.
        var sttCombos = combos.Where(c => ReferenceEquals(c.ItemsSource, vm.SttProviders)).ToList();
        Assert.False(sttCombos[0].IsEnabled);
        Assert.True(sttCombos[1].IsEnabled);
        Assert.False(combos.Single(c => ReferenceEquals(c.ItemsSource, vm.LlmProviders)).IsEnabled);
        Assert.True(combos.Single(c => ReferenceEquals(c.ItemsSource, vm.Languages)).IsEnabled);
        var checks = window.GetLogicalDescendants().OfType<CheckBox>().ToList();
        Assert.Contains(checks, c => !c.IsEnabled && (c.Content as string) == Loc.Instance["Settings_HistoryEnabled"]);
        var replace = window.GetLogicalDescendants().OfType<Button>().Where(b => b.Name == "ReplaceButton").ToList();
        Assert.Contains(replace, b => b.DataContext == vm.SttKey && !b.IsEnabled);
        Assert.Contains(replace, b => b.DataContext == vm.SttFallbackKey && b.IsEnabled);

        var hint = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "PolicyHint");
        Assert.True(hint.IsVisible);
        Assert.Contains("von IT festgelegt", hint.Text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Without_policy_everything_is_editable()
    {
        using var f = new ControllerFixture();
        var (window, vm) = Open(f);

        Assert.All(window.GetLogicalDescendants().OfType<ComboBox>(), c => Assert.True(c.IsEnabled));
        Assert.All(window.GetLogicalDescendants().OfType<TextBox>().Where(b => b.Name != "NewKeyBox"), b => Assert.True(b.IsEnabled));
        Assert.False(window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "PolicyHint").IsVisible);
        Assert.False(vm.HasPolicy);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Saving_under_a_policy_keeps_locked_values()
    {
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = "groq";
        f.Controller.Policy = new PolicySettings { Locked = ["stt.providerId"], AllowedProviders = ["groq", "openai"] };
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());

        Assert.DoesNotContain(vm.SttProviders, c => c.Value == "mistral");
        vm.SttProvider = vm.SttProviders.Single(c => c.Value == "openai"); // als ob die Sperre umgangen würde
        vm.SttModel = "m9";
        await vm.Save();

        Assert.Equal("groq", f.Controller.Settings.Stt.ProviderId);
        Assert.Equal("m9", f.Controller.Settings.Stt.Model);
    }

    [AvaloniaFact]
    public async Task Import_tab_previews_the_diff_and_applies_it()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Policy = new PolicySettings { Locked = ["refine.stage"] };
        var (window, vm) = Open(f);
        var applied = 0;
        vm.Sync.Applied += () => applied++;

        await vm.Sync.Preview(File.ReadAllText(SyncCommandsTest.Fixture("settings-v1.json")));
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Sync.CanApply);
        Assert.Contains(vm.Sync.Changes, r => r.Path == "stt.providerId" && r.New == "\"groq\"" && !r.Locked);
        Assert.Contains(vm.Sync.Changes, r => r.Path == "refine.stage" && r.Locked && r.Text.EndsWith("(von IT festgelegt)", StringComparison.Ordinal));
        var list = window.GetLogicalDescendants().OfType<ListBox>().Distinct().First(l => l.Name == "SyncChanges");
        Assert.Equal(vm.Sync.Changes.Count, list.ItemCount);

        await vm.Sync.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, applied);
        Assert.Equal("groq", f.Controller.Settings.Stt.ProviderId);
        Assert.Equal("off", f.Controller.Settings.Refine.Stage);
        Assert.False(vm.Sync.CanApply);
        Assert.DoesNotContain("test-key-not-real", vm.Sync.ExportJson(), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Invalid_file_shows_a_message_and_cannot_be_applied()
    {
        Loc.Instance.SetLanguage("en");
        using var f = new ControllerFixture();
        var sync = new SyncViewModel(f.Controller, new MemorySecretStore());

        await sync.Preview("""{"history":{"size":7}}""");
        Assert.False(sync.CanApply);
        Assert.Contains("history.size", sync.Status, StringComparison.Ordinal);

        await sync.Preview("kein json");
        Assert.False(sync.CanApply);
        Assert.StartsWith("Invalid file:", sync.Status, StringComparison.Ordinal);

        await sync.Preview(File.ReadAllText(SyncCommandsTest.Fixture("settings-v1-provisioning.json")));
        Assert.True(sync.CanApply);
        Assert.Contains("only via \"WhisperLoom provision\"", sync.Status, StringComparison.Ordinal);
        Assert.DoesNotContain(sync.Changes, r => r.Path.StartsWith("secrets", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Import_that_moves_a_stored_key_to_another_host_shows_it_and_deletes_the_key()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = "groq";
        var secrets = new MemorySecretStore();
        await secrets.SetAsync(SecretNames.Stt, "test-key-not-real-7", CancellationToken.None);
        var sync = new SyncViewModel(f.Controller, secrets);

        await sync.Preview("""{"stt":{"providerId":"groq","baseUrl":"https://angreifer.example/openai/v1"}}""");

        Assert.True(sync.CanApply);
        Assert.Contains(sync.Changes, r => r.Path == "secrets.stt" && r.Text.Contains("https://angreifer.example", StringComparison.Ordinal));
        Assert.Contains("„stt“", sync.Status, StringComparison.Ordinal);
        await sync.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("https://angreifer.example/openai/v1", f.Controller.Settings.Stt.BaseUrl);
        Assert.Null(await secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
    }

    [AvaloniaFact]
    public async Task A_key_saved_between_preview_and_apply_is_dropped_too()
    {
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = "groq";
        var secrets = new MemorySecretStore();
        var sync = new SyncViewModel(f.Controller, secrets);

        await sync.Preview("""{"stt":{"baseUrl":"https://angreifer.example/openai/v1"}}""");
        await secrets.SetAsync(SecretNames.Stt, "test-key-not-real-7", CancellationToken.None);
        await sync.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("https://angreifer.example/openai/v1", f.Controller.Settings.Stt.BaseUrl);
        Assert.Null(await secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
    }

    [AvaloniaTheory]
    [InlineData("openai", "https://angreifer.example/v1")] // erlaubter Anbieter, fremder Host
    [InlineData("mistral", "")] // nicht freigegebener Anbieter (bleibt sichtbar, weil gerade gewählt)
    public async Task Key_check_under_allowed_providers_does_not_send_the_key_to_a_forbidden_target(string provider, string url)
    {
        // Review WLD-FIX D-3: „Zugang prüfen“ schickte den Key an die eingetippte Adresse, vorbei an der Freigabeliste.
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = provider;
        f.Controller.Policy = new PolicySettings { AllowedProviders = ["openai"] };
        var secrets = new MemorySecretStore();
        await secrets.SetAsync(SecretNames.Stt, "test-key-not-real-7", CancellationToken.None);
        var checker = new FakeAccessChecker();
        var vm = new SettingsViewModel(f.Controller, secrets, checker) { SttBaseUrl = url };

        await vm.SttKey.CheckCommand.ExecuteAsync(null);

        Assert.Null(checker.Last);
    }

    [AvaloniaFact]
    public async Task Key_check_under_allowed_providers_still_works_for_the_catalog_host()
    {
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = "openai";
        f.Controller.Policy = new PolicySettings { AllowedProviders = ["openai"] };
        var secrets = new MemorySecretStore();
        await secrets.SetAsync(SecretNames.Stt, "test-key-not-real-7", CancellationToken.None);
        var checker = new FakeAccessChecker();
        var vm = new SettingsViewModel(f.Controller, secrets, checker);

        await vm.SttKey.CheckCommand.ExecuteAsync(null);

        Assert.Equal(("openai", ""), checker.Last);
    }

    [AvaloniaFact]
    public async Task Key_check_with_llm_same_uses_the_recognition_address()
    {
        // Review WLD-FIX: bei „wie Erkennung“ ignoriert die Auflösung llm.baseUrl — der Prüfknopf darf es auch nicht nutzen.
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = "groq";
        f.Controller.Settings.Llm.BaseUrl = "https://angreifer.example/v1";
        var secrets = new MemorySecretStore();
        await secrets.SetAsync(SecretNames.Llm, "test-key-not-real-8", CancellationToken.None);
        var checker = new FakeAccessChecker();
        var vm = new SettingsViewModel(f.Controller, secrets, checker);

        await vm.LlmKey.CheckCommand.ExecuteAsync(null);

        Assert.Equal(("groq", ""), checker.Last);
    }

    [AvaloniaFact]
    public async Task Saving_a_foreign_base_url_under_allowed_providers_is_rejected()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = "openai";
        f.Controller.Policy = new PolicySettings { AllowedProviders = ["openai"] };
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());

        vm.SttBaseUrl = "https://privat-server.example/v1";
        await vm.Save();

        Assert.Equal("", f.Controller.Settings.Stt.BaseUrl);
        Assert.Contains("stt.baseUrl", vm.SaveStatus, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Saving_keeps_the_own_vocabulary_file()
    {
        using var f = new ControllerFixture();
        f.Controller.Policy = new PolicySettings { AllowedProviders = ["openai"] };
        f.Controller.Settings.Stt.ProviderId = "openai";
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());

        vm.VocabularyFile = "C:/daten/namen.md";
        await vm.Save();

        Assert.Equal("C:/daten/namen.md", f.Controller.Settings.Stt.VocabularyFile);
    }
}
