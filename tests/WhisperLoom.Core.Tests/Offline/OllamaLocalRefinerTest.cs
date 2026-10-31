using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Local;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>Lokale Textverbesserung über einen Fake-Ollama: Bereitschaft über /api/tags, Auftrag über /api/chat, Fehler als „nicht bereit“.</summary>
public sealed class OllamaLocalRefinerTest : IAsyncLifetime
{
    private readonly FakeServer _server = new();
    private readonly HttpClient _http = Http.CreateClient(new HttpProxyOptions(ProxyMode.None));
    private readonly ConcurrentQueue<JsonObject> _chats = new();
    private string _tags = """{"models":[{"name":"qwen3:4b"},{"name":"gemma3:latest"}]}""";
    private int _chatStatus = 200;
    private string _model = "qwen3:4b";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _server.Map("/api/tags", _ => new FakeResponse(200, _tags));
        _server.Map("/api/chat", r =>
        {
            _chats.Enqueue(JsonNode.Parse(r.Text)!.AsObject());
            var body = new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "Hallo Welt." },
                ["done"] = true,
                ["done_reason"] = "stop",
            };
            return new FakeResponse(_chatStatus, _chatStatus == 200 ? body.ToJsonString() : """{"error":"model not found"}""");
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private OllamaLocalRefiner Refiner(string? baseUrl = null) =>
        new(_http, () => _model, new FakePromptBuilder(), new TextCleanup([]), () => baseUrl ?? _server.Base);

    private static string DeadUrl()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"http://127.0.0.1:{port}";
    }

    [Fact]
    public async Task BereitWennModellInstalliert()
    {
        Assert.True(await Refiner().IsReadyAsync(Ct));
        _model = "gemma3";
        Assert.True(await Refiner().IsReadyAsync(Ct));
    }

    [Fact]
    public async Task NichtBereitOhneModellOderOhneOllama()
    {
        _model = "llama3.2";
        Assert.False(await Refiner().IsReadyAsync(Ct));
        _model = " ";
        Assert.False(await Refiner().IsReadyAsync(Ct));
        _model = "qwen3:4b";
        Assert.False(await Refiner(DeadUrl()).IsReadyAsync(Ct));
        _tags = "kein json";
        Assert.False(await Refiner().IsReadyAsync(Ct));
    }

    [Fact]
    public async Task ModellListeOderNull()
    {
        Assert.Equal(["gemma3:latest", "qwen3:4b"], await OllamaLocalRefiner.ListModelsAsync(_http, _server.Base, Ct));
        Assert.Null(await OllamaLocalRefiner.ListModelsAsync(_http, DeadUrl(), Ct));
    }

    [Fact]
    public async Task VerbessertUeberApiChatMitDemGewaehltenModell()
    {
        var refiner = Refiner();
        Assert.Equal("Hallo Welt.", await refiner.RefineAsync("hallo welt", "de", new Refinement(RefineMode.Polish), Ct));
        Assert.True(_chats.TryDequeue(out var chat));
        Assert.Equal("qwen3:4b", (string?)chat["model"]);
        Assert.False((bool)chat["stream"]!);
        Assert.Equal("qwen3:4b", refiner.ModelLabel);
    }

    [Fact]
    public async Task OhneModellKeineAnfrage()
    {
        _model = "";
        var e = await Assert.ThrowsAsync<LocalNotAvailableException>(() => Refiner().RefineAsync("hallo", "de", new Refinement(RefineMode.Polish), Ct));
        Assert.Equal(OllamaLocalRefiner.MsgNoModel, e.Message);
        Assert.Empty(_chats);
    }

    [Fact]
    public async Task OllamaFehltGiltAlsNichtBereit()
    {
        var dead = DeadUrl();
        var e = await Assert.ThrowsAsync<LocalNotAvailableException>(() =>
            Refiner(dead).RefineAsync("hallo", "de", new Refinement(RefineMode.Polish), Ct));
        Assert.Contains(dead, e.Message, StringComparison.Ordinal);
    }

    /// <summary>Leere Adresse = Standard 127.0.0.1:11434 (ohne Anfrage geprüft; der Hinweis nennt dann den Download-Link).</summary>
    [Fact]
    public void OhneAdresseGiltDerStandard()
    {
        Assert.Equal(OllamaLocalRefiner.DefaultBaseUrl, new OllamaLocalRefiner(_http, () => _model, new FakePromptBuilder(), new TextCleanup([]), () => " ").BaseUrl);
        Assert.Equal(OllamaLocalRefiner.DefaultBaseUrl, new OllamaLocalRefiner(_http, () => _model, new FakePromptBuilder(), new TextCleanup([])).BaseUrl);
        Assert.Contains("https://ollama.com/download", OllamaLocalRefiner.MsgUnreachable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModellNichtInstalliertGiltAlsNichtBereit()
    {
        _chatStatus = 404;
        var e = await Assert.ThrowsAsync<LocalNotAvailableException>(() => Refiner().RefineAsync("hallo", "de", new Refinement(RefineMode.Polish), Ct));
        Assert.StartsWith(OllamaLocalRefiner.MsgModelMissing, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AusOderLeerRechnetNicht()
    {
        Assert.Equal("roh", await Refiner().RefineAsync("roh", "de", new Refinement(RefineMode.Off), Ct));
        Assert.Equal(" ", await Refiner().RefineAsync(" ", "de", new Refinement(RefineMode.Polish), Ct));
        Assert.Empty(_chats);
    }
}
