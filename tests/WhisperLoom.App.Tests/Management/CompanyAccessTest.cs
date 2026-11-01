using System.Text.Json.Nodes;
using WhisperLoom.App.Management;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.App.Tests.Management;

/// <summary>Zugänge (Kriterium 5): Proxy-Modus mit Geräte-Token, Verteilt-Modus mit eigenem Präfix, Fehlerklasse budget.</summary>
public sealed class CompanyAccessTest
{
    [Fact]
    public async Task Proxy_Modus_Geraete_Token_nur_an_den_Proxy_keine_Firmen_Keys()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.User.Values[SecretNames.Stt] = "test-key-not-real-1"; // eigener Key aus der Zeit „für mich“
        await r.JoinAsync();
        var token = r.User.Values[SecretNames.Device];

        Assert.Equal("proxy", r.Settings.Management.KeyMode);
        Assert.Equal(r.Server.ProxyUrl, r.Service.ProxyBaseUrl);
        Assert.Equal(token, await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
        Assert.Equal(token, await r.Secrets.GetAsync(SecretNames.Llm, CancellationToken.None)); // „wie Erkennung“ → Proxy
        Assert.Empty(r.Company.Values);
        Assert.True(r.Policy.IsLocked("secrets.stt")); // kein Key-Feld, key set verweigert
        Assert.Equal("test-key-not-real-1", r.User.Values[SecretNames.Stt]); // eigener Key bleibt liegen, geht aber nicht raus

        // Zeigt ein Zugang woanders hin (eigener Fallback), bekommt er nie das Token.
        r.Settings.SttFallback.Enabled = true;
        r.Settings.SttFallback.ProviderId = "custom";
        r.Settings.SttFallback.BaseUrl = "https://evil.example.test/api/v1/proxy";
        Assert.NotEqual(token, await r.Secrets.GetAsync(SecretNames.SttFallback, CancellationToken.None));

        // Gesperrt: kein Token mehr an den Proxy, kein eigener Key an das feste Ziel.
        r.Server.Set("blocked");
        await r.Service.SyncAsync(CancellationToken.None);
        Assert.Null(await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
    }

    [Fact]
    public async Task Proxy_Modus_Diktat_schickt_das_Geraete_Token_an_den_Proxy()
    {
        await using var proxy = new FakeServer();
        var auth = new List<string?>();
        proxy.Map("/api/v1/proxy/audio/transcriptions", req =>
        {
            auth.Add(req.Header("Authorization"));
            return new FakeResponse(200, """{"text":"Hallo Welt"}""");
        });
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        await r.JoinAsync();
        // Proxy-Adresse aus der Konfiguration auf den lokalen Fake umbiegen (wie die Server-Antwort mit lokaler Adresse).
        var cfg = r.Server.BuildConfig();
        cfg["proxy"] = new JsonObject { ["baseUrl"] = proxy.Url("/api/v1/proxy") };
        r.Cache.Save("\"x\"", cfg);
        r.Settings.Stt.BaseUrl = proxy.Url("/api/v1/proxy");
        r.Settings.Stt.Model = "whisper-large-v3-turbo";
        r.Settings.Engine = "online";

        using var pipeline = new AppPipeline(() => r.Settings, r.Secrets, new ModelCache(Path.Combine(r.F.DataDir, "mc.json")));
        var result = await pipeline.RunAsync(new DictationRequest(new AudioBuffer(new short[16000], 16000), "de", new Refinement(RefineMode.Off)), CancellationToken.None);
        Assert.Equal("Hallo Welt", result.Raw);
        Assert.Equal("Bearer " + r.User.Values[SecretNames.Device], auth.Single());
    }

    [Fact]
    public async Task Verteilt_Modus_Keys_mit_eigenem_Praefix_und_beim_Verlassen_geloescht()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.KeyMode = "distribute";
        r.Server.Secrets = new JsonObject { ["groq"] = "test-key-not-real-2", ["llm"] = "test-key-not-real-3" };
        r.Server.Settings["stt"] = new JsonObject { ["providerId"] = "groq" };
        r.Server.Settings["llm"] = new JsonObject { ["providerId"] = "groq" };
        r.Server.Levels["stt.providerId"] = "fixed";
        r.Server.Levels["llm.providerId"] = "fixed";
        r.User.Values[SecretNames.Stt] = "test-key-not-real-4";
        await r.JoinAsync();

        Assert.Equal("distribute", r.Settings.Management.KeyMode);
        Assert.Equal("test-key-not-real-2", r.Company.Values[SecretNames.Stt]);
        Assert.Equal("test-key-not-real-3", r.Company.Values[SecretNames.Llm]);
        Assert.Equal("test-key-not-real-2", await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None));
        Assert.Equal("test-key-not-real-4", r.User.Values[SecretNames.Stt]); // eigener Key unberührt
        Assert.True(r.Policy.IsLocked("secrets.stt"));
        Assert.False(File.ReadAllText(r.Cache.FilePath).Contains("test-key-not-real", StringComparison.Ordinal)); // Cache ohne Keys
        Assert.DoesNotContain("test-key-not-real", File.ReadAllText(r.F.Store.FilePath));

