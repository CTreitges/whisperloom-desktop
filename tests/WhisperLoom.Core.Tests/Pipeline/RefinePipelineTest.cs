using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>
/// Port von <c>RefinePipelineTest.kt</c>: die Textverbesserung nach der Entscheidungstabelle, end-to-end über
/// <see cref="DictationPipeline"/>. Offline-Erkennung, Netz und lokales Textmodell als Fakes, der eigene Online-Zugang
/// als lokaler Fake-Server, der jede Anfrage zählt. Statt „Also hallo welt“ prüfen die Tests meist,
/// dass der Rohtext mit den vollen Regeln an den Polisher geht; das Literal mit echten Regeln prüft ein eigener Fall.
/// </summary>
public sealed class RefinePipelineTest : IAsyncLifetime
{
    private const string Raw = "also ähm hallo welt";

    private readonly PipelineHarness _h = new();
    private int _chatRequests;
    private int _sttRequests;
    private volatile int _chatStatus = 200;
    private TaskCompletionSource? _chatHold;
    private readonly TaskCompletionSource _chatArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask InitializeAsync()
    {
        _h.Server.MapAsync("/v1/chat/completions", async _ =>
        {
            Interlocked.Increment(ref _chatRequests);
            _chatArrived.TrySetResult();
            if (_chatHold is { } hold) await hold.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return _chatStatus == 200
                ? new FakeResponse(200, """{"choices":[{"message":{"content":"Online verbessert."}}]}""")
                : new FakeResponse(_chatStatus, """{"error":{"message":"kaputt"}}""");
        });
        _h.Server.Map("/v1/audio/transcriptions", _ =>
        {
            Interlocked.Increment(ref _sttRequests);
            return new FakeResponse(200, $$"""{"text":"{{Raw}}"}""");
        });
        _h.Stage(RefineMode.Polish);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _chatHold?.TrySetResult();
        await _h.DisposeAsync();
    }

    private sealed record Run(string Text, string? Skipped, string? Note, bool Started);

    private async Task<Run> Transcribe(RefineSkip? skip = null, Action? onStart = null)
    {
        var started = false;
        var d = await _h.TranscribeAsync(skip, () =>
        {
            started = true;
            onStart?.Invoke();
        });
        return new Run(d.Text, d.Result.Skipped, d.Result.Note, started);
    }

    /// <summary>Ohne KI: der Rohtext geht mit den vollen Regeln (Füllwörter, Großschreibung, ohne Umbrüche) an den Polisher.</summary>
    private void AssertRawWithFullRules(Run run)
    {
        Assert.Equal(Raw, run.Text);
        var (text, options) = _h.Polisher.Last;
        Assert.Equal(Raw, text);
        Assert.True(options.RemoveFillers);
        Assert.True(options.AutoCapitalize);
        Assert.False(options.KeepLineBreaks);
    }

    private int LocalCalls => _h.LocalRefiner.Calls.Count;

    [Fact]
    public async Task OhneKiKommtDerRohtextMitDenVollenRegeln()
    {
        // Kotlin: „Also hallo welt“ — volle Regeln ohne KI: „ähm“ weg, Satzanfang groß.
        _h.Offline(OfflineRefineRule.Local);
        _h.OwnAccess();
        _h.LocalRefiner.Ready = false;
        _h.RealText = true;
        var run = await Transcribe();
        Assert.Equal("Also hallo welt", run.Text);
        Assert.Equal(RefinePlan.MsgLocalMissing, run.Skipped);
    }

