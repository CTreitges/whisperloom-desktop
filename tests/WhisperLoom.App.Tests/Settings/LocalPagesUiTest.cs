using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// W2-LOCAL headless: Seiten „Anbieter“ und „Lokale Modelle“, Modellwahl mit Serverliste, Text-Engine auf „Text &amp; Stufen“.
/// </summary>
public sealed class LocalPagesUiTest
{
    private static (SettingsWindow Window, SettingsShellViewModel Shell, SettingsViewModel Vm, MemorySecretStore Keys) Open(ControllerFixture f)
    {
        var keys = new MemorySecretStore();
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, keys, new FakeAccessChecker()));
        window.Show();
        return (window, window.Shell!, window.Settings!, keys);
    }

    [AvaloniaFact]
    public async Task Custom_provider_is_created_validated_offered_in_every_slot_and_protected_while_in_use()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, shell, vm, keys) = Open(f);
        SettingsUi.Show(window, "connections");
        var page = (ConnectionsPageViewModel)shell.Page("connections");

        page.AddCommand.Execute(null);
        var row = Assert.Single(page.Rows);
        row.BaseUrl = "http://angreifer.example/v1"; // unverschlüsselt ins Internet: Fehler, nicht übernommen
        Assert.NotEqual("", row.Error);
        await vm.Pending;
        Assert.Empty(f.Controller.Settings.Providers.Custom);

        row.Name = "Labor";
        row.BaseUrl = "https://lab.example/v1";
        row.Models = "m-1, m-2";
        await vm.Pending;
        var saved = Assert.Single(f.Controller.Settings.Providers.Custom);
        Assert.Equal(("Labor", "https://lab.example/v1"), (saved.Name, saved.BaseUrl));
        Assert.Equal(["m-1", "m-2"], saved.Models);
        Assert.Contains(vm.SttProviders, c => c.Value == row.Id && c.Label == "Labor · eigener Anbieter");
        Assert.Contains(vm.LlmFallbackProviders, c => c.Value == row.Id);

        vm.SttProvider = vm.SttProviders.Single(c => c.Value == row.Id);
        await vm.Pending;
        Assert.True(vm.SttUsesEntry); // Key-Feld des Zugangs entfällt, Key liegt beim Eintrag
        var recognition = (RecognitionPageViewModel)shell.Page("recognition");
        Assert.Equal(["", "m-1", "m-2"], recognition.SttModel.Items.Take(3).Select(c => c.Value));

        row.DeleteCommand.Execute(null);
        Assert.NotEqual("", row.Error); // in Gebrauch: bleibt, keine Rückfrage
        Assert.False(row.DeletePending);
        Assert.Single(page.Rows);

        vm.SttProvider = vm.SttProviders.Single(c => c.Value == "openai");
        await keys.SetAsync("custom:anbieter-1", "test-key-not-real-5", CancellationToken.None);
        row.DeleteCommand.Execute(null);
        Assert.True(row.DeletePending); // destruktiv: erst bestätigen
        Assert.Single(page.Rows);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);
        await vm.Pending;
        Assert.Empty(page.Rows);
        Assert.Empty(f.Controller.Settings.Providers.Custom);
        Assert.Null(await keys.GetAsync("custom:anbieter-1", CancellationToken.None)); // Key des Eintrags mit gelöscht
        Assert.DoesNotContain(vm.SttProviders, c => c.Value == row.Id);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Server_model_list_joins_catalog_and_custom_entry_with_error_text()
    {
        Loc.Instance.SetLanguage("de");
        await using var server = new FakeServer();
        server.Map("/v1/models", _ => new FakeResponse(200, """{"data":[{"id":"whisper-large-v3"},{"id":"whisper-tiny"}]}"""));
        using var f = new ControllerFixture();
        var s = f.Controller.Settings;
        s.Providers.Custom.Add(new CustomProvider { Id = "studio", Name = "Studio", BaseUrl = server.Url("/v1"), KeyOptional = true });
        s.Stt.ProviderId = "custom:studio";
        await f.Controller.ApplySettingsAsync(s);
        var (window, shell, _, _) = Open(f);
        var page = (RecognitionPageViewModel)shell.Page("recognition");

        await page.SttModelPicker.LoadCommand.ExecuteAsync(null);

        Assert.False(page.SttModelPicker.StatusIsError, page.SttModelPicker.Status);
        Assert.Contains(page.SttModel.Items, c => c.Value == "whisper-large-v3");
        Assert.Equal(Loc.Instance["Model_Custom"], page.SttModel.Items[^1].Label);

        server.Map("/v1/models", _ => new FakeResponse(500, "kaputt"));
        await page.SttModelPicker.LoadCommand.ExecuteAsync(null);
        Assert.True(page.SttModelPicker.StatusIsError);
        Assert.Equal(Loc.Instance["Models_Http"], page.SttModelPicker.Status); // ohne Statuscode im Text (UX-6, Richtlinie 05 §5.3)
        Assert.Contains(page.SttModel.Items, c => c.Value == "whisper-large-v3"); // Cache bleibt
        window.Close();
    }

    [AvaloniaFact]
    public async Task Own_whisper_file_and_local_server_are_added_on_local_models()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, shell, vm, _) = Open(f);
        SettingsUi.Show(window, "recognition");
        var page = (RecognitionPageViewModel)shell.Page("recognition");
        var dir = Directory.CreateTempSubdirectory("wl-ui-model-").FullName;
        try
        {
            var bad = Path.Combine(dir, "kaputt.bin");
            await File.WriteAllTextAsync(bad, "kein Modell");
            Assert.NotNull(page.AddCustomModel(bad));
            Assert.Empty(vm.Local.CustomModels);

            var good = Path.Combine(dir, "ggml-mein.bin");
            await using (var file = File.Create(good))
            {
                file.Write("lmgg"u8);
                file.SetLength(2_000_000);
            }
            Assert.Null(page.AddCustomModel(good));
            await vm.Pending;
            Assert.Equal("ggml-mein", Assert.Single(f.Controller.Settings.Offline.CustomModels).Name);
            vm.Local.SelectedModel = vm.Local.ModelChoices.Single(c => c.Value == "custom:ggml-mein");
            await vm.Pending;
            Assert.Equal("custom:ggml-mein", f.Controller.Settings.Offline.Model);
            Assert.Null(vm.Local.SelectedModelError);
            File.Delete(good); // Datei verschoben: Fehler direkt an der Modellwahl (HIG-Review Nr. 4)
            vm.Local.SelectedModel = vm.Local.ModelChoices.Single(c => c.Value == "small");
            vm.Local.SelectedModel = vm.Local.ModelChoices.Single(c => c.Value == "custom:ggml-mein");
            Assert.NotNull(vm.Local.SelectedModelError);

            vm.Local.AddServerCommand.Execute(null);
            var server = Assert.Single(vm.Local.Servers);
            server.Name = "LM Studio";
            await vm.Pending;
            var saved = Assert.Single(LocalSettings.Of(f.Controller.Settings).Servers);
            Assert.Equal(("LM Studio", "text"), (saved.Name, saved.Kind));
            Assert.Contains(vm.LlmProviders, c => c.Value == "local:LM Studio" && c.Label == "LM Studio · lokaler Server");
            Assert.Contains(vm.Local.TextServers, c => c.Value == "LM Studio");

            server.BaseUrl = "http://angreifer.example:1234/v1";
            Assert.NotEqual("", server.Error);
            Assert.Equal("http://127.0.0.1:1234/v1", LocalSettings.Of(f.Controller.Settings).Servers[0].BaseUrl);

            // Screenreader-Name = sichtbares Verb + Eintrag (HIG-Review Nr. 1); Entfernen erst nach Rückfrage (Nr. 9).
            Assert.Equal("Entfernen: LM Studio", server.RemoveName);
            Assert.StartsWith("Herunterladen: ", vm.Local.Models[0].DownloadName, StringComparison.Ordinal);
            server.RemoveCommand.Execute(null);
            Assert.True(server.RemovePending);
            Assert.Single(vm.Local.Servers);
            server.ConfirmRemoveCommand.Execute(null);
            await vm.Pending;
            Assert.Empty(vm.Local.Servers);
            Assert.Empty(LocalSettings.Of(f.Controller.Settings).Servers);
            Assert.DoesNotContain(vm.LlmProviders, c => c.Value == "local:LM Studio");
        }
        finally
        {
            window.Close();
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Text_engine_choice_shows_its_rows_and_is_saved()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, _, vm, _) = Open(f);
        SettingsUi.Show(window, "text");
        Assert.True(SettingsUi.Row(window, "local.ollamaModel").IsEffectivelyVisible);
        Assert.False(SettingsUi.Row(window, "local.bundledModel").IsEffectivelyVisible);

        vm.Local.TextEngine = vm.Local.TextEngines.Single(c => c.Value == "bundled");
        await vm.Pending;

        Assert.Equal("bundled", LocalSettings.Of(f.Controller.Settings).TextEngine);
        Assert.True(SettingsUi.Row(window, "local.bundledModel").IsEffectivelyVisible);
        Assert.False(SettingsUi.Row(window, "local.ollamaModel").IsEffectivelyVisible);
        Assert.Contains(vm.Local.BundledModels, c => c.Value == "qwen2.5-1.5b");
        window.Close();
    }
}