        // Wechsel auf Proxy: verteilte Keys weg.
        r.Server.KeyMode = "proxy";
        await r.Service.SyncAsync(CancellationToken.None);
        Assert.Empty(r.Company.Values);

        r.Server.KeyMode = "distribute";
        await r.Service.SyncAsync(CancellationToken.None);
        Assert.NotEmpty(r.Company.Values);
        await r.Service.LeaveAsync(CancellationToken.None);
        Assert.Empty(r.Company.Values);
        Assert.Equal("test-key-not-real-4", await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None)); // danach wieder der eigene
    }

    [Fact]
    public async Task Firmen_Keys_liegen_unter_eigenem_Praefix()
    {
        Assert.Equal("company:", CompanySecretStore.PrefixSuffix);
        var dir = Path.Combine(Path.GetTempPath(), "wl-company-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "secrets.json");
            var user = new Secrets.FileSecretStore(file, "WhisperLoom-Test-ENR:");
            var company = new Secrets.FileSecretStore(file, "WhisperLoom-Test-ENR:" + CompanySecretStore.PrefixSuffix);
            await company.SetAsync(SecretNames.Stt, "test-key-not-real-5", CancellationToken.None);
            Assert.Null(await user.GetAsync(SecretNames.Stt, CancellationToken.None));
            Assert.Contains("WhisperLoom-Test-ENR:company:stt", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Budget_vom_Proxy_wird_lesbar_angezeigt()
    {
        using var f = new ControllerFixture();
        var pipeline = new ThrowingPipeline { Error = ApiErrors.Http(403, CompanyProvider.BudgetError) };
        var controller = new DictationController(new FakeRecorder(), pipeline, new CaptureSink(), f.History, f.Log,
            new WhisperLoomSettings { Engine = "online" }, _ => { });
        controller.Start();
        await controller.StopAsync();
        Assert.Equal("budget", controller.LastErrorClass);
        Assert.Equal("Error_Budget", controller.LastErrorKey);
        // Tabelle direkt: die Sprache von Loc.Instance teilen sich parallel laufende Tests.
        Assert.Equal("Monatslimit der Firma erreicht", Localization.Loc.LoadTable("de")[controller.LastErrorKey!]);
        Assert.Equal("Company monthly limit reached", Localization.Loc.LoadTable("en")[controller.LastErrorKey!]);

        pipeline.Error = ApiErrors.Http(403, "forbidden");
        controller.Start();
        await controller.StopAsync();
        Assert.Equal("Error_Http", controller.LastErrorKey);
        Assert.Equal("ApiHttpException", controller.LastErrorClass);
    }

    private sealed class ThrowingPipeline : IDictationPipeline
    {
        public Exception Error { get; set; } = new InvalidOperationException();

        public Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct) => Task.FromException<DictationResult>(Error);

        public Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct) =>
            Task.FromException<RefinedText>(Error);
    }
}
