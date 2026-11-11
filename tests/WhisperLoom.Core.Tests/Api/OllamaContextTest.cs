using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>
/// TS-WSTUFEN (WLD-P1b-9): Ollama kürzt ohne <c>options.num_ctx</c> still auf seinen Standard-Kontext (unter 24 GiB VRAM 4k).
/// Die Anfrage an den Fake-Ollama nennt deshalb einen Kontext passend zur Länge: Untergrenze 4096, darüber die nächste
/// Zweierpotenz von <c>⌈(System+Nutzer)/3⌉ + ⌈2·Nutzer/3⌉ + 256</c>, höchstens 16384.
/// </summary>
public sealed class OllamaContextTest : IAsyncLifetime
{
    private readonly FakeServer _server = new();
    private readonly HttpClient _http = Http.CreateClient();
    private readonly ConcurrentQueue<JsonObject> _calls = new();
    private string _antwort = "Hallo Welt.";

    public ValueTask InitializeAsync()
    {
        _server.Map("/api/chat", r =>
        {
            _calls.Enqueue(JsonNode.Parse(r.Text)!.AsObject());
            var body = new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = _antwort },
                ["done"] = true,
                ["done_reason"] = "stop",
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

    /// <summary>Verbessert <paramref name="raw"/> über den Fake-Ollama; Rückgabe: gesendetes num_ctx und die Längen.</summary>
    private async Task<(int? NumCtx, int System, int User)> Send(string raw)
    {
        _antwort = raw;
        var access = AccessResolver.ResolveLlm(AccessResolver.ResolveStt("openai", "", "", ""), "ollama", _server.Base, "", "gemma4:e2b");
        await new TextRefiner(access, _http, new FakePromptBuilder(), new TextCleanup([]))
            .RefineAsync(raw, "de", new Refinement(RefineMode.Polish), TestContext.Current.CancellationToken);
        var call = Assert.Single(_calls);
        var messages = call["messages"]!.AsArray();
        var numCtx = call["options"]?["num_ctx"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : (int?)null;
        return (numCtx, ((string)messages[0]!["content"]!).Length, ((string)messages[1]!["content"]!).Length);
    }

    private static string Words(int chars) => string.Concat(Enumerable.Repeat("wort ", chars / 5)).TrimEnd();

    private static int Need(int system, int user) => (system + user + 2) / 3 + (2 * user + 2) / 3 + 256;

    [Fact]
    public async Task KurzerTextBekommtDieUntergrenze()
    {
        var (numCtx, system, user) = await Send("also ähm hallo welt");
        Assert.True(Need(system, user) <= 4096);
        Assert.Equal(4096, numCtx);
        Assert.Equal(0, (int)_calls.Single()["options"]!["temperature"]!);
    }

    [Fact]
    public async Task LangerTextBekommtDenKontextAusDerLaenge()
    {
        var (numCtx, system, user) = await Send(Words(6000));
        var need = Need(system, user);
        Assert.InRange(need, 4097, 8192);
        Assert.Equal(8192, numCtx);
    }

    [Fact]
    public async Task SehrLangerTextBekommtDieObergrenze()
    {
        var (numCtx, system, user) = await Send(Words(40000));
        Assert.True(Need(system, user) > 16384);
        Assert.Equal(16384, numCtx);
    }

    [Theory]
    [InlineData(0, 0, 4096)]
    [InlineData(0, 3840, 4096)] // 1280 + 2560 + 256 = 4096
    [InlineData(0, 3843, 8192)] // 1281 + 2562 + 256 = 4099
    [InlineData(300, 3900, 8192)] // 1400 + 2600 + 256 = 4256
    [InlineData(0, 7872, 8192)] // 2624 + 5248 + 256 = 8128
    [InlineData(0, 8000, 16384)] // 2667 + 5334 + 256 = 8257
    [InlineData(0, 15000, 16384)]
    [InlineData(2000, 30000, 16384)] // Obergrenze
    public void FormelMitGrenzen(int systemChars, int userChars, int expected)
    {
        Assert.Equal(expected, OllamaApi.NumCtx(new string('s', systemChars), new string('u', userChars)));
    }
}
