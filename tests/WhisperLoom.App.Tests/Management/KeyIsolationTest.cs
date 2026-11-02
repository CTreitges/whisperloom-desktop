using System.Text.Json.Nodes;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Management;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Tests.Management;

/// <summary>
/// W2-FIX (Security-Review H8): K1 „Ziel-Wechsel → eigenen Key verwerfen“ für Server-Konfiguration, Provisioning, Verlassen
/// und manuellen Proxy; Tiefenverteidigung im <see cref="CompanySecretStore"/>. Die Sonden P1, P2, P2b, P5 und P8 aus
/// <c>security-review-tmp/client-keys/</c> als Asserts. Nur Fake-Keys.
/// </summary>
public sealed class KeyIsolationTest
{
    private const string Own = "test-key-not-real-OWN";
    private const string Attacker = "https://angreifer.example.test/v1";

    private static async Task<(string Url, string Key)> SttRoute(ManagementRig r)
    {
        var keys = await SettingsAccess.KeysAsync(r.Secrets, CancellationToken.None, r.Settings);
        var stt = SettingsAccess.Stt(r.Settings, keys);
        return (stt.BaseUrl, stt.ApiKey);
    }

    private static Core.Contracts.Settings.WhisperLoomSettings Copy(Core.Contracts.Settings.WhisperLoomSettings s) =>
        Core.Contracts.Settings.SettingsJson.Deserialize(Core.Contracts.Settings.SettingsJson.Serialize(s));

    private static ManagementRig Distribute()
    {
        var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.KeyMode = "distribute";
        r.Server.Secrets = [];
        r.User.Values[SecretNames.Stt] = Own;
        return r;
    }

    [Theory]
    [InlineData("custom", "default")]
    [InlineData("custom", "free")]
    [InlineData("openai", "free")]
    public async Task P2_Server_lenkt_Zugang_um_eigener_Key_wird_verworfen(string provider, string level)
    {
        using var r = Distribute();
        r.Server.Settings["stt"] = new JsonObject { ["providerId"] = provider, ["baseUrl"] = Attacker };
        if (level != "free")
        {
            r.Server.Levels["stt.providerId"] = level;
            r.Server.Levels["stt.baseUrl"] = level;
        }

        Assert.Equal(SyncOutcome.Synced, await r.JoinAsync());

        var (url, key) = await SttRoute(r);
        Assert.Equal(Attacker, url);
        Assert.Equal("", key);
        Assert.False(r.User.Values.ContainsKey(SecretNames.Stt));
    }

    [Fact]
    public async Task P2b_Fest_nachgeschoben_kein_eigener_Key()
    {
        using var r = Distribute();
        r.Server.Settings["stt"] = new JsonObject { ["providerId"] = "openai", ["baseUrl"] = Attacker };
        await r.JoinAsync();
        r.Server.Levels["stt.providerId"] = "fixed";
        r.Server.Levels["stt.baseUrl"] = "fixed";
        r.Cache.Clear();
        Assert.Equal(SyncOutcome.Synced, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal("", (await SttRoute(r)).Key);
    }

    [Fact]
    public async Task Proxy_Modus_Fallbacks_auf_fremden_Host_verwerfen_eigene_Fallback_Keys()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.Settings["sttFallback"] = new JsonObject { ["providerId"] = "custom", ["baseUrl"] = Attacker };
        r.Server.Settings["llmFallback"] = new JsonObject { ["providerId"] = "custom", ["baseUrl"] = Attacker };
        r.User.Values[SecretNames.SttFallback] = "test-key-not-real-5";
        r.User.Values[SecretNames.LlmFallback] = "test-key-not-real-6";

        await r.JoinAsync();

        Assert.False(r.User.Values.ContainsKey(SecretNames.SttFallback));
        Assert.False(r.User.Values.ContainsKey(SecretNames.LlmFallback));
        Assert.Null(await r.Secrets.GetAsync(SecretNames.SttFallback, CancellationToken.None));
        Assert.Null(await r.Secrets.GetAsync(SecretNames.LlmFallback, CancellationToken.None));
    }

    [Fact]
    public async Task P5_Server_setzt_manuellen_Proxy_eigener_Key_wird_verworfen()
    {
        using var r = Distribute();
        r.Server.Settings["desktop"] = new JsonObject { ["proxyMode"] = "manual", ["proxyUrl"] = "http://angreifer.example.test:3128" };

        Assert.Equal(SyncOutcome.Synced, await r.JoinAsync());

        Assert.Equal("manual", SettingsStore.Desktop(r.Settings).ProxyMode);
        Assert.False(r.User.Values.ContainsKey(SecretNames.Stt));
        Assert.Equal("", (await SttRoute(r)).Key);
    }

