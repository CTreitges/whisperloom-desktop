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
    public async Task Proxy_Modus_Zugang_wird_Eintrag_Firma_Token_nur_fuer_diesen_Eintrag()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.User.Values[SecretNames.Stt] = "test-key-not-real-1"; // eigener Key aus der Zeit „für mich“
        await r.JoinAsync();
        var token = r.User.Values[SecretNames.Device];

        Assert.Equal("proxy", r.Settings.Management.KeyMode);
        Assert.Equal((CompanyProvider.EntryId, ""), (r.Settings.Stt.ProviderId, r.Settings.Stt.BaseUrl));
        var entry = ProviderDirectory.For(r.Settings).Find(CompanyProvider.EntryId);
        Assert.NotNull(entry);
        Assert.Equal((r.Server.ProxyUrl, ProviderCatalog.CustomId, "Muster GmbH"), (entry.BaseUrl, entry.Family, entry.Name));
        Assert.Equal(token, await r.Secrets.GetAsync(CompanyProvider.EntryId, CancellationToken.None));
        Assert.Null(await r.Secrets.GetAsync(SecretNames.Stt, CancellationToken.None)); // Zugangs-Key: Ziel ist Fest, nie der eigene
        Assert.Empty(r.Company.Values);
        Assert.True(r.Policy.IsLocked("secrets.stt")); // kein Key-Feld, key set verweigert
        Assert.Equal("test-key-not-real-1", r.User.Values[SecretNames.Stt]); // eigener Key bleibt liegen, geht aber nicht raus

        // Auflösung wie in der Pipeline: Key des Eintrags = Token, Adresse = Proxy; „wie Erkennung“ erbt beides.
        var keys = await Core.Pipeline.SettingsAccess.KeysAsync(r.Secrets, CancellationToken.None, r.Settings);
        var stt = Core.Pipeline.SettingsAccess.Stt(r.Settings, keys);
        Assert.Equal((r.Server.ProxyUrl, token), (stt.BaseUrl, stt.ApiKey));
        Assert.Equal(token, Core.Pipeline.SettingsAccess.Llm(r.Settings, keys).ApiKey);

        // Ein eigener Server mit anderer Adresse bekommt das Token nie (Key gehört nur zum Eintrag).
        r.Settings.SttFallback.Enabled = true;
        r.Settings.SttFallback.ProviderId = "custom";
        r.Settings.SttFallback.BaseUrl = "https://evil.example.test/api/v1/proxy";
        var fallback = Core.Pipeline.SettingsAccess.SttFallback(r.Settings, keys)!;
        Assert.NotEqual(token, fallback.ApiKey);

        // Gesperrt: kein Token mehr; der Eintrag bleibt, damit die festen Zugänge gültig bleiben.
        r.Server.Set("blocked");
        await r.Service.SyncAsync(CancellationToken.None);
        Assert.Null(await r.Secrets.GetAsync(CompanyProvider.EntryId, CancellationToken.None));
        Assert.NotNull(ProviderDirectory.For(r.Settings).Find(CompanyProvider.EntryId));
        keys = await Core.Pipeline.SettingsAccess.KeysAsync(r.Secrets, CancellationToken.None, r.Settings);
        Assert.Equal("", Core.Pipeline.SettingsAccess.Stt(r.Settings, keys).ApiKey);
    }

    [Fact]
    public async Task Verlassen_stellt_Firmen_Zugaenge_auf_eigenen_Server_mit_Proxy_Adresse_zurueck()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        await r.JoinAsync();
        Assert.Equal(LeaveOutcome.Left, await r.Service.LeaveAsync(CancellationToken.None));
        Assert.Equal((ProviderCatalog.CustomId, r.Server.ProxyUrl), (r.Settings.Stt.ProviderId, r.Settings.Stt.BaseUrl));
        Assert.Null(ProviderDirectory.For(r.Settings).Find(CompanyProvider.EntryId));
        Assert.Null(await r.Secrets.GetAsync(CompanyProvider.EntryId, CancellationToken.None));
    }

    [Fact]
    public async Task Proxy_Adresse_eines_anderen_Hosts_wird_nicht_zum_Firmen_Eintrag()
    {
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        r.Server.ProxyOverride = "https://other.example.test/api/v1/proxy";
        await r.JoinAsync();
        Assert.Equal(ProviderCatalog.CustomId, r.Settings.Stt.ProviderId); // kein Token an einen fremden Host
        var keys = await Core.Pipeline.SettingsAccess.KeysAsync(r.Secrets, CancellationToken.None, r.Settings);
        Assert.Equal("", Core.Pipeline.SettingsAccess.Stt(r.Settings, keys).ApiKey);
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
        // Gerät „läuft“ gegen den lokalen Fake als Firmen-Server: Eintrag-Adresse = <serverUrl>/api/v1/proxy.
        r.Settings.Management.ServerUrl = proxy.Base;
        r.Settings.Stt.Model = "whisper-large-v3-turbo";
        r.Settings.Engine = "online";

        using var pipeline = new AppPipeline(() => r.Settings, r.Secrets, new ModelCache(Path.Combine(r.F.DataDir, "mc.json")));
        var result = await pipeline.RunAsync(new DictationRequest(new AudioBuffer(new short[16000], 16000), "de", new Refinement(RefineMode.Off)), CancellationToken.None);
        Assert.Equal("Hallo Welt", result.Raw);
        Assert.Equal("Bearer " + r.User.Values[SecretNames.Device], auth.Single());
    }

    [Fact]
    public async Task Budget_403_budget_exhausted_vom_Proxy_ist_Fehlerklasse_budget()
    {
        await using var proxy = new FakeServer();
        proxy.Map("/api/v1/proxy/audio/transcriptions", _ =>
            new FakeResponse(403, """{"error":"budget_exhausted","message":"Budget der Gruppe aufgebraucht"}"""));
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        await r.JoinAsync();
        r.Settings.Management.ServerUrl = proxy.Base;
        r.Settings.Engine = "online";
        using var pipeline = new AppPipeline(() => r.Settings, r.Secrets, new ModelCache(Path.Combine(r.F.DataDir, "mc.json")));
        var e = await Assert.ThrowsAnyAsync<Exception>(() =>
            pipeline.RunAsync(new DictationRequest(new AudioBuffer(new short[16000], 16000), "de", new Refinement(RefineMode.Off)), CancellationToken.None));
        Assert.True(CompanyProvider.IsBudgetError(e), e.GetType().Name);
    }

    [Fact]
    public async Task Budget_erschoepft_in_der_Textverbesserung_kein_Ersatz_Anbieter()
    {
        // hinweise-srv2.md 8 (Christof): Budget erreicht = klar ablehnen, kein Fallback-Versuch.
        await using var proxy = new FakeServer();
        proxy.Map("/api/v1/proxy/chat/completions", _ =>
            new FakeResponse(403, """{"error":"budget_exhausted","message":"Budget der Firma aufgebraucht"}"""));
        await using var other = new FakeServer();
        var fallbackCalls = 0;
        other.Map("/v1/chat/completions", _ =>
        {
            fallbackCalls++;
            return new FakeResponse(200, """{"choices":[{"message":{"role":"assistant","content":"Ersatz"}}]}""");
        });
        using var r = new ManagementRig();
        r.Server.AutoApprove = true;
        await r.JoinAsync();
        r.Settings.Management.ServerUrl = proxy.Base;
        r.Settings.Engine = "online";
        r.Settings.Llm.Model = "llama-3.3-70b";
        r.Settings.LlmFallback.Enabled = true;
        r.Settings.LlmFallback.ProviderId = "custom";
        r.Settings.LlmFallback.BaseUrl = other.Url("/v1");
        r.Settings.LlmFallback.Model = "fake-llm";
        using var pipeline = new AppPipeline(() => r.Settings, r.Secrets, new ModelCache(Path.Combine(r.F.DataDir, "mc.json")));
        try
        {
            var refined = await pipeline.RefineAsync("hallo welt", "de", new Refinement(RefineMode.Polish), CancellationToken.None);
            Assert.NotEqual("Ersatz", refined.Text);
        }
        catch (Exception e)
        {
            Assert.True(CompanyProvider.IsBudgetError(e) || CompanyProvider.IsBudgetError(e.InnerException!), e.GetType().Name);
        }
        Assert.Equal(0, fallbackCalls);
    }

    [Fact]
    public async Task Server_Konfig_mit_eigenem_Anbieter_neuer_Adresse_loescht_den_Nutzer_Key()
    {
        // W2-LOCAL R-K1-1 / Hinweis 6: Ein Key eines eigenen Anbieters ginge sonst an die Adresse, die die Firma setzt.
        var own = new WhisperLoomSettings();
        own.Providers.Custom.Add(new CustomProvider { Id = "acme", Name = "Acme", Protocol = "openai", BaseUrl = "https://acme.example.test/v1" });
        using var r = new ManagementRig(own);
        r.User.Values["custom:acme"] = "test-key-not-real-6";
        r.Server.AutoApprove = true;
        r.Server.Settings["providers"] = new JsonObject
        {
            ["custom"] = new JsonArray(new JsonObject
            {
                ["id"] = "acme", ["name"] = "Acme", ["protocol"] = "openai", ["baseUrl"] = "https://firma.example.test/v1",
            }),
        };
        await r.JoinAsync();
        Assert.Equal("https://firma.example.test/v1", r.Settings.Providers.Custom.Single().BaseUrl);
        Assert.False(r.User.Values.ContainsKey("custom:acme"));
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
        Assert.Equal("Firmenbudget für diesen Monat erreicht – bitte IT fragen", Localization.Loc.LoadTable("de")[controller.LastErrorKey!]);
        Assert.Equal("Company budget for this month reached – please ask your IT team", Localization.Loc.LoadTable("en")[controller.LastErrorKey!]);

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
