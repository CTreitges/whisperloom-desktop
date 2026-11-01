using Avalonia.Headless.XUnit;
using WhisperLoom.App.Output;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Windows;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Komfort aus der Android-App (W2-OUT, Prüfrunde 1): &lt; 0,3 s verwerfen, 12-min-Deckel mit Hinweis ab 10 min, Kürzel während der
/// Verbesserung = sofort ohne KI, „Erneut senden“ nach wiederholbarem Fehler.
/// </summary>
public sealed class ComfortTest
{
    private static WhisperLoomSettings Polish() => new()
    {
        Engine = "online",
        Refine = new RefineSettings { Stage = "polish" },
        Polish = new PolishSettings { TrailingSpace = false },
    };

    // --- < 0,3 s ------------------------------------------------------------------------------

    [Fact]
    public async Task Kuerzer_als_300_ms_wird_verworfen()
    {
        using var f = new ControllerFixture(Polish(), recorder: new FakeRecorder(samples: 4_000)); // 250 ms
        f.Controller.Start();
        Assert.Equal("too-short", await f.Controller.StopAsync());
        Assert.Equal(DictationState.Idle, f.Controller.State);
        Assert.Equal(0, f.Pipeline.Runs);
        Assert.Empty(f.Sink.Written);
        Assert.Empty(await f.History.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Ab_300_ms_wird_erkannt()
    {
        using var f = new ControllerFixture(Polish(), recorder: new FakeRecorder(samples: 4_800)); // 300 ms
        f.Controller.Start();
        Assert.Equal("done", await f.Controller.StopAsync());
        Assert.Equal(1, f.Pipeline.Runs);
    }

    // --- 12-min-Deckel ------------------------------------------------------------------------

    [Fact]
    public void Hinweis_ab_10_min_einmal_dann_Deckel_bei_12_min()
    {
        using var f = new ControllerFixture(Polish());
        f.Controller.Start();
        f.Now += TimeSpan.FromMinutes(9);
        Assert.Null(f.Controller.CheckLength());
        f.Now += TimeSpan.FromMinutes(1);
        Assert.Equal("long", f.Controller.CheckLength());
        Assert.Null(f.Controller.CheckLength());
        f.Now += TimeSpan.FromMinutes(2);
        Assert.Equal("capped", f.Controller.CheckLength());
        Assert.Equal(DictationState.Paused, f.Controller.State);
        Assert.True(f.Controller.Capped);
        Assert.Equal("capped", f.Controller.Resume());
        Assert.Equal(DictationState.Paused, f.Controller.State);
    }

    [Fact]
    public void Offline_ohne_Deckel()
    {
        var s = Polish();
        s.Engine = "offline";
        using var f = new ControllerFixture(s);
        f.Controller.Start();
        f.Now += TimeSpan.FromMinutes(30);
        Assert.Null(f.Controller.CheckLength());
        Assert.Equal(DictationState.Recording, f.Controller.State);
    }

    [Fact]
    public void Neue_Aufnahme_setzt_den_Deckel_zurueck()
    {
        using var f = new ControllerFixture(Polish());
        f.Controller.Start();
        f.Now += TimeSpan.FromMinutes(12);
        f.Controller.CheckLength();
        f.Controller.Cancel();
        f.Controller.Start();
        Assert.False(f.Controller.Capped);
    }

    [AvaloniaFact]
    public async Task Halten_ueber_12_min_sendet_wie_Loslassen()
    {
        var (f, rig, flow, ui) = Flow(Polish());
        using var _ = f;
        await flow.HandleAsync(HotkeyAction.HoldStart);
        f.Now += TimeSpan.FromMinutes(12);
        flow.Tick();
        await WaitFor(() => f.Controller.State == DictationState.Idle);
        Assert.Equal(f.Controller.LastResult, rig.Keys.Target);
        Assert.Contains(ui.Hints, h => h.Message == Localization.Loc.Instance["Recording_Capped"]);
        await flow.HandleAsync(HotkeyAction.HoldEnd); // echte Freigabe danach: läuft ins Leere
        Assert.Equal(1, f.Pipeline.Runs);
    }

    [AvaloniaFact]
    public async Task Umschalten_ueber_12_min_pausiert_das_Overlay()
    {
        var (f, _, flow, _) = Flow(Polish());
        using var __ = f;
        await flow.HandleAsync(HotkeyAction.Toggle);
        f.Now += TimeSpan.FromMinutes(12);
        flow.Tick();
        Assert.Equal(OverlayState.Paused, flow.Overlay.State);
        flow.Overlay.PauseResumeCommand.Execute(null); // „Weiter“ bleibt gesperrt
        Assert.Equal(OverlayState.Paused, flow.Overlay.State);
        await flow.HandleAsync(HotkeyAction.OverlayDiscard);
    }

    // --- Kürzel während der Verbesserung = ohne KI ---------------------------------------------

    [AvaloniaFact]
    public async Task Kuerzel_waehrend_der_Verbesserung_fuegt_sofort_ohne_KI_ein()
    {
        var pipeline = new SlowRefinePipeline();
        var (f, rig, flow, _) = Flow(Polish(), pipeline);
        using var _ = f;
        await flow.HandleAsync(HotkeyAction.Toggle);
        var send = flow.HandleAsync(HotkeyAction.OverlaySend);
        await WaitFor(() => f.Controller.IsRefining);
        await flow.HandleAsync(HotkeyAction.Toggle);
        await send;
        Assert.Equal("Hallo Welt", rig.Keys.Target);
        Assert.Null(f.Controller.LastRefined!.Model);
        Assert.Null(f.Controller.LastRefined.Skipped); // ohne Hinweis, wie Android
    }

    [Fact]
    public void Ohne_laufende_Verbesserung_nichts_zu_ueberspringen()
    {
        using var f = new ControllerFixture(Polish());
        Assert.False(f.Controller.SkipRefine());
    }

    // --- Erneut senden ------------------------------------------------------------------------

    [Fact]
    public async Task Netzfehler_haelt_das_Audio_fuer_Erneut_senden()
    {
        using var f = new ControllerFixture(Polish());
        f.Pipeline.Failure = FakeFailure.SttNetwork;
        f.Controller.Start();
        Assert.Equal("error", await f.Controller.StopAsync());
        Assert.True(f.Controller.CanRetry);

        f.Pipeline.Failure = FakeFailure.None;
        Assert.Equal("done", await f.Controller.RetryAsync());
        Assert.False(f.Controller.CanRetry);
        Assert.Single(await f.History.ListAsync(CancellationToken.None));
        Assert.Equal("nothing", await f.Controller.RetryAsync());
        Assert.Empty(Directory.EnumerateFiles(f.DataDir, "*.wav", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Nicht_wiederholbarer_Fehler_behaelt_nichts()
    {
        using var f = new ControllerFixture(Polish());
        f.Pipeline.Failure = FakeFailure.SttNotConfigured;
        f.Controller.Start();
        await f.Controller.StopAsync();
        Assert.False(f.Controller.CanRetry);
    }

    [Fact]
    public async Task Neue_Aufnahme_verwirft_das_alte_Audio()
    {
        using var f = new ControllerFixture(Polish());
        f.Pipeline.Failure = FakeFailure.SttNetwork;
        f.Controller.Start();
        await f.Controller.StopAsync();
        f.Controller.Start();
        f.Controller.Cancel();
        Assert.False(f.Controller.CanRetry);
    }

    [AvaloniaFact]
    public async Task Erneut_senden_im_Overlay_fuegt_ins_selbe_Ziel_ein()
    {
        var (f, rig, flow, _) = Flow(Polish());
        using var _ = f;
        f.Pipeline.Failure = FakeFailure.SttNetwork;
        await flow.HandleAsync(HotkeyAction.Toggle);
        var send = flow.HandleAsync(HotkeyAction.OverlaySend);
        await WaitFor(() => flow.Overlay.State == OverlayState.Error);
        Assert.True(flow.Overlay.CanRetry);
        f.Pipeline.Failure = FakeFailure.None;
        flow.Overlay.RetrySendCommand.Execute(null);
        await WaitFor(() => rig.Keys.Target.Length > 0);
        Assert.Equal(f.Controller.LastResult, rig.Keys.Target);
        GC.KeepAlive(send); // läuft im Hintergrund bis zum Ende des Retry-Fensters aus
    }

    // --- Helfer --------------------------------------------------------------------------------

    private static (ControllerFixture F, InsertRig Rig, InputFlow Flow, FakePresenter Ui) Flow(WhisperLoomSettings s, IDictationPipeline? pipeline = null)
    {
        var rig = new InsertRig();
        var ui = new FakePresenter();
        ControllerFixture? f = null;
        var sink = new InsertOutputSink(new CaptureSink(), rig.Inserter, () => InsertMode.Paste, () => f!.Controller.Settings.Output);
        f = new ControllerFixture(s, sink, pipeline: pipeline);
        f.Pipeline.RawText = "Hallo Welt";
        var flow = new InputFlow(f.Controller, sink, ui, () => rig.Windows.Current, _ => { });
        return (f, rig, flow, ui);
    }

    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 1000 && !done(); i++) // bis 10 s: im Gesamtlauf unter Last einmal knapp (W2-OUT)
        {
            await Task.Delay(10);
        }
        Assert.True(done());
    }

    /// <summary>Erkennung sofort, Verbesserung hängt, bis übersprungen wird (wie eine langsame KI).</summary>
    private sealed class SlowRefinePipeline : IDictationPipeline, ISkippablePipeline
    {
        public Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("ohne Ausweg aufgerufen");

        public async Task<DictationResult> RunAsync(DictationRequest request, RefineSkip skip, Action? onRefineStart, CancellationToken ct)
        {
            onRefineStart?.Invoke();
            while (!skip.IsSkipped)
            {
                await Task.Delay(5, ct);
            }
            return new DictationResult("Hallo Welt", "de", request.Audio.DurationMs, new RefinedText(request.Refinement, "Hallo Welt"), "Fake-STT");
        }

        public Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct) =>
            Task.FromResult(new RefinedText(refinement, raw));
    }
}
