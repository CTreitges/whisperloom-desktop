using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/TextRefinerRetryTest.kt</c>: genau ein zweiter Versuch ohne <c>temperature</c>.</summary>
public sealed class TextRefinerRetryTest : IAsyncLifetime
{
    private const string TemperatureError =
        """{"error":{"message":"Unsupported parameter: 'temperature' is not supported with this model.","type":"invalid_request_error","param":"temperature","code":"unsupported_parameter"}}""";

    private const string Ok = """{"choices":[{"message":{"content":"Hallo Welt."},"finish_reason":"stop"}]}""";

    private readonly FakeServer _server = new();
    private readonly HttpClient _http = Http.CreateClient();
    private readonly ConcurrentQueue<JsonObject> _bodies = new();
    private readonly List<ApiAccess> _rejected = [];
    private Func<JsonObject, FakeResponse> _answer = body =>
        body.ContainsKey("temperature") ? new FakeResponse(400, TemperatureError) : new FakeResponse(200, Ok);

    public ValueTask InitializeAsync()
    {
        _server.Map("/v1/chat/completions", r =>
        {
            var body = JsonNode.Parse(r.Text)!.AsObject();
            _bodies.Enqueue(body);
            return _answer(body);
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private JsonObject[] Bodies => [.. _bodies];

    private ApiAccess Access(string model, IServerModelLookup? lookup = null)
    {
        var stt = AccessResolver.ResolveStt("groq", "", Keys.K1, "");
        return AccessResolver.ResolveLlm(stt, "openai", _server.Url("/v1"), Keys.K2, model, lookup);
    }

    private Task<string> Refine(string model, Action<ApiAccess>? onRejected = null) =>
        new TextRefiner(Access(model), _http, new FakePromptBuilder(), new TextCleanup([]), onTemperatureRejected: onRejected ?? _rejected.Add)
            .RefineAsync("hallo welt", "de", new Refinement(RefineMode.Polish), TestContext.Current.CancellationToken);

    [Fact]
    public async Task AbgelehnteTemperatureEinmalOhneWiederholen()
    {
        Assert.Equal("Hallo Welt.", await Refine("gpt-neu-2026"));
        Assert.Equal(2, Bodies.Length);
        Assert.Equal(0, (int)Bodies[0]["temperature"]!);
        Assert.False(Bodies[1].ContainsKey("temperature"));
        Assert.Equal(ChatPayload.MaxCompletionTokens, (int)Bodies[1]["max_completion_tokens"]!);
        Assert.Equal("gpt-neu-2026", (string?)Bodies[1]["model"]);
    }

    [Fact]
    public async Task HoechstensEinZweiterVersuch()
    {
        _answer = _ => new FakeResponse(400, TemperatureError);
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => Refine("gpt-neu-2026"));
        Assert.Equal(400, e.StatusCode);
        Assert.Equal(2, Bodies.Length);
        Assert.Empty(_rejected);
    }

    [Fact]
    public async Task GeklappterZweiterVersuchWirdGenauEinmalGemeldet()
    {
        await Refine("gpt-neu-2026");
        Assert.Single(_rejected);
        Assert.Equal("gpt-neu-2026", _rejected[0].Model);
        Assert.False(_rejected[0].ModelOption!.TemperatureSupported);
        Assert.Equal(_server.Url("/v1"), _rejected[0].BaseUrl);
    }

    [Fact]
    public async Task OhneAbgelehnteTemperatureKeineMeldung()
    {
        _answer = _ => new FakeResponse(200, Ok);
        await Refine("gpt-neu-2026");
        Assert.Single(Bodies);
        Assert.Empty(_rejected);
    }

    /// <summary>Kotlin: „Zugang prüfen“ (AccessTest) merkt sich die Ablehnung im ModelCache; hier über den TextRefiner direkt.</summary>
    [Fact]
    public async Task ZugangPruefenMerktDieAbgelehnteTemperature()
    {
        var cache = new ModelCache();
        await Refine("gpt-neu-2026", cache.RememberNoTemperature);
        Assert.Equal(2, Bodies.Length);
        Assert.False(Access("gpt-neu-2026", cache).ModelOption!.TemperatureSupported);
        Assert.Null(Access("gpt-anders", cache).ModelOption);
    }

    [Fact]
    public async Task AndererFehlerOhneWiederholen()
    {
        _answer = _ => new FakeResponse(400, """{"error":{"message":"Invalid value for 'messages'"}}""");
        await Assert.ThrowsAsync<ApiHttpException>(() => Refine("gpt-neu-2026"));
        _answer = _ => new FakeResponse(500, """{"error":{"message":"temperature service down"}}""");
        await Assert.ThrowsAsync<ApiHttpException>(() => Refine("gpt-neu-2026"));
        Assert.Equal(2, Bodies.Length);
        Assert.Empty(_rejected);
    }

    [Fact]
    public async Task OhneTemperatureGesendetKeinZweiterVersuch()
    {
        _answer = _ => new FakeResponse(400, TemperatureError);
        await Assert.ThrowsAsync<ApiHttpException>(() => Refine("gpt-5.6-luna"));
        Assert.Single(Bodies);
        Assert.True(Bodies[0].ContainsKey("max_completion_tokens"));
    }
}
