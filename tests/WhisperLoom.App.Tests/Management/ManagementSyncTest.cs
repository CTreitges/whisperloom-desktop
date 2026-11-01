using System.Text.Json.Nodes;
using WhisperLoom.App.Management;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tests.Management;

/// <summary>Abgleich gegen den Fake-Server (Kriterium 3): Enrollment, pending → approved, ETag, offline, Sperre, Widerruf, Rotation, Verlassen.</summary>
public sealed class ManagementSyncTest
{
    [Fact]
    public async Task Enrollment_wartet_dann_Freigabe_und_Abgleich_mit_ETag()
    {
        using var r = new ManagementRig();
        Assert.Equal(SyncOutcome.Pending, await r.JoinAsync());
        var m = r.Settings.Management;
        Assert.Equal(("pending", FakeManagementServer.Url, "Muster GmbH", ""), (m.State, m.ServerUrl, m.TenantName, m.Code));
        Assert.StartsWith("d_", m.DeviceId);
        Assert.Equal(r.Server.Only.Token, r.User.Values[SecretNames.Device]);
        Assert.DoesNotContain(r.Server.Only.Token, File.ReadAllText(r.F.Store.FilePath)); // Token nie in settings.json

        Assert.Equal(SyncOutcome.Pending, await r.Service.SyncAsync(CancellationToken.None));
        r.Server.Set("approved");
        Assert.Equal(SyncOutcome.Synced, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal("approved", r.Settings.Management.State);
        Assert.Equal(r.F.Now.ToUnixTimeMilliseconds(), r.Settings.Management.LastSync);
        Assert.True(r.Policy.IsLocked("stt.providerId"));

        // Zweiter Abgleich: If-None-Match mit dem ETag der ersten Antwort → 304, nichts neu angewandt.
        r.F.Now = r.F.Now.AddMinutes(15);
        Assert.Equal(SyncOutcome.NotModified, await r.Service.SyncAsync(CancellationToken.None));
        var last = r.Server.Requests.Last(q => q.Path == "/api/v1/config");
        Assert.NotNull(last.IfNoneMatch);
        Assert.Equal(r.F.Now.ToUnixTimeMilliseconds(), r.Settings.Management.LastSync);
        Assert.Contains("\"etag\"", File.ReadAllText(r.Cache.FilePath));
    }

    [Fact]
    public async Task Sofort_freigegebener_Code_gleicht_gleich_ab()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        Assert.Equal(SyncOutcome.Synced, await r.JoinAsync());
        Assert.Equal("approved", r.Settings.Management.State);
        Assert.Equal(r.Server.ProxyUrl, r.Settings.Stt.BaseUrl);
    }

    [Fact]
    public async Task Falscher_Code_und_falsche_Adresse_melden_Fehler_ohne_Zustand()
    {
        using var r = new ManagementRig();
        var e = await Assert.ThrowsAsync<ManagementException>(() =>
            r.Service.JoinAsync(FakeManagementServer.Url, "ZZZZ-ZZZZ-ZZZZ-ZZZZ", "PC", CancellationToken.None));
        Assert.Equal("code_invalid", e.Error);
        var bad = await Assert.ThrowsAsync<ArgumentException>(() => r.Service.JoinAsync("http://wl.example.test", FakeManagementServer.Code, "PC", CancellationToken.None));
        Assert.Equal("Company_ServerHttps", bad.Message);
        Assert.Equal("none", r.Settings.Management.State);
        Assert.False(r.User.Values.ContainsKey(SecretNames.Device));
    }

    [Fact]
    public async Task Offline_bleibt_die_letzte_Konfiguration_Fest_bleibt_Fest()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.Settings["refine"] = new JsonObject { ["stage"] = "beautify" };
        r.Server.Levels["refine.stage"] = "fixed";
        await r.JoinAsync();
        Assert.True(r.Policy.IsLocked("refine.stage"));

        r.Server.Offline = true;
        Assert.Equal(SyncOutcome.Offline, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal("offline", r.Service.LastError);
        Assert.Equal("approved", r.Settings.Management.State);
        Assert.True(r.Policy.IsLocked("refine.stage"));
        Assert.Equal("beautify", r.Settings.Refine.Stage);
        // Auch nach Neustart: policy.json und die festen Werte liegen auf der Platte.
        Assert.True(r.Policies.Load().IsLocked("refine.stage"));
    }