    [Fact]
    public async Task Ohne_Zielwechsel_bleibt_der_eigene_Key_nutzbar()
    {
        using var r = Distribute();
        r.Server.Settings["language"] = "de";

        await r.JoinAsync();

        Assert.Equal(Own, (await SttRoute(r)).Key);
    }

    [Fact]
    public async Task P1_Verlassen_im_Proxy_Modus_schickt_keinen_eigenen_Key_an_den_Firmen_Proxy()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.User.Values[SecretNames.Stt] = Own;
        await r.JoinAsync();
        Assert.StartsWith("wld_test-token-not-real-", (await SttRoute(r)).Key);

        Assert.Equal(LeaveOutcome.Left, await r.Service.LeaveAsync(CancellationToken.None));

        var (url, key) = await SttRoute(r);
        Assert.Equal(r.Server.ProxyUrl, url);
        Assert.Equal("", key);
        Assert.False(r.User.Values.ContainsKey(SecretNames.Stt));
        Assert.False(File.Exists(new OwnKeyTargets(r.F.DataDir).FilePath));
    }

    private const string Gateway = "https://gateway.firma.example.test/v1";

    private static async Task<ManagementRig> FixedGatewayAsync()
    {
        var r = Distribute();
        r.Server.Settings["stt"] = new JsonObject { ["providerId"] = "custom", ["baseUrl"] = Gateway };
        r.Server.Levels["stt.providerId"] = "fixed";
        r.Server.Levels["stt.baseUrl"] = "fixed";
        Assert.Equal(SyncOutcome.Synced, await r.JoinAsync());
        return r;
    }

    [Fact]
    public async Task P1b_key_set_per_CLI_unter_Fest_Ziel_geht_nach_Verlassen_nicht_ans_Firmen_Ziel()
    {
        using var r = await FixedGatewayAsync();
        var errors = new StringWriter();

        Assert.Equal(0, await KeyCommand.RunAsync("set", SecretNames.Stt, () => "test-key-not-real-CLI", r.User, r.F.DataDir,
            new StringWriter(), errors, CancellationToken.None));
        Assert.Equal((Gateway, ""), await SttRoute(r));

        Assert.Equal(LeaveOutcome.Left, await r.Service.LeaveAsync(CancellationToken.None));

        var (url, key) = await SttRoute(r);
        Assert.Equal(Gateway, url);
        Assert.Equal("", key);
        Assert.False(r.User.Values.ContainsKey(SecretNames.Stt));
        Assert.Contains("warn Ziel von stt legt die Firma fest", errors.ToString());
    }

    [Fact]
    public async Task P1b_Key_im_Feld_unter_Fest_Ziel_geht_nach_Verlassen_nicht_ans_Firmen_Ziel()
    {
        using var r = await FixedGatewayAsync();

        await r.Secrets.SetAsync(SecretNames.Stt, "test-key-not-real-UI", ("custom", Gateway), CancellationToken.None);
        Assert.Equal("", (await SttRoute(r)).Key);

        Assert.Equal(LeaveOutcome.Left, await r.Service.LeaveAsync(CancellationToken.None));

        Assert.Equal((Gateway, ""), await SttRoute(r));
        Assert.False(r.User.Values.ContainsKey(SecretNames.Stt));
    }

    [Fact]
    public async Task Tiefe_Ziel_aendert_sich_ohne_Abgleich_eigener_Key_wird_zurueckgehalten()
    {
        using var r = Distribute();
        await r.JoinAsync();
        Assert.Equal(Own, (await SttRoute(r)).Key);

        // z. B. „Auf Firmenvorgabe zurücksetzen“ oder eine Änderung an settings.json: kein K1-Lauf dazwischen.
        var changed = Copy(r.Settings);
        changed.Stt.ProviderId = "custom";
        changed.Stt.BaseUrl = Attacker;
        await r.F.Controller.ApplySettingsAsync(changed);
        Assert.Equal("", (await SttRoute(r)).Key);

        // Verlassen: dieser Key ginge danach an ein Ziel, für das der Nutzer ihn nicht gespeichert hat.
        await r.Service.LeaveAsync(CancellationToken.None);
        Assert.False(r.User.Values.ContainsKey(SecretNames.Stt));
    }

    [Fact]
    public async Task Tiefe_Key_im_Feld_eingegeben_gilt_fuer_das_gezeigte_Ziel()
    {
        using var r = Distribute();
        await r.JoinAsync();
        var changed = Copy(r.Settings);
        changed.Stt.ProviderId = "custom";
        changed.Stt.BaseUrl = "https://eigener.example.test/v1";
        await r.F.Controller.ApplySettingsAsync(changed);
        Assert.Equal("", (await SttRoute(r)).Key);

        await r.Secrets.SetAsync(SecretNames.Stt, "test-key-not-real-7", ("custom", "https://eigener.example.test/v1"), CancellationToken.None);
        Assert.Equal("test-key-not-real-7", (await SttRoute(r)).Key);

        // Gespeichert für ein anderes Ziel als das gültige: zurückgehalten.
        await r.Secrets.SetAsync(SecretNames.Stt, "test-key-not-real-8", ("openai", ""), CancellationToken.None);
        Assert.Equal("", (await SttRoute(r)).Key);
    }

    [Fact]
    public async Task Tiefe_Override_des_Nutzers_gibt_den_Key_frei()
    {
        using var r = Distribute();
        await r.JoinAsync();
        var changed = Copy(r.Settings);
        changed.Stt.ProviderId = "custom";
        changed.Stt.BaseUrl = "https://eigener.example.test/v1";
        changed.Management.Overrides = ["stt.providerId", "stt.baseUrl"];
        await r.F.Controller.ApplySettingsAsync(changed);

        Assert.Equal(Own, (await SttRoute(r)).Key);
    }

    [Fact]
    public async Task Tiefe_Override_nur_auf_Abschnittsebene_gibt_den_Key_nicht_frei()
    {
        // Prüfer-Befund 3: Vorgabe auf „stt“ als Ganzes plus geändertes Modell ist keine Wahl des Ziels.
        using var r = Distribute();
        await r.JoinAsync();
        var changed = Copy(r.Settings);
        changed.Stt.ProviderId = "custom";
        changed.Stt.BaseUrl = "https://firma-vorgabe.example.test/v1";
        changed.Management.Overrides = ["stt"];
        await r.F.Controller.ApplySettingsAsync(changed);

        Assert.Equal("", (await SttRoute(r)).Key);
    }

    [Fact]
    public async Task M3_Firmencode_in_settings_verbindet_ein_verwaltetes_Geraet_nicht_neu()
    {
        // Prüfer-Befund 4: Token verloren, Code nachgetragen – kein neues Gerät (sonst wären Fest und allowLeave=false weg).
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.AllowLeave = false;
        await r.JoinAsync();
        r.User.Values.Remove(SecretNames.Device);
        var changed = Copy(r.Settings);
        changed.Management.Code = FakeManagementServer.Code;
        await r.F.Controller.ApplySettingsAsync(changed);

        Assert.Equal(SyncOutcome.Revoked, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Single(r.Server.Devices);
    }

    [Fact]
    public async Task M3_Join_bei_bestehender_Anbindung_abgelehnt()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.AllowLeave = false;
        await r.JoinAsync();
        Assert.False(r.Service.CanLeave);
        var before = r.Settings.Management;

        var e = await Assert.ThrowsAsync<ArgumentException>(() => r.Service.JoinAsync("http://127.0.0.1:9", FakeManagementServer.Code, "PC", CancellationToken.None));
        Assert.Equal("Company_AlreadyJoined", e.Message);
        Assert.Equal((before.ServerUrl, before.State, before.DeviceId), (r.Settings.Management.ServerUrl, r.Settings.Management.State, r.Settings.Management.DeviceId));
        Assert.True(r.Policy.IsLocked("management.allowLeave"));
        Assert.Single(r.Server.Devices);

        // Nach Widerruf darf neu verbunden werden.
        r.Server.Devices.Clear();
        Assert.Equal(SyncOutcome.Revoked, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal(SyncOutcome.Synced, await r.JoinAsync());
    }

    [Fact]
    public async Task M3_CLI_join_bei_bestehender_Anbindung_Exit_1()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.AllowLeave = false;
        await r.JoinAsync();
        var settings = r.F.Store.FilePath;
        var before = File.ReadAllText(settings);

        var errors = new StringWriter();
        var exit = await JoinCommand.RunAsync(r.F.DataDir, FakeManagementServer.Url, FakeManagementServer.Code, "PC", r.User, r.Company,
            r.Server.Client(), new StringWriter(), errors, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains(Localization.Loc.Instance["Company_AlreadyJoined"], errors.ToString());
        Assert.Equal(before, File.ReadAllText(settings));
        Assert.Single(r.Server.Devices);
    }

    [Fact]
    public async Task N4_Sperre_vergisst_den_ETag_Keys_kommen_nach_dem_Entsperren_wieder()
    {
        using var r = Distribute();
        r.Server.Secrets = new JsonObject { ["openai"] = "test-key-not-real-9" };
        await r.JoinAsync();
        Assert.Equal("test-key-not-real-9", (await SttRoute(r)).Key);

        r.Server.Set("blocked");
        Assert.Equal(SyncOutcome.Blocked, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Null(r.Cache.ETag);
        r.Server.Set("approved");
        Assert.Equal(SyncOutcome.Synced, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal("test-key-not-real-9", (await SttRoute(r)).Key);
    }

    [Fact]
    public async Task N1_304_schreibt_policy_fixed_nicht_aus_lokalen_Werten()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        await r.JoinAsync();
        var fixedPath = r.Policies.FixedPath;
        var before = File.ReadAllText(fixedPath);

        // Lokal geänderter fester Wert (z. B. settings.json bearbeitet, policy-fixed.json gelöscht und neu erzeugt).
        var changed = Copy(r.Settings);
        changed.Stt.BaseUrl = Attacker;
        await r.F.Controller.ApplySettingsAsync(changed);
        Assert.Equal(SyncOutcome.NotModified, await r.Service.SyncAsync(CancellationToken.None));

        Assert.Equal(before, File.ReadAllText(fixedPath));
    }

    [Fact]
    public async Task N1_fehlende_policy_fixed_bei_Fest_gilt_als_unlesbar_und_vergisst_den_ETag()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        await r.JoinAsync();
        Assert.NotNull(r.Cache.ETag);
        File.Delete(r.Policies.FixedPath);

        var (_, changed, _) = r.Policies.EnforceFixed(r.Settings, r.Policy);
        Assert.Equal(-1, changed);

        r.Service.ForgetConfigVersion();
        Assert.Null(new ManagementCache(r.F.DataDir).Load().ETag);
        Assert.Equal(SyncOutcome.Synced, await r.Service.SyncAsync(CancellationToken.None));
        Assert.True(File.Exists(r.Policies.FixedPath));
    }

    [Fact]
    public async Task Provision_Datei_lenkt_Zugang_um_eigener_Key_wird_geloescht()
    {
        using var f = new ControllerFixture();
        var user = new MemorySecretStore();
        user.Values[SecretNames.Stt] = Own;
        user.Values[SecretNames.SttFallback] = "test-key-not-real-5";
        var path = Path.Combine(f.DataDir, "it.json");
        File.WriteAllText(path, $$$"""{"schemaVersion":1,"stt":{"providerId":"custom","baseUrl":"{{{Attacker}}}"},"sttFallback":{"providerId":"openai"}}""");
        var output = new StringWriter();
        var errors = new StringWriter();

        Assert.Equal(0, await new SyncCommands(f.DataDir, user, output, errors).RunAsync("provision", path, false, CancellationToken.None));

        Assert.False(user.Values.ContainsKey(SecretNames.Stt));
        Assert.Contains("key-drop stt", output.ToString());
        Assert.Contains("warn secrets.stt: Adresse geändert", errors.ToString());
        Assert.Equal("test-key-not-real-5", user.Values[SecretNames.SttFallback]); // Ziel unverändert (OpenAI)
    }

    [Fact]
    public async Task Provision_Datei_mit_eigenem_Key_fuer_das_neue_Ziel_behaelt_ihn()
    {
        using var f = new ControllerFixture();
        var user = new MemorySecretStore();
        user.Values[SecretNames.Stt] = Own;
        var path = Path.Combine(f.DataDir, "it.json");
        File.WriteAllText(path, $$$"""{"schemaVersion":1,"stt":{"providerId":"custom","baseUrl":"{{{Attacker}}}"},"secrets":{"stt":"test-key-not-real-2"}}""");

        Assert.Equal(0, await new SyncCommands(f.DataDir, user, new StringWriter(), new StringWriter()).RunAsync("provision", path, false, CancellationToken.None));

        Assert.Equal("test-key-not-real-2", user.Values[SecretNames.Stt]);
    }

    [Fact]
    public async Task V1_Import_zaehlt_gespeicherte_Keys_im_rohen_Store()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.User.Values[SecretNames.Stt] = Own;
        await r.JoinAsync(); // Proxy-Modus: secrets.stt Fest, der Firmen-Speicher hält den eigenen Key zurück
        Assert.Null(await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));

        var stored = await SyncCommands.StoredKeysAsync(r.Secrets, CancellationToken.None, r.Settings);

        Assert.Contains(SecretNames.Stt, stored);
    }
}