    [Fact]
    public async Task OfflineLokalMitModellRechnetLokalUndSchicktNichts()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.OwnAccess();
        var run = await Transcribe();
        Assert.Equal("Lokal verbessert.", run.Text);
        Assert.Null(run.Skipped);
        Assert.True(run.Started);
        Assert.Equal(0, _chatRequests);
        Assert.Equal([Raw], _h.LocalRefiner.Calls);
    }

    [Fact]
    public async Task OfflineLokalOhneModellKommtSofortOhneKiMitHinweis()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.OwnAccess();
        _h.LocalRefiner.Ready = false;
        var run = await Transcribe();
        AssertRawWithFullRules(run);
        Assert.Equal(RefinePlan.MsgLocalMissing, run.Skipped);
        Assert.False(run.Started);
        Assert.Equal(0, _chatRequests);
        Assert.Equal(0, LocalCalls);
    }

    /// <summary>Kotlin: E4B gewählt, Gerät zu klein. Desktop: Ollama meldet das gewählte Modell als nicht vorhanden.</summary>
    [Fact]
    public async Task OfflineLokalMitNichtBereitemModellGiltAlsFehlend()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.LocalRefiner.Ready = false;
        Assert.Equal(RefinePlan.MsgLocalMissing, (await Transcribe()).Skipped);
        Assert.True(_h.LocalRefiner.ReadyChecks > 0);
        Assert.Equal(0, LocalCalls);
    }

    /// <summary>Kotlin: kein Textmodell passt ins Gerät. Desktop: kein lokaler Textverbesserer eingerichtet.</summary>
    [Fact]
    public async Task PasstKeinTextmodellWirktDieRegelWieUeberspringen()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.HasLocalRefiner = false;
        Assert.Null((await Transcribe()).Skipped);
        _h.OwnAccess();
        Assert.Equal("Online verbessert.", (await Transcribe()).Text);
        Assert.Equal(1, _chatRequests);
        Assert.Equal(0, LocalCalls);
    }

    [Fact]
    public async Task LokaleRechnungScheitertGibtRohtextMitHinweis()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.LocalRefiner.Answer = _ => throw new InvalidOperationException("nativer Fehler");
        var run = await Transcribe();
        AssertRawWithFullRules(run);
        Assert.Equal(RefinePlan.MsgLocalFailed, run.Skipped);
    }

    [Fact]
    public async Task LokaleAntwortDieDieBitteErfuelltBehaeltIhreMeldung()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.LocalRefiner.Answer = _ => throw new RefineRejectedException("Modell hat geantwortet, statt den Text zu bearbeiten");
        var run = await Transcribe();
        AssertRawWithFullRules(run);
        Assert.Contains("statt den Text zu bearbeiten", run.Skipped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlineLokalMitNetzGehtOnline()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        _h.OwnAccess();
        Assert.Equal("Online verbessert.", (await Transcribe()).Text);
        Assert.Equal(1, _chatRequests);
        Assert.Equal(0, LocalCalls);
    }

    [Fact]
    public async Task OnlineLokalOnlineFehlerRechnetLokalMitHinweis()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        _h.OwnAccess();
        _chatStatus = 500;
        var run = await Transcribe();
        Assert.Equal("Lokal verbessert.", run.Text);
        Assert.Equal(1, _chatRequests);
        Assert.Equal(1, LocalCalls);
        Assert.Null(run.Skipped);
        Assert.Equal(RefinePlan.MsgOnlineFailedLocal, run.Note);
    }

    [Fact]
    public async Task OnlineLokalOnlineFehlerOhneModellKommtOhneKi()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        _h.OwnAccess();
        _h.LocalRefiner.Ready = false;
        _chatStatus = 500;
        var run = await Transcribe();
        AssertRawWithFullRules(run);
        Assert.Contains("500", run.Skipped, StringComparison.Ordinal);
        Assert.Null(run.Note);
    }

    [Fact]
    public async Task OnlineLokalOhneNetzRechnetLokalOhneAnfrage()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        _h.OwnAccess();
        _h.Net = false;
        Assert.Equal("Lokal verbessert.", (await Transcribe()).Text);
        Assert.Equal(0, _chatRequests);
    }

    [Fact]
    public async Task OnlineLokalOhneEigenenZugangRechnetLokal()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        Assert.Equal("Lokal verbessert.", (await Transcribe()).Text);
        Assert.Equal(0, _chatRequests);
    }

    [Fact]
    public async Task OnlineLokalOhneNetzUndOhneModellKommtOhneKiMitHinweis()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        _h.OwnAccess();
        _h.Net = false;
        _h.LocalRefiner.Ready = false;
        Assert.Equal(RefinePlan.MsgLocalMissing, (await Transcribe()).Skipped);
        Assert.Equal(0, _chatRequests);
    }

    [Fact]
    public void MitAusweichloesungNurAchtSekundenConnectTimeout()
    {
        Assert.Equal(8_000, RefinePlan.ConnectTimeoutMs(new RefineRoute.Online(FallbackLocal: true)));
        Assert.Equal(Http.ConnectTimeoutMs, RefinePlan.ConnectTimeoutMs(new RefineRoute.Online(FallbackLocal: false)));
        Assert.Equal(15_000, Http.ConnectTimeoutMs);
    }

    [Fact]
    public async Task UeberspringenMitNetzGehtOnline()
    {
        _h.Offline(OfflineRefineRule.Skip);
        _h.OwnAccess();
        Assert.Equal("Online verbessert.", (await Transcribe()).Text);
        Assert.Equal(1, _chatRequests);
        Assert.Equal(0, LocalCalls);
    }

    [Fact]
    public async Task UeberspringenOnlineFehlerKommtOhneKiNieLokal()
    {
        _h.Offline(OfflineRefineRule.Skip);
        _h.OwnAccess();
        _chatStatus = 500;
        var run = await Transcribe();
        AssertRawWithFullRules(run);
        Assert.Contains("500", run.Skipped, StringComparison.Ordinal);
        Assert.Equal(0, LocalCalls);
    }

    [Fact]
    public async Task UeberspringenOhneNetzSchicktKeineAnfrage()
    {
        _h.Offline(OfflineRefineRule.Skip);
        _h.OwnAccess();
        _h.Net = false;
        var run = await Transcribe();
        AssertRawWithFullRules(run);
        Assert.Equal(RefinePlan.MsgNoNet, run.Skipped);
        Assert.False(run.Started);
        Assert.Equal(0, _chatRequests);
    }

    [Fact]
    public async Task UeberspringenOhneEigenenZugangKommtOhneKiUndOhneHinweis()
    {
        _h.Offline(OfflineRefineRule.Skip);
        var run = await Transcribe();
        AssertRawWithFullRules(run);
        Assert.Null(run.Skipped);
        Assert.Equal(0, _chatRequests);
        Assert.Equal(0, LocalCalls);
    }

    [Fact]
    public async Task OnlineErkennungVerbessertOnlineWieBisher()
    {
        _h.UseLocalServer();
        Assert.Equal("Online verbessert.", (await Transcribe()).Text);
        Assert.Equal(1, _chatRequests);
        Assert.Equal(0, LocalCalls);
    }

    [Fact]
    public async Task OnlineErkennungOhneNetzDanachKommtOhneKiOhneAnfrage()
    {
        _h.UseLocalServer();
        _h.Net = false;
        var run = await Transcribe();
        Assert.Equal(1, _sttRequests);
        AssertRawWithFullRules(run);
        Assert.Equal(RefinePlan.MsgNoNet, run.Skipped);
        Assert.Equal(0, _chatRequests);
    }

    /// <summary>Kotlin: unvalidiertes WLAN. Desktop: die Netzprüfung bekommt „bewiesen“ nach einer Online-Erkennung.</summary>
    [Fact]
    public async Task OnlineErkennungBrauchtKeinValidiertesNetz()
    {
        _h.NetworkOverride = (_, proven) => proven;
        _h.Settings.Stt.ProviderId = ProviderCatalog.OpenAiId;
        _h.Secrets.Set(SecretNames.Stt, Keys.K1);
        _h.Online();
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(new RefineRoute.Online(false), (await _h.Pipeline.PlanAsync(RefineMode.Polish, networkProven: true, ct)).Route);

        _h.Offline(OfflineRefineRule.Skip);
        _h.Settings.Llm.ProviderId = ProviderCatalog.OpenAiId;
        _h.Secrets.Set(SecretNames.Llm, Keys.K2);
        Assert.Equal(new RefineRoute.Raw(RefineHint.NoNet), (await _h.Pipeline.PlanAsync(RefineMode.Polish, networkProven: false, ct)).Route);
    }

    [Fact]
    public async Task StufeAusFragtWederNetzNochModell()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.Stage(RefineMode.Off);
        _h.NetworkOverride = (_, _) => throw new InvalidOperationException("Netz gefragt");
        var run = await Transcribe();
        Assert.Equal(Raw, run.Text);
        Assert.Null(run.Skipped);
        Assert.False(run.Started);
        Assert.Equal(0, _h.LocalRefiner.ReadyChecks);
    }

    [Fact]
    public async Task AbbruchTippLiefertSofortDenRohtextUndBrichtLokalAb()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.LocalRefiner.HoldNext = true;
        var skip = new RefineSkip();
        var run = await Transcribe(skip, () => _ = _h.LocalRefiner.Started.Task.ContinueWith(_ => skip.Skip(), TaskScheduler.Default));
        AssertRawWithFullRules(run);
        Assert.Null(run.Skipped);
        await WaitUntil(() => _h.LocalRefiner.Cancels == 1);
        Assert.Equal(1, _h.LocalRefiner.Cancels);
    }

    [Fact]
    public async Task AbbruchTippVerwirftDieLaufendeOnlineAnfrage()
    {
        _h.Offline(OfflineRefineRule.Skip);
        _h.OwnAccess();
        _chatHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var skip = new RefineSkip();
        var run = await Transcribe(skip, () => _ = _chatArrived.Task.ContinueWith(_ => skip.Skip(), TaskScheduler.Default));
        Assert.Equal(Raw, run.Text);
        Assert.Equal(1, _chatRequests);
        Assert.Null(run.Skipped);
    }

    [Fact]
    public async Task OhneAbbruchLiefertDerAuswegDasErgebnis()
    {
        _h.Offline(OfflineRefineRule.Local);
        Assert.Equal("Lokal verbessert.", (await Transcribe(new RefineSkip())).Text);
    }

    [Fact]
    public async Task FehlerKommtAuchMitAuswegAlsHinweis()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.LocalRefiner.Answer = _ => throw new InvalidOperationException("nativer Fehler");
        var run = await Transcribe(new RefineSkip());
        AssertRawWithFullRules(run);
        Assert.Equal(RefinePlan.MsgLocalFailed, run.Skipped);
    }

    [Fact]
    public async Task VorwaermenNurWennLokalVerbessertWird()
    {
        _h.Offline(OfflineRefineRule.Local);
        Assert.True(await _h.Pipeline.ShouldWarmUpLocalAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VorwaermenMitFesterStufeAuchWennDasDiktatAusIst()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.Stage(RefineMode.Off);
        Assert.False(await _h.Pipeline.ShouldWarmUpLocalAsync(null, TestContext.Current.CancellationToken));
        Assert.True(await _h.Pipeline.ShouldWarmUpLocalAsync(RefineMode.Prompt, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeinVorwaermenBeiOnlineUeberspringenOderAus()
    {
        var ct = TestContext.Current.CancellationToken;
        _h.OwnAccess();
        _h.UseLocalServer();
        Assert.False(await _h.Pipeline.ShouldWarmUpLocalAsync(null, ct));
        _h.Offline(OfflineRefineRule.Skip);
        Assert.False(await _h.Pipeline.ShouldWarmUpLocalAsync(null, ct));
        _h.Offline(OfflineRefineRule.OnlineLocal);
        Assert.False(await _h.Pipeline.ShouldWarmUpLocalAsync(null, ct));
        _h.Offline(OfflineRefineRule.Local);
        _h.Stage(RefineMode.Off);
        Assert.False(await _h.Pipeline.ShouldWarmUpLocalAsync(null, ct));
        Assert.Equal(0, LocalCalls);
    }

    private bool? _startedLocal;

    private Task<SharedRefine.Result> Share(params string[] parts) =>
        _h.Pipeline.RefineSharedAsync(parts.Length == 0 ? ["Erstes Stück.", "Zweites Stück."] : parts, "de", null,
            local => _startedLocal = local, TestContext.Current.CancellationToken);

    [Fact]
    public async Task GeteilteAudiosOfflineLokal()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.OwnAccess();
        _h.Settings.Share.Stage = "polish";
        var result = await Share();
        Assert.Equal(["Lokal verbessert.", "Lokal verbessert."], result.Paragraphs!);
        Assert.Null(result.Skipped);
        Assert.Equal(2, LocalCalls);
        Assert.Equal(0, _chatRequests);
        Assert.True(_startedLocal);
        Assert.False(result.LocalFallback);
    }

    [Fact]
    public async Task GeteilteAudiosOnlineMeldenDenStartAlsOnline()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        _h.OwnAccess();
        _h.Settings.Share.Stage = "polish";
        var result = await Share();
        Assert.Equal(["Online verbessert.", "Online verbessert."], result.Paragraphs!);
        Assert.False(_startedLocal);
        Assert.False(result.LocalFallback);
    }

    [Fact]
    public async Task GeteilteAudiosOhneModellOhneKiMitHinweis()
    {
        _h.Offline(OfflineRefineRule.Local);
        _h.LocalRefiner.Ready = false;
        _h.Settings.Share.Stage = "polish";
        var result = await Share();
        Assert.Equal(RefineMode.Polish, result.Mode);
        Assert.Null(result.Paragraphs);
        Assert.Equal(RefinePlan.MsgLocalMissing, result.Skipped);
    }

    [Fact]
    public async Task GeteilteAudiosOhneNetzSchickenNichts()
    {
        _h.Offline(OfflineRefineRule.Skip);
        _h.OwnAccess();
        _h.Net = false;
        _h.Settings.Share.Stage = "polish";
        Assert.Equal(RefinePlan.MsgNoNet, (await Share()).Skipped);
        Assert.Equal(0, _chatRequests);
    }

    [Fact]
    public async Task GeteilteAudiosUeberspringenOhneZugangWieAus()
    {
        _h.Offline(OfflineRefineRule.Skip);
        _h.Settings.Share.Stage = "polish";
        Assert.Equal(new SharedRefine.Result(RefineMode.Off, null, null), await Share());
    }

    [Fact]
    public async Task GeteilteAudiosNachOnlineFehlerDieRestlichenStueckeGleichLokal()
    {
        _h.Offline(OfflineRefineRule.OnlineLocal);
        _h.OwnAccess();
        _chatStatus = 500;
        _h.Settings.Share.Stage = "polish";
        var result = await Share();
        Assert.Equal(["Lokal verbessert.", "Lokal verbessert."], result.Paragraphs!);
        Assert.Equal(1, _chatRequests);
        Assert.Equal(2, LocalCalls);
        Assert.True(result.LocalFallback);
        Assert.Null(result.Skipped);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
    }
}
