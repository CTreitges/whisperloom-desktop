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
}
