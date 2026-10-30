using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>Port von <c>StageModelTest.kt</c>: welches <c>"model"</c> je Stufe an den Server geht (Zugang „Anthropic“ auf dem Fake-Server).</summary>
public sealed class StageModelTest : IAsyncLifetime
{
    private readonly PipelineHarness _h = new();
    private readonly ConcurrentQueue<JsonObject> _bodies = new();

    public ValueTask InitializeAsync()
    {
        _h.Server.Map("/v1/audio/transcriptions", _ => new FakeResponse(200, """{"text":"also ähm hallo welt"}"""));
        _h.Server.Map("/v1/chat/completions", r =>
        {
            _bodies.Enqueue(JsonNode.Parse(r.Text)!.AsObject());
            return new FakeResponse(200, """{"choices":[{"message":{"content":"Hallo Welt."},"finish_reason":"stop"}]}""");
        });
        _h.Online();
        var s = _h.Settings;
        s.Stt.ProviderId = "custom";
        s.Stt.BaseUrl = _h.V1;
        s.Stt.Model = "whisper-1";
        s.Llm.ProviderId = "anthropic";
        s.Llm.BaseUrl = _h.V1;
        _h.Secrets.Set(SecretNames.Llm, Keys.K1);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private List<string> Models => [.. _bodies.Select(b => (string)b["model"]!)];

    private void StageModel(RefineMode mode, string model) => _h.Settings.Llm.StageModels[mode.ModelStage()!.Value.Key()] = model;

    private async Task<string> Diktat(RefineMode mode, bool readable = false)
    {
        _h.Stage(mode);
        _h.Settings.Refine.PolishCleanup = readable ? "readable" : "plain";
        await _h.TranscribeAsync();
        return Models[^1];
    }

    [Fact]
    public async Task OhneEigeneModelleDieEmpfehlungJeStufe()
    {
        Assert.Equal("claude-haiku-5-5", await Diktat(RefineMode.Polish));
        Assert.Equal("claude-haiku-5-5", await Diktat(RefineMode.Polish, readable: true));
        Assert.Equal("claude-sonnet-5-5", await Diktat(RefineMode.Beautify));
        Assert.Equal("claude-sonnet-5-5", await Diktat(RefineMode.Summarize));
        Assert.Equal(4, _bodies.Count);
    }

    [Fact]
    public async Task DiktatSchicktDasModellSeinerStufe()
    {
        StageModel(RefineMode.Polish, "claude-sonnet-5");
        StageModel(RefineMode.Beautify, "claude-opus-5-5");
        Assert.Equal("claude-opus-5-5", await Diktat(RefineMode.Beautify));
        Assert.Equal("claude-sonnet-5", await Diktat(RefineMode.Polish, readable: true));
        Assert.Equal("claude-sonnet-5-5", await Diktat(RefineMode.Summarize));
    }

    [Fact]
    public async Task DasModellDesZugangsGiltFuerStufenOhneEigenes()
    {
        _h.Settings.Llm.Model = "claude-sonnet-5";
        StageModel(RefineMode.Summarize, "claude-opus-5-5");
        Assert.Equal("claude-sonnet-5", await Diktat(RefineMode.Beautify));
        Assert.Equal("claude-opus-5-5", await Diktat(RefineMode.Summarize));
    }

    [Fact]
    public async Task TogetherWieErkennungOhneModellSchicktAuchMitStufenModellNichts()
    {
        _h.Settings.Stt.ProviderId = "together";
        _h.Secrets.Set(SecretNames.Stt, Keys.K2);
        _h.Settings.Llm.ProviderId = AccessResolver.LlmSame;
        StageModel(RefineMode.Beautify, "Y");
        _h.Stage(RefineMode.Beautify);
        await _h.TranscribeAsync();
        Assert.Empty(_bodies);
        var keys = await SettingsAccess.KeysAsync(_h.Secrets, TestContext.Current.CancellationToken);
        Assert.False(SetupState.LlmReady(SettingsAccess.Llm(_h.Settings, keys, mode: RefineMode.Beautify)));
    }

    [Fact]
    public void OllamaOhneModellDesZugangsIstAuchMitStufenModellNichtBereit()
    {
        _h.Settings.Llm.ProviderId = "ollama";
        _h.Settings.Llm.Model = "";
        StageModel(RefineMode.Beautify, "gemma4:26b");
        var access = SettingsAccess.Llm(_h.Settings, new AccessKeys(), mode: RefineMode.Beautify);
        Assert.False(SetupState.LlmReady(access));
        Assert.Equal("", access.Model);
    }

    [Fact]
    public async Task SprachnachrichtSchicktDasModellIhrerStufe()
    {
        var ct = TestContext.Current.CancellationToken;
        _h.Stage(RefineMode.Polish);
        _h.Settings.Share.Stage = "summarize";
        StageModel(RefineMode.Summarize, "claude-opus-5-5");
        var result = await _h.Pipeline.RefineSharedAsync(["Erstes Stück.", "Zweites Stück."], "de", null, null, ct);
        Assert.Equal(RefineMode.Summarize, result.Mode);
        Assert.Equal(["claude-opus-5-5", "claude-opus-5-5"], Models);
        _bodies.Clear();
        StageModel(RefineMode.Summarize, "");
        await _h.Pipeline.RefineSharedAsync(["Hallo."], "de", null, null, ct);
        Assert.Equal("claude-sonnet-5-5", Assert.Single(Models));
    }

    [Fact]
    public async Task SprachnachrichtUndDiktatTeilenDasModellDerStufeGleichWelcheBereinigung()
    {
        StageModel(RefineMode.Polish, "claude-sonnet-5");
        _h.Settings.Share.Stage = "polish";
        foreach (var cleanup in Enum.GetValues<PolishCleanup>())
        {
            _h.Settings.Share.PolishCleanup = cleanup.Key();
            await _h.Pipeline.RefineSharedAsync(["Hallo."], "de", null, null, TestContext.Current.CancellationToken);
            Assert.Equal("claude-sonnet-5", Models[^1]);
        }
        _h.Settings.Refine.PolishCleanup = "clean";
        _h.Stage(RefineMode.Polish);
        await _h.TranscribeAsync();
        Assert.Equal("claude-sonnet-5", Models[^1]);
    }
}