    [Fact]
    public async Task Gesperrt_schaltet_Firmen_Zugaenge_ab_und_laesst_eigene_Werte()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.KeyMode = "distribute";
        r.Server.Secrets = new JsonObject { ["groq"] = "test-key-not-real-7" };
        r.Server.Settings["stt"] = new JsonObject { ["providerId"] = "groq" };
        r.Server.Levels["stt.providerId"] = "fixed";
        r.User.Values[SecretNames.SttFallback] = "test-key-not-real-8";
        await r.JoinAsync();
        Assert.Equal("test-key-not-real-7", r.Company.Values[SecretNames.Stt]);
        r.Settings.Polish.TrailingSpace = false;

        r.Server.Set("blocked");
        Assert.Equal(SyncOutcome.Blocked, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal("blocked", r.Settings.Management.State);
        Assert.Empty(r.Company.Values);
        Assert.Equal("test-key-not-real-8", r.User.Values[SecretNames.SttFallback]); // eigene Keys bleiben
        Assert.False(r.Settings.Polish.TrailingSpace); // lokale Einstellungen unangetastet
        Assert.Null(await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
        Assert.Contains("Muster GmbH", ManagementTexts.Notice(r.Settings.Management));
    }

    [Fact]
    public async Task Unbekanntes_Token_gilt_als_widerrufen()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        await r.JoinAsync();
        r.Server.Devices.Clear(); // im Portal gelöscht
        Assert.Equal(SyncOutcome.Revoked, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal("revoked", r.Settings.Management.State);
        Assert.False(r.User.Values.ContainsKey(SecretNames.Device));
        Assert.True(r.Policy.IsLocked("stt.providerId")); // Firmen-Werte bleiben bis zum Neu-Verbinden
    }

    [Fact]
    public async Task Token_Rotation_speichert_das_neue_Token_vor_der_Nutzung()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        await r.JoinAsync();
        var old = r.User.Values[SecretNames.Device];
        r.Server.Only.Rotate = true;
        Assert.Equal(SyncOutcome.NotModified, await r.Service.SyncAsync(CancellationToken.None));
        var fresh = r.User.Values[SecretNames.Device];
        Assert.NotEqual(old, fresh);
        Assert.Equal(r.Server.Only.Token, fresh);
        Assert.Equal(fresh, r.Server.Requests.Last().Auth); // config schon mit dem neuen Token
        Assert.Null(r.Server.Only.OldToken); // altes Token damit verbraucht
    }

    [Fact]
    public async Task Verlassen_nur_wenn_erlaubt_und_raeumt_Firmen_Teile_ab()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.AllowLeave = false;
        await r.JoinAsync();
        Assert.False(r.Service.CanLeave);
        Assert.Equal(LeaveOutcome.NotAllowed, await r.Service.LeaveAsync(CancellationToken.None));
        Assert.Equal("approved", r.Settings.Management.State);

        r.Server.AllowLeave = true;
        await r.Service.SyncAsync(CancellationToken.None);
        Assert.True(r.Service.CanLeave);
        Assert.Equal(LeaveOutcome.Left, await r.Service.LeaveAsync(CancellationToken.None));
        Assert.Equal(("none", ""), (r.Settings.Management.State, r.Settings.Management.ServerUrl));
        Assert.False(r.Policy.IsActive);
        Assert.False(r.User.Values.ContainsKey(SecretNames.Device));
        Assert.Empty(r.Server.Devices);
        Assert.Equal(r.Server.ProxyUrl, r.Settings.Stt.BaseUrl); // Werte bleiben als Frei
        Assert.False(File.Exists(r.Cache.FilePath));
    }

    [Fact]
    public async Task Provisioning_mit_Firmencode_verbindet_beim_ersten_Abgleich()
    {
        using var r = new ManagementRig();
        r.Settings.Management.ServerUrl = FakeManagementServer.Url;
        r.Settings.Management.Code = FakeManagementServer.Code;
        Assert.Equal(SyncOutcome.Pending, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal("", r.Settings.Management.Code); // Code verworfen
        Assert.Equal("pending", r.Settings.Management.State);
        Assert.Single(r.Server.Devices);
    }

    [Fact]
    public async Task Abgleich_ohne_Firma_tut_nichts()
    {
        using var r = new ManagementRig();
        Assert.Equal(SyncOutcome.NotManaged, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Empty(r.Server.Requests);
    }
}
