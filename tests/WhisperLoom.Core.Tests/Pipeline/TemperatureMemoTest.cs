using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>Port von <c>TemperatureMemoTest.kt</c>: ein Modell, das <c>temperature</c> ablehnt, kostet genau einen Fehlversuch (ModelCache).</summary>
public sealed class TemperatureMemoTest : IAsyncLifetime
{
    private const string Answer = """{"choices":[{"message":{"content":"Hallo Welt."},"finish_reason":"stop"}]}""";
    private const string TemperatureError =
        """{"type":"error","error":{"type":"invalid_request_error","message":"temperature: Setting temperature is not supported for this model."}}""";

    private readonly PipelineHarness _h = new();
    private readonly ConcurrentQueue<JsonObject> _bodies = new();

    public ValueTask InitializeAsync()
    {
        _h.Server.Map("/v1/audio/transcriptions", _ => new FakeResponse(200, """{"text":"also ähm hallo welt"}"""));
        _h.Server.Map("/v1/chat/completions", r =>
        {
            var body = JsonNode.Parse(r.Text)!.AsObject();
            _bodies.Enqueue(body);
            return body.ContainsKey("temperature") ? new FakeResponse(400, TemperatureError) : new FakeResponse(200, Answer);
        });
        _h.UseLocalServer();
        _h.Stage(RefineMode.Polish);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private JsonObject[] Bodies => [.. _bodies];

    [Fact]
    public async Task ZweitesDiktatSchicktNurNochEineAnfrage()
    {
        Assert.Equal("Hallo Welt.", (await _h.TranscribeAsync()).Text);
        Assert.Equal(2, Bodies.Length);
        _bodies.Clear();
        Assert.Equal("Hallo Welt.", (await _h.TranscribeAsync()).Text);
        Assert.Single(Bodies);
        Assert.False(Bodies[0].ContainsKey("temperature"));
        Assert.Equal(ChatPayload.MaxCompletionTokens, (int)Bodies[0]["max_completion_tokens"]!);
    }

    [Fact]
    public async Task WeitereStueckeEinerSprachnachrichtOhneFehlversuch()
    {
        _h.Settings.Share.Stage = "polish";
        var result = await _h.Pipeline.RefineSharedAsync(["Erstes Stück.", "Zweites Stück."], "de", null, null, TestContext.Current.CancellationToken);
        Assert.Equal(["Hallo Welt.", "Hallo Welt."], result.Paragraphs!);
        Assert.Equal(3, Bodies.Length);
        Assert.False(Bodies[2].ContainsKey("temperature"));
    }

    [Fact]
    public async Task GemerktWirdJeModell()
    {
        await _h.TranscribeAsync();
        _h.Settings.Llm.Model = "qwen3:14b";
        _bodies.Clear();
        await _h.TranscribeAsync();
        Assert.Equal(2, Bodies.Length);
        Assert.Equal(0, (int)Bodies[0]["temperature"]!);
    }

    /// <summary>Desktop: die Merkliste übersteht einen Neustart (Datei im Datenordner, nie ein Key darin).</summary>
    [Fact]
    public void MerklisteUeberstehtNeustartOhneKey()
    {
        var file = Path.Combine(Path.GetTempPath(), $"wl-models-{Guid.NewGuid():N}.json");
        try
        {
            var stt = AccessResolver.ResolveStt("custom", "http://h:1/v1", Keys.K1, "whisper-1");
            var llm = AccessResolver.ResolveLlm(stt, "same", "", "", "qwen3:8b");
            new ModelCache(file).RememberNoTemperature(llm);
            Assert.True(new ModelCache(file).RejectsTemperature("custom", "http://h:1/v1/", "qwen3:8b"));
            Assert.DoesNotContain(Keys.K1, File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
