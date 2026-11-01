using Avalonia.Headless.XUnit;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// Prüfrunde 1 K-1: Ein Key <c>custom:&lt;id&gt;</c> darf das Entfernen seines Eintrags nicht überleben — sonst erbt ihn
/// ein späterer Eintrag gleicher ID (Import oder „Anbieter hinzufügen“) und schickt ihn an einen fremden Host.
/// Weg wie in der App: <see cref="SyncCommands.StoredKeysAsync"/> → <see cref="SyncImport.Preview"/> → <see cref="SyncCommands.DropKeysAsync"/>.
/// </summary>
public sealed class OrphanProviderKeyTest
{
    private const string Key = "test-key-not-real-7";

    private static WhisperLoomSettings WithLab()
    {
        var s = new WhisperLoomSettings();
        s.Providers.Custom.Add(new CustomProvider { Id = "lab", Name = "Labor", BaseUrl = "https://lab.example/v1" });
        s.Stt.ProviderId = "custom:lab";
        return s;
    }

    /// <summary>Import wie in der App; liefert die Einstellungen danach.</summary>
    private static async Task<WhisperLoomSettings> ImportAsync(MemorySecretStore store, WhisperLoomSettings current, string json)
    {
        var file = SyncDocument.Parse(json);
        var stored = await SyncCommands.StoredKeysAsync(store, CancellationToken.None, current, SyncImport.ProviderKeyNames(file));
        var preview = SyncImport.Preview(current, file, PolicySettings.None, stored);
        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        await SyncCommands.DropKeysAsync(store, preview, CancellationToken.None);
        return preview.Settings!;
    }

    [Fact]
    public async Task Entry_removed_by_import_drops_its_key()
    {
        var store = new MemorySecretStore { Values = { ["custom:lab"] = Key } };

        // Export eines Geräts ohne eigene Anbieter: "custom": [] ersetzt die Liste.
        var after = await ImportAsync(store, WithLab(), SyncExport.ToJson(new WhisperLoomSettings()));

        Assert.Empty(after.Providers.Custom);
        Assert.False(store.Values.ContainsKey("custom:lab"));
    }

    [Fact]
    public async Task Same_id_again_with_another_host_gets_no_key()
    {
        // Verwaister Key (z. B. von einer älteren Version oder einem gescheiterten Löschen), aktuell kein Eintrag.
        var store = new MemorySecretStore { Values = { ["custom:lab"] = Key } };

        var after = await ImportAsync(store, new WhisperLoomSettings(),
            """{"providers":{"custom":[{"id":"lab","name":"Labor","baseUrl":"https://angreifer.example/v1"}]},"stt":{"providerId":"custom:lab"}}""");
        var access = SettingsAccess.Stt(after, await SettingsAccess.KeysAsync(store, CancellationToken.None, after));

        Assert.Equal("https://angreifer.example/v1", access.BaseUrl);
        Assert.Equal("", access.ApiKey);
        Assert.False(store.Values.ContainsKey("custom:lab"));
    }

    [AvaloniaFact]
    public async Task Add_after_an_orphaned_key_starts_without_key()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var store = new MemorySecretStore { Values = { ["custom:anbieter-1"] = Key } };
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, store, new FakeAccessChecker()));
        window.Show();
        var page = (ProvidersPageViewModel)window.Shell!.Page("providers");

        await page.AddCommand.ExecuteAsync(null);

        var row = Assert.Single(page.Rows);
        Assert.Equal("custom:anbieter-1", row.Id);
        Assert.False(row.Key.IsSet);
        Assert.False(store.Values.ContainsKey("custom:anbieter-1"));
        window.Close();
    }

    [Fact]
    public async Task Provisioning_with_another_host_deletes_the_entry_key()
    {
        // Prüfrunde 2 R-K1-1: auch „WhisperLoom provision“ lenkt keinen Key eines eigenen Anbieters um.
        var dir = Directory.CreateTempSubdirectory("wl-provision-key-").FullName;
        try
        {
            new WhisperLoom.App.Services.SettingsStore(dir).Save(WithLab());
            var store = new MemorySecretStore { Values = { ["custom:lab"] = Key } };
            var file = Path.Combine(dir, "it.json");
            await File.WriteAllTextAsync(file, """{"providers":{"custom":[{"id":"lab","name":"Labor","baseUrl":"https://it.example/v1"}]}}""");
            using var output = new StringWriter();
            using var errors = new StringWriter();

            var rc = await new SyncCommands(dir, store, output, errors).RunAsync("provision", file, false, CancellationToken.None);

            Assert.Equal(0, rc);
            Assert.False(store.Values.ContainsKey("custom:lab"));
            Assert.Contains("secrets.custom:lab: Adresse geändert", errors.ToString(), StringComparison.Ordinal);
            Assert.Contains("key-drop custom:lab", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Broken_store_blocks_add_and_delete_instead_of_leaving_an_orphan()
    {
        // Prüfrunde 2 D-K1-2: Scheitert die Ablage, entsteht weder ein Eintrag mit fremdem Alt-Key noch ein verwaister Key.
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var s = f.Controller.Settings;
        s.Providers.Custom.Add(new CustomProvider { Id = "lab", Name = "Labor", BaseUrl = "https://lab.example/v1" });
        await f.Controller.ApplySettingsAsync(s);
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new BrokenSecretStore(), new FakeAccessChecker()));
        window.Show();
        var page = (ProvidersPageViewModel)window.Shell!.Page("providers");
        var row = Assert.Single(page.Rows);

        await page.AddCommand.ExecuteAsync(null);
        row.DeleteCommand.Execute(null);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(Loc.Instance["Key_StoreError"], page.Error);
        Assert.Equal(Loc.Instance["Key_StoreError"], row.Error);
        Assert.Single(page.Rows);
        Assert.Single(f.Controller.Settings.Providers.Custom);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Changing_the_host_of_an_entry_with_a_key_shows_a_hint()
    {
        // Prüfrunde 2 D-K1-1: Wer die Adresse ändert, sieht, dass der gespeicherte Key mitgeht.
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var s = f.Controller.Settings;
        s.Providers.Custom.Add(new CustomProvider { Id = "lab", Name = "Labor", BaseUrl = "https://lab.example/v1" });
        await f.Controller.ApplySettingsAsync(s);
        var store = new MemorySecretStore { Values = { ["custom:lab"] = Key } };
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, store, new FakeAccessChecker()));
        window.Show();
        var page = (ProvidersPageViewModel)window.Shell!.Page("providers");
        await page.LoadKeysAsync();
        var row = Assert.Single(page.Rows);

        row.BaseUrl = "https://lab.example/api/v1";
        Assert.Equal("", row.Hint); // gleicher Host
        row.BaseUrl = "https://neu.example/v1";

        Assert.Equal(Loc.Instance.Format("Provider_KeyNewHost", "neu.example"), row.Hint);
        window.Close();
    }

    [Fact]
    public void Drop_of_a_removed_entry_has_its_own_text()
    {
        // Prüfrunde 2 D-K1-4
        Loc.Instance.SetLanguage("de");
        var drop = new KeyDrop("custom:lab", KeyDrop.EntryRemovedTarget);

        Assert.True(drop.EntryRemoved);
        Assert.Equal("Key von „custom:lab“ wird gelöscht (Eintrag entfernt).", Loc.Instance.Format("Sync_KeyDropRemoved", drop.Name));
    }
}
