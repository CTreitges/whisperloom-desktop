using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// Abnahme W2-UI1 Nr. 3 mit dem <see cref="ManagedState"/>-Modell aus W2-C0: Fest = Zeile weg (Gruppe weg, wenn nur
/// feste Zeilen), Vorgabe = sichtbar mit Etikett, nach Nutzer-Änderung „Auf Firmenvorgabe zurücksetzen“, Frei = normal.
/// </summary>
public sealed class ManagedLevelsTest
{
    private static SettingsWindow Open(ControllerFixture f)
    {
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Dictionary<string, JsonElement> Defaults(params (string Path, object Value)[] values) =>
        values.ToDictionary(v => v.Path, v => JsonSerializer.SerializeToElement(v.Value));

    [AvaloniaFact]
    public void Fixed_rows_disappear_a_fully_fixed_group_disappears_and_the_page_names_the_company()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Management.TenantName = "Beispiel GmbH";
        f.Controller.Policy = new PolicySettings { Locked = ["engine", "language", "stt.providerId"] };
        var window = Open(f);

        var page = SettingsUi.Show(window, "recognition");
        var groups = window.GetVisualDescendants().OfType<SettingsGroup>().ToList();
        Assert.False(groups.Single(g => g.TitleKey == "G_Engine").IsShown); // nur feste Zeilen
        Assert.True(groups.Single(g => g.TitleKey == "G_Online").IsShown);
        Assert.False(SettingsUi.Row(window, "stt.providerId").IsShown);
        Assert.Equal(ManagedState.Fixed, SettingsUi.Row(window, "stt.providerId").Level);
        Assert.True(SettingsUi.Row(window, "stt.model").IsShown);
        Assert.True(page.HasManagedRows);
        Assert.Equal("Einige Einstellungen werden von Beispiel GmbH verwaltet.", page.ManagedText);

        Assert.False(SettingsUi.Show(window, "general").HasManagedRows);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Default_rows_are_marked_offer_reset_after_a_user_change_and_reset_to_the_company_value()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.Model = "gpt-transcribe";
        f.Controller.Policy = new PolicySettings { Defaults = Defaults(("stt.model", "gpt-transcribe")) };
        var window = Open(f);

        SettingsUi.Show(window, "recognition");
        var row = SettingsUi.Row(window, "stt.model");
        Assert.True(row.IsShown);
        Assert.Equal(ManagedState.Default, row.Level);
        Assert.Contains(":default", row.Classes);
        Assert.DoesNotContain(":overridden", row.Classes); // Wert = Vorgabe: nichts zurückzusetzen
        Assert.Equal("Firmenvorgabe", row.BadgeText);

        // Nutzer ändert die Vorgabe → Override (ManagedDefaults), Zeile bietet Zurücksetzen.
        window.Settings!.SttModel = "eigene-wahl";
        await window.Settings.Pending;
        Assert.Equal(["stt.model"], f.Controller.Settings.Management.Overrides);
        SettingsUi.Show(window, "general");
        SettingsUi.Show(window, "recognition");
        Assert.Contains(":overridden", SettingsUi.Row(window, "stt.model").Classes);

        await window.Shell!.ResetToDefaultAsync("stt.model");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("gpt-transcribe", f.Controller.Settings.Stt.Model);
        Assert.Empty(f.Controller.Settings.Management.Overrides);
        Assert.Equal("gpt-transcribe", window.Settings!.SttModel);
        Assert.Equal("recognition", window.Shell.SelectedPage!.Id);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Reset_to_a_default_with_another_host_drops_the_own_key()
    {
        // W2-FIX Prüfer-Befund 1: Die Vorgabe zeigt auf einen anderen Host; der eigene Key ginge nach dem Zurücksetzen dorthin.
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.ProviderId = "custom";
        f.Controller.Settings.Stt.BaseUrl = "https://eigener.example.test/v1";
        f.Controller.Settings.Management.Overrides = ["stt.baseUrl"];
        f.Controller.Policy = new PolicySettings { Defaults = Defaults(("stt.baseUrl", "https://firma.example.test/v1")) };
        var secrets = new MemorySecretStore();
        secrets.Values[Core.Contracts.SecretNames.Stt] = "test-key-not-real-1";
        secrets.Values[Core.Contracts.SecretNames.Llm] = "test-key-not-real-2";
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, secrets, new FakeAccessChecker()));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        await window.Shell!.ResetToDefaultAsync("stt.baseUrl");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("https://firma.example.test/v1", f.Controller.Settings.Stt.BaseUrl);
        Assert.False(secrets.Values.ContainsKey(Core.Contracts.SecretNames.Stt));
        Assert.False(secrets.Values.ContainsKey(Core.Contracts.SecretNames.Llm)); // „wie Erkennung“: ginge ebenfalls an die Firma
        window.Close();
    }

    [AvaloniaFact]
    public async Task An_import_after_a_reset_reloads_the_window_from_the_new_values()
    {
        // Review W2-UI1 R2: Nach „Zurücksetzen“ ersetzte die Hülle die Session; das Import-Abo hing an der alten → alte Werte.
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.Model = "eigene-wahl";
        f.Controller.Policy = new PolicySettings { Defaults = Defaults(("stt.model", "gpt-transcribe")) };
        var window = Open(f);
        await window.Shell!.ResetToDefaultAsync("stt.model");

        var sync = window.Settings!.Sync;
        await sync.Preview("""{"stt":{"providerId":"groq"}}""");
        await sync.ApplyCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("groq", f.Controller.Settings.Stt.ProviderId);
        Assert.Equal("groq", window.Settings!.SttProvider.Value); // neue Arbeitskopie, nicht die veraltete
        Assert.NotSame(sync, window.Settings.Sync);
        window.Close();
    }

    [AvaloniaFact]
    public void Free_rows_have_no_mark_and_hotkeys_are_never_fixed()
    {
        using var f = new ControllerFixture();
        var policy = new PolicySettings { Locked = ["desktop"] };
        var source = new PolicyManagedSource(policy, () => f.Controller.Settings);
        Assert.Equal(ManagedState.Fixed, source.StateOf("desktop.proxyUrl"));
        Assert.NotEqual(ManagedState.Fixed, source.StateOf("desktop.hotkeyHold"));
        Assert.Null(new PolicyManagedSource(PolicySettings.None, () => f.Controller.Settings).ManagerName);
        Assert.Equal(Loc.Instance["Managed_It"], source.ManagerName); // ohne Firmenname

        var window = Open(f);
        SettingsUi.Show(window, "network");
        Assert.All(SettingsUi.Rows(window), r => Assert.Equal(ManagedState.Free, r.Level));
        Assert.All(SettingsUi.Rows(window), r => Assert.DoesNotContain(":default", r.Classes));
        window.Close();
    }
}
