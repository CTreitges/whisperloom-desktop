using System.Text.Json.Nodes;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.App.Tests;

/// <summary>
/// TS-WSTUFEN (WLD-P1b-9): Hört Ollama an seiner Längengrenze auf (<c>done_reason: length</c>), kommt der Rohtext — und das
/// Log nennt genau einmal je Diktat die Fehlerklasse, nur als Metadatum. Echte Pipeline der App, Ollama über einen Fake-Proxy.
/// </summary>
public sealed class TruncatedRefineLogTest : IAsyncDisposable
{
    private const string SttOrigin = "http://stt.invalid/v1";
    private const string LlmOrigin = "http://llm.invalid";
    private const string Diktat = "also ähm geheimes diktat";

    private readonly FakeServer _proxy = new();
    private readonly WhisperLoomSettings _settings = new() { Engine = "online", Language = "de" };
    private readonly AppPipeline _pipeline;
    private string _doneReason = "length";
    private int _chats;

    public TruncatedRefineLogTest()
    {
        _proxy.Map(SttOrigin + "/audio/transcriptions", _ => new FakeResponse(200, new JsonObject { ["text"] = Diktat }.ToJsonString()));
        _proxy.Map(LlmOrigin + "/api/chat", _ =>
        {
            Interlocked.Increment(ref _chats);
            var body = new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "Also geheimes Dik" },
                ["done"] = true,
                ["done_reason"] = _doneReason,
            };
            return new FakeResponse(200, body.ToJsonString());
        });
        _settings.Stt.ProviderId = "custom";
        _settings.Stt.BaseUrl = SttOrigin;
        _settings.Stt.Model = "whisper-1";
        _settings.Llm.ProviderId = "ollama";
        _settings.Llm.BaseUrl = LlmOrigin;
        _settings.Llm.Model = "gemma4:e2b";
        _settings.Refine.Stage = "polish";
        SettingsStore.SetDesktop(_settings, new DesktopSettings { ProxyMode = "manual", ProxyUrl = _proxy.Base });
        _pipeline = new AppPipeline(() => _settings, new MemorySecretStore(), new ModelCache());
    }

    public async ValueTask DisposeAsync()
    {
        _pipeline.Dispose();
        await _proxy.DisposeAsync();
    }

    private static string[] Lines(ControllerFixture f, string needle) =>
        [.. f.LogText.Split('\n').Where(l => l.Contains(needle, StringComparison.Ordinal))];

    [Fact]
    public async Task AbgeschnitteneAntwortLoggtGenauEinmalDieFehlerklasse()
    {
        using var f = new ControllerFixture(_settings, pipeline: _pipeline);

        f.Controller.Start();
        Assert.Equal("done", await f.Controller.StopAsync());

        Assert.Equal(1, _chats);
        var line = Assert.Single(Lines(f, "RefineTruncated"));
        Assert.Contains(" dictation status=ok ", line, StringComparison.Ordinal);
        Assert.Contains(" refineErrorClass=RefineTruncatedException", line, StringComparison.Ordinal);
        Assert.Contains(" refineSkipped=True", line, StringComparison.Ordinal);
        // Nur Metadaten: weder Diktat noch abgeschnittene Antwort.
        Assert.DoesNotContain("geheim", f.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dik", f.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VollstaendigeAntwortLoggtKeineFehlerklasse()
    {
        _doneReason = "stop";
        using var f = new ControllerFixture(_settings, pipeline: _pipeline);

        f.Controller.Start();
        Assert.Equal("done", await f.Controller.StopAsync());

        Assert.Empty(Lines(f, "RefineTruncated"));
        Assert.Contains(" refineErrorClass=- ", Assert.Single(Lines(f, " dictation ")), StringComparison.Ordinal);
    }
}
