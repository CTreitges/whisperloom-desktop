using WhisperLoom.App.Cli;
using WhisperLoom.App.Management;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tests.Management;

/// <summary>Deploy ohne Klicks (Kriterium 2): <c>WhisperLoom join</c>, Einladungslink, Provisioning mit Firmencode, Eingabe-Regeln.</summary>
public sealed class JoinCliTest
{
    [Fact]
    public void Befehlszeile_join_mit_Schaltern_positional_und_Einladungslink()
    {
        var a = CommandLine.Parse(["join", "--server", "https://wl.example.test", "--code", "abcd efgh jkmn pqrs", "--device-name", "PC-3"]);
        Assert.Equal(("join", true), (a.Verb, a.IsLocal));
        Assert.Equal(["https://wl.example.test", "abcd efgh jkmn pqrs", "PC-3"], a.Args);
        Assert.Equal(["https://wl.example.test", "ABCD-EFGH-JKMN-PQRS", ""], CommandLine.Parse(["join", "https://wl.example.test", "ABCD-EFGH-JKMN-PQRS"]).Args);

        var link = CommandLine.Parse(["whisperloom://join?server=https%3A%2F%2Fwl.example.test&code=ABCD-EFGH-JKMN-PQRS"]);
        Assert.Equal("setup", link.Verb);
        Assert.Equal(["https://wl.example.test", "ABCD-EFGH-JKMN-PQRS"], link.Args);

        Assert.Throws<UsageException>(() => CommandLine.Parse(["join", "--server", "https://wl.example.test"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["status", "--code", "X"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["whisperloom://join?server=https://x"]));
        Assert.Equal(("sync", true), (CommandLine.Parse(["sync"]).Verb, CommandLine.Parse(["sync"]).NeedsRunningInstance));
    }

    private static async Task<(int Code, string Out, string Err)> Join(string dataDir, FakeManagementServer server, MemorySecretStore user,
        string code = FakeManagementServer.Code, string url = FakeManagementServer.Url)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var exit = await JoinCommand.RunAsync(dataDir, url, code, "PC-Buchhaltung-3", user, new MemorySecretStore(), server.Client(),
            output, errors, CancellationToken.None);
        return (exit, output.ToString(), errors.ToString());
    }

    [Fact]
    public async Task Join_Exit_0_bei_pending_und_approved_1_bei_Fehler()
    {
        using var f = new ControllerFixture();
        var server = new FakeManagementServer();
        var user = new MemorySecretStore();
        var (code, output, _) = await Join(f.DataDir, server, user);
        Assert.Equal(0, code);
        Assert.Contains("ok join state=pending", output);
        Assert.DoesNotContain(server.Only.Token, output);
        Assert.Equal("PC-Buchhaltung-3", server.Only.Name);
        var stored = new SettingsStore(f.DataDir).Load().Management;
        Assert.Equal(("pending", FakeManagementServer.Url), (stored.State, stored.ServerUrl));
        Assert.Equal(server.Only.Token, user.Values[SecretNames.Device]);

        using var g = new ControllerFixture();
        var auto = new FakeManagementServer { AutoApprove = true };
        (code, output, _) = await Join(g.DataDir, auto, new MemorySecretStore());
        Assert.Equal(0, code);
        Assert.Contains("state=approved", output);
        Assert.Contains("sync=synced", output);
        Assert.True(new PolicyStore(g.DataDir).Load().IsLocked("stt.baseUrl"));

        using var h = new ControllerFixture();
        (code, _, var errors) = await Join(h.DataDir, new FakeManagementServer(), new MemorySecretStore(), code: "ZZZZ-ZZZZ-ZZZZ-ZZZZ");
        Assert.Equal(1, code);
        Assert.Contains("code_invalid", errors);
        Assert.DoesNotContain("ZZZZ", errors); // Code nie in der Ausgabe
        (code, _, errors) = await Join(h.DataDir, new FakeManagementServer { Offline = true }, new MemorySecretStore());
        Assert.Equal((1, true), (code, errors.Contains("offline")));
        (code, _, _) = await Join(h.DataDir, new FakeManagementServer(), new MemorySecretStore(), code: "zu-kurz");
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Provisioning_Datei_mit_Server_und_Code_verbindet_beim_ersten_Start()
    {
        using var r = new ManagementRig();
        var path = Path.Combine(r.F.DataDir, "deploy.json");
        var deploy = new System.Text.Json.Nodes.JsonObject
        {
            ["schemaVersion"] = 1,
            ["management"] = new System.Text.Json.Nodes.JsonObject
            {
                ["serverUrl"] = FakeManagementServer.Url, ["code"] = FakeManagementServer.Code, ["deviceId"] = "d_fremd",
            },
        };
        File.WriteAllText(path, deploy.ToJsonString());
        var errors = new StringWriter();
        Assert.Equal(0, await new SyncCommands(r.F.DataDir, r.User, TextWriter.Null, errors).RunAsync("provision", path, false, CancellationToken.None));
        Assert.Contains("management.deviceId", errors.ToString()); // Gerätezustand nicht setzbar
        var stored = r.F.Store.Load();
        Assert.Equal((FakeManagementServer.Url, FakeManagementServer.Code, ""), (stored.Management.ServerUrl, stored.Management.Code, stored.Management.DeviceId));

        // „Erster Start“: App lädt die Datei, der Abgleich verbindet und verwirft den Code.
        await r.F.Controller.ApplySettingsAsync(stored);
        r.Server.AutoApprove = true;
        Assert.Equal(SyncOutcome.Synced, await r.Service.SyncAsync(CancellationToken.None));
        Assert.Equal("", r.Settings.Management.Code);
        Assert.DoesNotContain(FakeManagementServer.Code, File.ReadAllText(r.F.Store.FilePath));
        Assert.Equal("approved", r.Settings.Management.State);
    }

    [Theory]
    [InlineData("abcd-efgh-jkmn-pqrs", "ABCD-EFGH-JKMN-PQRS")]
    [InlineData(" ABCD EFGH JKMN PQRS ", "ABCD-EFGH-JKMN-PQRS")]
    [InlineData("0OIL-1234-5678-9ABC", "0011-1234-5678-9ABC")]
    [InlineData("ABCD-EFGH-JKMN-PQR", null)]
    [InlineData("ABCD-EFGH-JKMN-PQRU", null)]
    [InlineData("", null)]
    public void Firmencode_wird_normalisiert(string input, string? expected) => Assert.Equal(expected, EnrollInput.NormalizeCode(input));

    [Theory]
    [InlineData("https://wl.example.com/", "https://wl.example.com", null)]
    [InlineData("https://wl.example.com/api/v1", "https://wl.example.com", null)]
    [InlineData("http://192.168.1.20:8080", "http://192.168.1.20:8080", null)]
    [InlineData("http://wl.example.com", null, "Company_ServerHttps")]
    [InlineData("ftp://wl.example.com", null, "Company_ServerInvalid")]
    [InlineData("https://user:pw@wl.example.com", null, "Company_ServerInvalid")]
    [InlineData("  ", null, "Company_ServerMissing")]
    public void Server_Adresse_wird_geprueft(string input, string? url, string? error) =>
        Assert.Equal((url, error), EnrollInput.NormalizeServer(input));

    [Fact]
    public void Einladungslink_liefert_Server_und_Code()
    {
        Assert.Equal(new JoinLink("https://wl.example.com", "ABCD-EFGH-JKMN-PQRS"),
            JoinLink.TryParse("whisperloom://join?server=https%3A%2F%2Fwl.example.com&code=ABCD-EFGH-JKMN-PQRS"));
        Assert.Equal("https://wl.example.com", JoinLink.TryParse("WhisperLoom://JOIN?code=X&server=https://wl.example.com")?.Server);
        Assert.Null(JoinLink.TryParse("https://wl.example.com/?code=X"));
        Assert.Null(JoinLink.TryParse("whisperloom://other?server=https://x&code=y"));
        Assert.Null(JoinLink.TryParse("whisperloom://join?server=https://x"));
    }
}
