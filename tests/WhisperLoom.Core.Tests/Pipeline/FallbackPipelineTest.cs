using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>
/// Port von <c>FallbackPipelineTest.kt</c>: Fallback von Erkennung und Textverbesserung end-to-end. Zwei „eigene Server“
/// auf einem Fake-Server — der erste unter /erst/v1, der Fallback unter /fallback/v1, jeder mit Zähler und Status.
/// </summary>
public sealed class FallbackPipelineTest : IAsyncLifetime
{
    private const string Custom = "Eigener Server (OpenAI-kompatibel)";

    private sealed class Endpoint(int status, string answer)
    {
        public volatile int Status = status;
        public int Hits;
        public string Answer => answer;
    }

    private readonly PipelineHarness _h = new();
    private readonly Endpoint _firstStt = new(200, """{"text":"erster anbieter"}""");
    private readonly Endpoint _fallbackStt = new(200, """{"text":"also ähm hallo welt"}""");
    private readonly Endpoint _firstChat = new(200, """{"choices":[{"message":{"content":"Vom ersten verbessert."}}]}""");
    private readonly Endpoint _fallbackChat = new(200, """{"choices":[{"message":{"content":"Vom Fallback verbessert."}}]}""");

    public ValueTask InitializeAsync()
    {
        Map("/erst/v1/audio/transcriptions", _firstStt);
        Map("/fallback/v1/audio/transcriptions", _fallbackStt);
        Map("/erst/v1/chat/completions", _firstChat);
        Map("/fallback/v1/chat/completions", _fallbackChat);
        _h.Stage(RefineMode.Polish);
        _h.Online();
        var s = _h.Settings;
        s.Stt.ProviderId = "custom";
        s.Stt.BaseUrl = _h.Server.Url("/erst/v1");
        s.Stt.Model = "whisper-1";
        s.Llm.ProviderId = "custom";
        s.Llm.BaseUrl = _h.Server.Url("/erst/v1");
        s.Llm.Model = "qwen3:8b";
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private void Map(string path, Endpoint e) => _h.Server.Map(path, _ =>
    {
        Interlocked.Increment(ref e.Hits);
        return e.Status == 200 ? new FakeResponse(200, e.Answer) : new FakeResponse(e.Status, "{\"error\":{\"message\":\"kaputt " + e.Status + "\"}}");
    });

    private void SttFallback(string model = "whisper-large-v3")
    {
        var f = _h.Settings.SttFallback;
        f.Enabled = true;
        f.ProviderId = "custom";
        f.BaseUrl = _h.Server.Url("/fallback/v1");
        f.Model = model;
    }

    private void LlmFallback(string model = "gemma4:12b")
    {
        var f = _h.Settings.LlmFallback;
        f.Enabled = true;
        f.ProviderId = "custom";
        f.BaseUrl = _h.Server.Url("/fallback/v1");
        f.Model = model;
    }

    private Task<DictationResult> Transcribe() => _h.TranscribeAsync();

    [Fact]
    public async Task ErsterAnbieterErkenntFallbackBleibtStill()
    {
        SttFallback();
        var d = await Transcribe();
        Assert.Equal("erster anbieter", d.Raw);
        Assert.Equal(0, _fallbackStt.Hits);
        Assert.Null(d.Result.Note);
        Assert.False(d.UsedSttFallback);
    }

    [Fact]
    public async Task ErsterScheitertFallbackErkenntMitHinweis()
    {
        SttFallback();
        _firstStt.Status = 503;
        var d = await Transcribe();
        Assert.Equal(1, _firstStt.Hits);
        Assert.Equal(1, _fallbackStt.Hits);
        Assert.Equal("also ähm hallo welt", d.Raw);
        Assert.Equal("Vom ersten verbessert.", d.Text);
        Assert.Equal(DictationPipeline.SttFallbackMessage(Custom), d.Result.Note);
        Assert.True(d.UsedSttFallback);
        Assert.Equal(Custom, d.SttLabel);
    }

    [Fact]
    public async Task OhneFallbackBleibtEsBeimFehler()
    {
        _firstStt.Status = 503;
        var e = await Assert.ThrowsAsync<ApiHttpException>(Transcribe);
        Assert.Equal(503, e.StatusCode);
        Assert.Equal(0, _fallbackStt.Hits);
    }

    [Fact]
    public async Task AusgeschalteterFallbackSpringtNichtEin()
    {
        SttFallback();
        _h.Settings.SttFallback.Enabled = false;
        _firstStt.Status = 503;
        await Assert.ThrowsAsync<ApiHttpException>(Transcribe);
        Assert.Equal(0, _fallbackStt.Hits);
    }

    [Fact]
    public async Task UnvollstaendigerFallbackSpringtNichtEin()
    {
        SttFallback(model: "");
        _firstStt.Status = 503;
        await Assert.ThrowsAsync<ApiHttpException>(Transcribe);
        Assert.Equal(0, _fallbackStt.Hits);
    }

    [Fact]
    public async Task ScheiternBeideKommtDerFehlerDesErsten()
    {
        SttFallback();
        _firstStt.Status = 401;
        _fallbackStt.Status = 500;
        var e = await Assert.ThrowsAsync<ApiHttpException>(Transcribe);
        Assert.Equal(401, e.StatusCode);
        Assert.Equal(500, Assert.IsType<ApiHttpException>(FallbackBackend.Suppressed(e)).StatusCode);
    }

    [Fact]
    public async Task KiZugangScheitertFallbackVerbessertMitHinweis()
    {
        LlmFallback();
        _firstChat.Status = 500;
        var d = await Transcribe();
        Assert.Equal("Vom Fallback verbessert.", d.Text);
        Assert.Null(d.Result.Skipped);
        Assert.Equal(RefinePlan.FallbackMessage(Custom), d.Result.Note);
        Assert.Equal("gemma4:12b", d.Result.Model);
        Assert.Equal(1, _firstChat.Hits);
        Assert.Equal(1, _fallbackChat.Hits);
    }

    [Fact]
    public async Task KiZugangKlapptFallbackWirdNichtGefragt()
    {
        LlmFallback();
        var d = await Transcribe();
        Assert.Equal("Vom ersten verbessert.", d.Text);
        Assert.Equal("qwen3:8b", d.Result.Model);
        Assert.Null(d.Result.Note);
        Assert.Equal(0, _fallbackChat.Hits);
    }

    [Fact]
    public async Task ScheiternBeideKommtDerTextOhneKiMitBeidenGruenden()
    {
        LlmFallback();
        _firstChat.Status = 500;
        _fallbackChat.Status = 401;
        var d = await Transcribe();
        Assert.Equal("erster anbieter", d.Text);
        Assert.Equal(("erster anbieter", true), (_h.Polisher.Last.Text, _h.Polisher.Last.Options.RemoveFillers));
        var skipped = Assert.IsType<string>(d.Result.Skipped);
        Assert.Contains("API-Fehler 500", skipped, StringComparison.Ordinal);
        Assert.Contains("Fallback: API-Fehler 401", skipped, StringComparison.Ordinal);
        Assert.Null(d.Result.Model);
    }

    [Fact(Skip = P1a.Reason)]
    [Trait("Needs", "P1a")]
    public void ScheiternBeideKommtDerRohtextMitDenVollenRegeln() =>
        Assert.Equal("Erster anbieter", P1a.Polisher.Polish("erster anbieter", new PolishOptions(Language: "de")));

    [Fact]
    public async Task OhneFallbackKommtDerTextOhneKiWieBisher()
    {
        _firstChat.Status = 500;
        var d = await Transcribe();
        Assert.Equal("erster anbieter", d.Text);
        Assert.StartsWith("API-Fehler 500", d.Result.Skipped, StringComparison.Ordinal);
        Assert.Equal(0, _fallbackChat.Hits);
    }

    [Fact]
    public async Task UnvollstaendigerFallbackDerTextverbesserungSpringtNichtEin()
    {
        LlmFallback(model: "");
        _firstChat.Status = 500;
        Assert.StartsWith("API-Fehler 500", (await Transcribe()).Result.Skipped, StringComparison.Ordinal);
        Assert.Equal(0, _fallbackChat.Hits);
    }

    [Fact]
    public async Task BeideFallbacksInEinemDiktatNennenBeideHinweise()
    {
        SttFallback();
        LlmFallback();
        _firstStt.Status = 503;
        _firstChat.Status = 500;
        var d = await Transcribe();
        Assert.Equal("Vom Fallback verbessert.", d.Text);
        Assert.Equal(DictationPipeline.SttFallbackMessage(Custom) + " · " + RefinePlan.FallbackMessage(Custom), d.Result.Note);
    }

    [Fact]
    public async Task OfflineOnlineLokalErstFallbackDannLokal()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        LlmFallback();
        _firstChat.Status = 500;
        Assert.Equal("Vom Fallback verbessert.", (await Transcribe()).Text);
        Assert.Empty(_h.LocalRefiner.Calls);

        _fallbackChat.Status = 500;
        var d = await Transcribe();
        Assert.Equal("Lokal verbessert.", d.Text);
        Assert.Equal(RefinePlan.MsgOnlineFailedLocal, d.Result.Note);
    }

    [Fact]
    public async Task GeteilteAudiosNachAusfallDieRestlichenStueckeGleichZumFallback()
    {
        LlmFallback();
        _firstChat.Status = 500;
        _h.Settings.Share.Stage = "polish";
        var result = await _h.Pipeline.RefineSharedAsync(["Erstes Stück.", "Zweites Stück."], "de", null, null, TestContext.Current.CancellationToken);
        Assert.Equal(["Vom Fallback verbessert.", "Vom Fallback verbessert."], result.Paragraphs!);
        Assert.Equal(1, _firstChat.Hits);
        Assert.Equal(2, _fallbackChat.Hits);
        Assert.False(result.LocalFallback);
        Assert.Null(result.Skipped);
    }
}
