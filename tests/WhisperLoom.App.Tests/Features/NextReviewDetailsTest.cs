using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Output;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Windows;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// TS-WTRAY, Review-Details zu W3-NEXT: (1) „Esc = sofort ohne KI“ steht im Overlay nur, solange die Verbesserung läuft (in der
/// Erkennung wirkt Esc nicht); (2) <c>refine.skipped</c> steht genau dann im Log, wenn eine Verbesserung wirklich übersprungen
/// wurde, und dann genau einmal je Diktat.
/// </summary>
public sealed class NextReviewDetailsTest : IDisposable
{
    private static WhisperLoomSettings Polish() => new()
    {
        Engine = "online",
        Refine = new RefineSettings { Stage = "polish" },
        Polish = new PolishSettings { TrailingSpace = false },
    };

    private ControllerFixture? _fixture;

    public void Dispose() => _fixture?.Dispose();

    private ControllerFixture Fixture(GatedRefinePipeline pipeline, IOutputSink? output = null) =>
        _fixture = new ControllerFixture(Polish(), output, pipeline: pipeline);

    private static int SkippedLogs(ControllerFixture f) =>
        f.LogText.Split('\n').Count(l => l.Contains(" refine.skipped", StringComparison.Ordinal));

    /// <summary>Sichtbarer Text des Overlays, auch der Tastenkappen-Zeile (Runs).</summary>
    private static string VisibleText(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var parts = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible)
            .Select(t => t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? "");
        return string.Join(" | ", parts);
    }

    // (1) Esc-Hinweis nur in der Verbesserungsphase

    [AvaloniaFact]
    public async Task Esc_Hinweis_fehlt_in_der_Erkennung_und_steht_in_der_Verbesserung()
    {
        Loc.Instance.SetLanguage("de");
        var pipeline = new GatedRefinePipeline { HoldRecognition = true };
        var rig = new InsertRig();
        InsertOutputSink? sink = null;
        sink = new InsertOutputSink(new CaptureSink(), rig.Inserter, () => InsertMode.Paste, () => _fixture!.Controller.Settings.Output);
        var f = Fixture(pipeline, sink);
        var flow = new InputFlow(f.Controller, sink, new FakePresenter(), () => rig.Windows.Current, _ => { });
        var window = new OverlayWindow(flow.Overlay);
        window.Show();

        // Diktat 1 senden: es wird erkannt, das Overlay zeigt „Verarbeitung“.
        await flow.HandleAsync(HotkeyAction.Toggle);
        f.Now += TimeSpan.FromSeconds(1);
        var send1 = flow.HandleAsync(HotkeyAction.Toggle);
        await Waits.For(() => pipeline.Recognizing(1));
        flow.Tick();
        Assert.Equal(OverlayState.Processing, flow.Overlay.State);
        Assert.DoesNotContain("Esc", VisibleText(window), StringComparison.Ordinal);

        // Nächste Aufnahme während der Erkennung von Diktat 1: Hinweis aufs vorige Diktat, aber ohne Esc.
        await flow.HandleAsync(HotkeyAction.Toggle);
        flow.Tick();
        Assert.True(flow.Overlay.PreviousPending);
        var recognizing = VisibleText(window);
        Assert.Contains("Voriges Diktat", recognizing, StringComparison.Ordinal);
        Assert.DoesNotContain("Esc = sofort ohne KI", recognizing, StringComparison.Ordinal);

        // Diktat 1 wird jetzt verbessert: Esc wirkt, der Hinweis nennt es.
        pipeline.ReleaseRecognition(1);
        await Waits.For(() => pipeline.Refining(1));
        flow.Tick();
        Assert.Contains("Esc = sofort ohne KI", VisibleText(window), StringComparison.Ordinal);

        pipeline.Release(1);
        await send1.Within();
        window.Close();
    }

    // (2) refine.skipped genau einmal je echtem Überspringen

    [AvaloniaFact]
    public async Task Esc_waehrend_der_Verbesserung_loggt_genau_einmal()
    {
        var pipeline = new GatedRefinePipeline();
        var f = Fixture(pipeline);
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(1);
        var stop = f.Controller.StopAsync();
        await Waits.For(() => pipeline.Refining(1));

        Assert.True(f.Controller.SkipRefine());
        Assert.Equal("done", await stop.Within());
        Assert.Equal(["Diktat 1"], f.Sink.Written);
        Assert.Equal(1, SkippedLogs(f));
    }

    [AvaloniaFact]
    public async Task Esc_und_Doppeldruck_auf_dieselbe_Verbesserung_loggen_einmal()
    {
        var pipeline = new GatedRefinePipeline();
        var f = Fixture(pipeline);
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(1);
        var stop = f.Controller.StopAsync();
        await Waits.For(() => pipeline.Refining(1));

        Assert.True(f.Controller.SkipRefine());
        Assert.Equal("recording", await f.Controller.ToggleAsync()); // nächste Aufnahme …
        Assert.Equal("skipped", await f.Controller.ToggleAsync()); // … und gleich Doppeldruck
        await stop.Within();
        await Waits.For(() => f.Controller.State == DictationState.Idle);
        Assert.Equal(["Diktat 1"], f.Sink.Written);
        Assert.Equal(1, SkippedLogs(f));
    }

    [AvaloniaFact]
    public async Task Doppeldruck_ohne_geplante_Verbesserung_loggt_nichts()
    {
        var pipeline = new GatedRefinePipeline { HoldRecognition = true, NoRefine = true };
        var f = Fixture(pipeline);
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(1);
        var stop = f.Controller.StopAsync();
        await Waits.For(() => pipeline.Recognizing(1));

        Assert.Equal("recording", await f.Controller.ToggleAsync());
        Assert.Equal("skipped", await f.Controller.ToggleAsync()); // Doppeldruck: es gibt nichts zu überspringen
        pipeline.ReleaseRecognition(1);
        await stop.Within();
        await Waits.For(() => f.Controller.State == DictationState.Idle);
        Assert.Equal(["Diktat 1"], f.Sink.Written);
        Assert.Equal(0, SkippedLogs(f));
    }

    [AvaloniaFact]
    public async Task Doppeldruck_in_der_Erkennung_loggt_einmal_wenn_die_Verbesserung_dann_uebersprungen_wird()
    {
        var pipeline = new GatedRefinePipeline { HoldRecognition = true };
        var f = Fixture(pipeline);
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(1);
        var stop = f.Controller.StopAsync();
        await Waits.For(() => pipeline.Recognizing(1));

        Assert.Equal("recording", await f.Controller.ToggleAsync());
        Assert.Equal("skipped", await f.Controller.ToggleAsync());
        pipeline.ReleaseRecognition(1); // die Verbesserung beginnt und endet sofort ohne KI
        await stop.Within();
        await Waits.For(() => f.Controller.State == DictationState.Idle);
        Assert.Equal(["Diktat 1"], f.Sink.Written);
        Assert.Equal(1, SkippedLogs(f));
    }

    /// <summary>Prüfer-Fund: Die Verbesserung scheiterte wirklich (z. B. 401), Esc kam erst danach – das ist kein Überspringen.</summary>
    [AvaloniaFact]
    public async Task Gescheiterte_Verbesserung_mit_spaetem_Esc_loggt_nichts()
    {
        var pipeline = new GatedRefinePipeline { RefineFailure = "Fake-401" };
        var f = Fixture(pipeline);
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(1);
        var stop = f.Controller.StopAsync();
        await Waits.For(() => pipeline.Refining(1));

        Assert.True(f.Controller.SkipRefine());
        await stop.Within();
        Assert.Equal(["Diktat 1"], f.Sink.Written);
        Assert.Equal(0, SkippedLogs(f));
    }

    [AvaloniaFact]
    public async Task Ohne_Ueberspringen_kein_Eintrag()
    {
        var pipeline = new GatedRefinePipeline();
        var f = Fixture(pipeline);
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(1);
        var stop = f.Controller.StopAsync();
        await Waits.For(() => pipeline.Refining(1));

        pipeline.Release(1);
        await stop.Within();
        Assert.False(f.Controller.SkipRefine()); // nichts läuft mehr
        Assert.Equal(["[KI] Diktat 1"], f.Sink.Written);
        Assert.Equal(0, SkippedLogs(f));
    }
}
