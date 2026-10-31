using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.App.Tests;

/// <summary>
/// Die echte Pipeline der App (WLD-INT): Erkennung und Textverbesserung über einen Fake-Proxy zu einem Server, den es nicht
/// gibt (<c>*.invalid</c>), mit den echten Text-Bausteinen. Ein Wechsel der Proxy-Einstellung gilt ab dem nächsten Diktat.
/// </summary>
public sealed class AppPipelineTest : IAsyncDisposable
{
    private const string Origin = "http://stt.invalid/v1";

    private readonly FakeServer _proxy = new();
    private readonly ConcurrentQueue<FakeRequest> _requests = new();
    private readonly WhisperLoomSettings _settings = new() { Engine = "online", Language = "de" };
    private readonly AppPipeline _pipeline;
    private string _chatAnswer = "Hallo Welt.";

    public AppPipelineTest()
    {
        _proxy.Map(Origin + "/audio/transcriptions", r =>
        {
            _requests.Enqueue(r);
            return new FakeResponse(200, """{"text":"also ähm hallo welt"}""");
        });
        _proxy.Map(Origin + "/chat/completions", r =>
        {
            _requests.Enqueue(r);
            var body = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = _chatAnswer } }) };
            return new FakeResponse(200, body.ToJsonString());
        });
        _settings.Stt.ProviderId = "custom";
        _settings.Stt.BaseUrl = Origin;
        _settings.Stt.Model = "whisper-1";
        _settings.Llm.Model = "qwen3:8b";
        SettingsStore.SetDesktop(_settings, new DesktopSettings { ProxyMode = "manual", ProxyUrl = _proxy.Base });
        _pipeline = new AppPipeline(() => _settings, new MemorySecretStore(), new ModelCache());
    }

    public async ValueTask DisposeAsync()
    {
        _pipeline.Dispose();
        await _proxy.DisposeAsync();
    }

    private Task<DictationResult> Run() =>
        _pipeline.RunAsync(
            new DictationRequest(new AudioBuffer(Enumerable.Repeat((short)9830, AudioBuffer.DefaultSampleRate).ToArray()), "de",
                DictationController.RefinementOf(_settings.Refine)),
            CancellationToken.None);

    [Fact]
    public async Task OhneStufeKommtDerRohtextMitDenEchtenRegelnUeberDenProxy()
    {
        _settings.Refine.Stage = "off";
        var d = await Run();
        Assert.Equal("also ähm hallo welt", d.Raw);
        Assert.Equal("Also hallo welt", d.Text);
        Assert.Equal([Origin + "/audio/transcriptions"], _requests.Select(r => r.Path));
    }

    [Fact]
    public async Task StufeGlaettenSchicktDenEchtenPromptUeberDenProxy()
    {
        _settings.Refine.Stage = "polish";
        var d = await Run();
        Assert.Equal("Hallo Welt.", d.Text);
        var chat = Assert.Single(_requests, r => r.Path.EndsWith("/chat/completions", StringComparison.Ordinal));
        var system = (string)JsonNode.Parse(chat.Text)!["messages"]![0]!["content"]!;
        Assert.Contains("Du korrigierst in diktiertem Text nur Satzzeichen", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProxyAusGiltAbDemNaechstenDiktat()
    {
        _settings.Refine.Stage = "off";
        await Run();
        SettingsStore.SetDesktop(_settings, new DesktopSettings { ProxyMode = "off" });
        // Ohne Proxy ist stt.invalid nicht erreichbar.
        await Assert.ThrowsAsync<ApiNetworkException>(Run);
        Assert.Single(_requests);
    }

    [Theory]
    [InlineData("system", "", ProxyMode.System, "")]
    [InlineData("off", "", ProxyMode.None, "")]
    [InlineData("manual", "http://proxy.example:3128", ProxyMode.Manual, "http://proxy.example:3128")]
    [InlineData("manual", "kein-url", ProxyMode.System, "")]
    [InlineData("unbekannt", "", ProxyMode.System, "")]
    public void ProxyNachEinstellung(string mode, string url, ProxyMode expected, string address)
    {
        var proxy = HttpClients.Proxy(new DesktopSettings { ProxyMode = mode, ProxyUrl = url });
        Assert.Equal((expected, address), (proxy.Mode, proxy.Address));
    }

    [Fact]
    public void OhneGewaehlteErkennungGiltOnline()
    {
        var dir = Directory.CreateTempSubdirectory("wl-settings-");
        try
        {
            var store = new SettingsStore(dir.FullName);
            Assert.Equal("online", store.Load().Engine);
            File.WriteAllText(store.FilePath, """{"language":"en"}""");
            Assert.Equal("online", store.Load().Engine);
            File.WriteAllText(store.FilePath, """{"engine":"offline"}""");
            Assert.Equal("offline", store.Load().Engine);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AnbieterlisteKommtAusDemKatalog()
    {
        Assert.Equal(ProviderCatalog.Providers.Select(p => p.Id), ProviderChoice.All.Select(p => p.Id));
        var anthropic = ProviderChoice.Find("anthropic")!;
        Assert.True(anthropic.Llm);
        Assert.False(anthropic.Stt);
    }
}
