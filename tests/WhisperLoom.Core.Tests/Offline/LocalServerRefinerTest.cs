using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>
/// W2-LOCAL: lokale Textverbesserung über einen OpenAI-kompatiblen Server (<c>local.textEngine = "server"</c>, auch der
/// Transport des mitgelieferten <c>llama-server</c>). Gegen einen Fake: derselbe Auftrag wie online, nie ein Key.
/// </summary>
public sealed class LocalServerRefinerTest : IAsyncLifetime
{
    private readonly FakeServer _server = new();
    private readonly HttpClient _http = Http.CreateClient();
    private readonly FakePromptBuilder _prompts = new();
    private readonly ConcurrentQueue<FakeRequest> _calls = new();

    public ValueTask InitializeAsync()
    {
        _server.Map("/v1/models", _ => new FakeResponse(200, """{"object":"list","data":[{"id":"qwen2.5-1.5b-instruct"},{"id":"whisper-1"}]}"""));
        _server.Map("/v1/chat/completions", r =>
        {
            _calls.Enqueue(r);
            var body = new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "Hallo Welt." },
                    ["finish_reason"] = "stop",
                }),
            };
            return new FakeResponse(200, body.ToJsonString());
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private LocalServerRefiner Refiner(string model = "", string? url = null) => new(_http,
        () => new LocalServer { Name = "studio", Kind = "text", BaseUrl = url ?? _server.Url("/v1"), Model = model }, _prompts, new TextCleanup([]));

    [Fact]
    public async Task Same_job_as_online_with_the_first_model_and_no_key()
    {
        var refiner = Refiner();

        Assert.True(await refiner.IsReadyAsync(CancellationToken.None));
        var text = await refiner.RefineAsync("also ähm hallo welt", "de", new Refinement(RefineMode.Polish, Paragraphs: false), CancellationToken.None);

        Assert.Equal("Hallo Welt.", text);
        var call = Assert.Single(_calls);
        Assert.Null(call.Header("Authorization"));
        var json = JsonNode.Parse(call.Text)!;
        Assert.Equal("qwen2.5-1.5b-instruct", (string)json["model"]!);
        Assert.Equal(_prompts.Build(RefineMode.Polish, german: true, smartFillers: false, paragraphs: false, isShort: true),
            (string)json["messages"]![0]!["content"]!);
        Assert.Equal("<diktat>\nalso ähm hallo welt\n</diktat>", (string)json["messages"]![1]!["content"]!);
        Assert.Equal("qwen2.5-1.5b-instruct", refiner.ModelLabel);
    }

    [Fact]
    public async Task Configured_model_wins()
    {
        await Refiner("mein-modell").RefineAsync("hallo welt", "de", new Refinement(RefineMode.Polish), CancellationToken.None);

        Assert.Equal("mein-modell", (string)JsonNode.Parse(Assert.Single(_calls).Text)!["model"]!);
    }

    [Fact]
    public async Task Unreachable_server_is_not_ready_and_says_so()
    {
        var refiner = Refiner(url: "http://127.0.0.1:9/v1");

        Assert.False(await refiner.IsReadyAsync(CancellationToken.None));
        var e = await Assert.ThrowsAsync<LocalNotAvailableException>(() =>
            refiner.RefineAsync("hallo welt", "de", new Refinement(RefineMode.Polish), CancellationToken.None));
        Assert.Equal(LocalServerRefiner.MsgUnreachable("studio", "http://127.0.0.1:9/v1"), e.Message);
    }

    [Fact]
    public async Task No_server_entry_is_not_ready()
    {
        var refiner = new LocalServerRefiner(_http, () => null, _prompts, new TextCleanup([]));

        Assert.False(await refiner.IsReadyAsync(CancellationToken.None));
        await Assert.ThrowsAsync<LocalNotAvailableException>(() =>
            refiner.RefineAsync("hallo welt", "de", new Refinement(RefineMode.Polish), CancellationToken.None));
    }

    [Fact]
    public async Task Stage_off_sends_nothing()
    {
        Assert.Equal("roh", await Refiner().RefineAsync("roh", "de", new Refinement(RefineMode.Off), CancellationToken.None));
        Assert.Empty(_calls);
    }
}
