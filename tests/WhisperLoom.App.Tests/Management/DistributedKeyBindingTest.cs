using System.Text.Json.Nodes;
using WhisperLoom.App.Cli;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tests.Management;

/// <summary>
/// Prüfrunde 1 (review.md): verteilte Firmen-Keys an ihr Ziel gebunden (R1), Keys eigener Anbieter unter <c>company:</c> und
/// Nutzer-Key gleichen Namens gelöscht (D1), Einladungslink nie mit weiteren Schaltern (D2).
/// </summary>
public sealed class DistributedKeyBindingTest
{
    [Fact]
    public async Task R1_Anbieter_Key_mit_Vorgabe_Anbieter_wird_Fest_und_geht_nie_an_ein_anderes_Ziel()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.KeyMode = "distribute";
        r.Server.Secrets = new JsonObject { ["groq"] = "test-key-not-real-21" }; // Key je Anbieter-ID (§3.3)
        r.Server.Settings["stt"] = new JsonObject { ["providerId"] = "groq" };
        r.Server.Levels["stt.providerId"] = "default"; // nur Vorgabe: der Nutzer dürfte umstellen
        await r.JoinAsync();

        Assert.Equal("test-key-not-real-21", r.Company.Values[SecretNames.Stt]);
        Assert.True(r.Policy.IsLocked("stt.providerId")); // Ziel des verteilten Keys ist Fest
        Assert.True(r.Policy.IsLocked("stt.baseUrl"));
        Assert.Equal("test-key-not-real-21", await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));

        // Verteidigung in der Tiefe: Zeigt der Zugang trotzdem woanders hin (z. B. settings.json von Hand), gibt es keinen Key.
        r.Settings.Stt.ProviderId = "custom";
        r.Settings.Stt.BaseUrl = "https://dritter.example.test/v1";
        Assert.Null(await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
        r.Settings.Stt.ProviderId = "groq";
        r.Settings.Stt.BaseUrl = "https://dritter.example.test/openai/v1";
        Assert.Null(await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
        r.Settings.Stt.BaseUrl = "";
        Assert.Equal("test-key-not-real-21", await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
    }

    [Fact]
    public async Task D1_Key_eines_eigenen_Anbieters_liegt_unter_company_und_der_Nutzer_Key_ist_weg()
    {
        var own = new Core.Contracts.Settings.WhisperLoomSettings();
        own.Providers.Custom.Add(new Core.Contracts.Settings.CustomProvider
        {
            Id = "acme", Name = "Acme", Protocol = "openai", BaseUrl = "https://acme.example.test/v1",
        });
        using var r = new ManagementRig(own);
        r.User.Values["custom:acme"] = "test-key-not-real-22"; // eigener Key des Nutzers
        r.Server.AutoApprove = true;
        r.Server.KeyMode = "distribute";
        r.Server.Settings["providers"] = new JsonObject
        {
            ["custom"] = new JsonArray(new JsonObject
            {
                ["id"] = "acme", ["name"] = "Acme", ["protocol"] = "openai", ["baseUrl"] = "https://firma.example.test/v1",
            }),
        };
        r.Server.Secrets = new JsonObject { ["custom:acme"] = "test-key-not-real-23" };
        await r.JoinAsync();

        Assert.False(r.User.Values.ContainsKey("custom:acme")); // nie an die Firmen-Adresse
        Assert.Equal("test-key-not-real-23", r.Company.Values["custom:acme"]);
        Assert.Equal("test-key-not-real-23", await r.Secrets.GetAsync("custom:acme", CancellationToken.None));

        // Eintrag lokal auf eine andere Adresse umgebogen → kein Firmen-Key.
        r.Settings.Providers.Custom.Single().BaseUrl = "https://dritter.example.test/v1";
        Assert.Null(await r.Secrets.GetAsync("custom:acme", CancellationToken.None));

        // Sperre: Firmen-Keys weg, auch der des eigenen Anbieters; der Nutzer-Key ist ebenfalls weg.
        r.Settings.Providers.Custom.Single().BaseUrl = "https://firma.example.test/v1";
        r.Server.Set("blocked");
        await r.Service.SyncAsync(CancellationToken.None);
        Assert.Empty(r.Company.Values);
        Assert.Null(await r.Secrets.GetAsync("custom:acme", CancellationToken.None));
    }

    [Fact]
    public void D4_Hinweis_unter_dem_Zugang_nennt_die_Firma_statt_der_Seite_Anbieter()
    {
        var s = new Core.Contracts.Settings.WhisperLoomSettings { Engine = "online" };
        s.Management.ServerUrl = FakeManagementServer.Url;
        s.Management.State = "approved";
        s.Stt.ProviderId = Core.Api.CompanyProvider.EntryId;
        using var f = new ControllerFixture(s);
        var vm = new ViewModels.SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        Assert.True(vm.SttUsesEntry);
        Assert.Equal("Key_AtCompany", vm.SttKeyHint);
        Assert.Equal("Key_AtCompany", vm.LlmKeyHint); // „wie Erkennung“
        Assert.Equal("Key_AtEntry", vm.SttFallbackKeyHint);
    }

    [Fact]
    public void D2_Einladungslink_nimmt_keine_weiteren_Schalter_an()
    {
        const string link = "whisperloom://join?server=https%3A%2F%2Fwl.example.test&code=ABCD-EFGH-JKMN-PQRS";
        Assert.Equal("setup", CommandLine.Parse([link]).Verb);
        Assert.Throws<UsageException>(() => CommandLine.Parse([link, "--data-dir", "C:/fremd"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--output-file", "x.txt", link]));
        Assert.Throws<UsageException>(() => CommandLine.Parse([link, "toggle"]));
    }
}
