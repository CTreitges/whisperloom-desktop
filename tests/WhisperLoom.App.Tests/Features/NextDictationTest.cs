using Avalonia.Headless.XUnit;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// W3-NEXT, OS-neutraler Teil im Controller: Start während der Verbesserung = nächste Aufnahme, Ausgaben in Diktat-Reihenfolge,
/// Doppeldruck &lt; 400 ms = Aufnahme verwerfen und Verbesserung überspringen; Linux über das IPC-Verb <c>toggle</c>.
/// </summary>
public sealed class NextDictationTest
{
    private static WhisperLoomSettings Polish() => new()
    {
        Engine = "online",
        Refine = new RefineSettings { Stage = "polish" },
        Polish = new PolishSettings { TrailingSpace = false },
    };

    private readonly GatedRefinePipeline _pipeline = new();

    private ControllerFixture Fixture() => new(Polish(), pipeline: _pipeline);

    /// <summary>Diktat <paramref name="n"/> aufnehmen und stoppen, bis seine Verbesserung läuft.</summary>
    private async Task<Task<string>> RecordAndStopAsync(ControllerFixture f, int n)
    {
        Assert.Equal("recording", f.Controller.Start());
        f.Now += TimeSpan.FromSeconds(1);
        var stop = f.Controller.StopAsync();
        await Waits.For(() => _pipeline.Refining(n));
        return stop;
    }

    // (d) zweiter Druck nach mehr als 400 ms: normal stoppen und verarbeiten

    [Fact]
    public async Task Zweiter_Druck_nach_400_ms_stoppt_die_naechste_Aufnahme_normal()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        Assert.Equal("recording", await f.Controller.ToggleAsync());
        f.Now += DictationController.DoublePressWindow + TimeSpan.FromMilliseconds(100);
        var stop2 = f.Controller.ToggleAsync();
        await Waits.For(() => _pipeline.Refining(2));
        _pipeline.Release(1);
        _pipeline.Release(2);
        Assert.Equal("done", await stop1.Within());
        Assert.Equal("done", await stop2.Within());
        Assert.Equal(["[KI] Diktat 1", "[KI] Diktat 2"], f.Sink.Written);
        Assert.Equal(2, (await f.History.ListAsync(CancellationToken.None)).Count);
        Assert.Equal(DictationState.Idle, f.Controller.State);
    }

    // (f) Reihenfolge

    [Fact]
    public async Task Ausgaben_kommen_in_Diktat_Reihenfolge_auch_bei_drei_Diktaten()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        var stop2 = await RecordAndStopAsync(f, 2);
        var stop3 = await RecordAndStopAsync(f, 3);
        Assert.Equal(3, f.Controller.Pending);

        _pipeline.Release(3);
        await Waits.For(() => _pipeline.Finished(3));
        _pipeline.Release(2);
        await Waits.For(() => _pipeline.Finished(2));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(f.Sink.Written); // 2 und 3 sind fertig, warten aber auf Diktat 1

        _pipeline.Release(1);
        await Task.WhenAll(stop1, stop2, stop3).Within();
        Assert.Equal(["[KI] Diktat 1", "[KI] Diktat 2", "[KI] Diktat 3"], f.Sink.Written);
        var history = await f.History.ListAsync(CancellationToken.None);
        Assert.Equal(["Diktat 1", "Diktat 2", "Diktat 3"], history.Select(h => h.Raw).Order());
        Assert.Equal("[KI] Diktat 3", f.Controller.LastResult);
        Assert.Equal(DictationState.Idle, f.Controller.State);
    }

    [Fact]
    public async Task Abbruch_ohne_Aufnahme_beendet_alle_wartenden_Diktate_ohne_Ausgabe()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        var stop2 = await RecordAndStopAsync(f, 2);
        _pipeline.Release(2);
        await Waits.For(() => _pipeline.Finished(2));
        Assert.Equal("cancelled", f.Controller.Cancel()); // bricht beide ab, keine Ausgabe
        await Task.WhenAll(stop1, stop2).Within();
        Assert.Empty(f.Sink.Written);
        Assert.Equal(DictationState.Idle, f.Controller.State);
        Assert.Equal(0, f.Controller.Pending);
    }

    // (c) Doppeldruck auf Controller-Ebene

    [Fact]
    public async Task Doppeldruck_unter_400_ms_verwirft_die_Aufnahme_und_ueberspringt()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        Assert.Equal("recording", await f.Controller.ToggleAsync());
        f.Now += TimeSpan.FromMilliseconds(150);
        Assert.Equal("skipped", await f.Controller.ToggleAsync());
        Assert.False(f.Controller.IsRecording);
        Assert.Equal("done", await stop1.Within());
        Assert.Equal(["Diktat 1"], f.Sink.Written);
        Assert.Equal(1, _pipeline.Runs); // kein Audio der zweiten Aufnahme gesendet
        Assert.Single(await f.History.ListAsync(CancellationToken.None));
        Assert.Equal(DictationState.Idle, f.Controller.State);
    }

    [Fact]
    public async Task Antippen_beim_Halten_dann_zweiter_Druck_ist_ein_Doppeldruck()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        Assert.Equal("recording", f.Controller.Start()); // Halten gedrückt
        f.Now += TimeSpan.FromMilliseconds(120);
        Assert.Equal("tap", await f.Controller.StopAsync()); // losgelassen: ein Antippen, nichts gesendet
        Assert.Equal(DictationState.Processing, f.Controller.State);
        f.Now += TimeSpan.FromMilliseconds(150);
        Assert.Equal("skipped", f.Controller.Start()); // zweiter Druck
        Assert.Equal("done", await stop1.Within());
        Assert.Equal(["Diktat 1"], f.Sink.Written);
        Assert.Equal(1, _pipeline.Runs);
    }

    [Fact]
    public async Task Einzelner_Druck_ueberspringt_nie_und_Verwerfen_laesst_Diktat_1_weiterlaufen()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        Assert.Equal("recording", f.Controller.Start());
        Assert.Equal("cancelled", f.Controller.Cancel()); // nur die neue Aufnahme
        Assert.Equal(DictationState.Processing, f.Controller.State);
        f.Now += TimeSpan.FromSeconds(1);
        Assert.False(f.Controller.TryDoublePress());
        _pipeline.Release(1);
        Assert.Equal("done", await stop1.Within());
        Assert.Equal(["[KI] Diktat 1"], f.Sink.Written);
    }

    [Fact]
    public async Task Escape_Weg_ueberspringt_nur_die_Verbesserung_die_Aufnahme_laeuft_weiter()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        Assert.Equal("recording", f.Controller.Start());
        Assert.True(f.Controller.IsRefining);
        Assert.True(f.Controller.SkipRefine());
        Assert.Equal("done", await stop1.Within());
        Assert.Equal(["Diktat 1"], f.Sink.Written);
        Assert.Equal(DictationState.Recording, f.Controller.State);
        Assert.False(f.Controller.IsRefining);
        Assert.False(f.Controller.SkipRefine());
        f.Controller.Cancel();
    }

    [Fact]
    public async Task Umschreiben_und_Neu_verarbeiten_bleiben_exklusiv()
    {
        using var f = Fixture();
        Assert.False(f.Controller.CanStartNext);
        var stop1 = await RecordAndStopAsync(f, 1);
        Assert.True(f.Controller.CanStartNext);
        Assert.Equal("busy", await f.Controller.ReprocessAsync("off"));
        _pipeline.Release(1);
        await stop1.Within();
    }

    // Review-Befunde: Ergebnis und Retry-Audio gehören genau einem Diktat

    [Fact]
    public async Task Stopp_liefert_Fehler_und_Ergebnis_genau_dieses_Diktats()
    {
        using var f = Fixture();
        Assert.Equal("recording", f.Controller.Start());
        f.Now += TimeSpan.FromSeconds(1);
        var stop1 = f.Controller.StopDictationAsync();
        await Waits.For(() => _pipeline.Refining(1));
        Assert.Equal("recording", f.Controller.Start());
        f.Now += TimeSpan.FromSeconds(1);
        var stop2 = f.Controller.StopDictationAsync();
        await Waits.For(() => _pipeline.Refining(2));
        _pipeline.FailWithNetwork(2); // Diktat 2 scheitert zuerst, Diktat 1 gelingt danach
        await Waits.For(() => _pipeline.Finished(2));
        _pipeline.Release(1);
        var first = await stop1.Within();
        var second = await stop2.Within();
        Assert.Equal(("done", null, "[KI] Diktat 1"), (first.Code, first.ErrorKey, first.Refined?.Text));
        Assert.Equal(("error", "Error_Network", null), (second.Code, second.ErrorKey, second.Refined?.Text));
    }

    [Fact]
    public async Task Fehler_eines_aelteren_Diktats_haelt_kein_Audio_fuer_Erneut_senden()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        Assert.Equal("recording", f.Controller.Start());
        _pipeline.FailWithNetwork(1); // scheitert wiederholbar, während Aufnahme 2 läuft
        Assert.Equal("error", await stop1.Within());
        f.Now += TimeSpan.FromSeconds(1);
        var stop2 = f.Controller.StopAsync();
        await Waits.For(() => _pipeline.Refining(2));
        _pipeline.Release(2);
        Assert.Equal("done", await stop2.Within());
        // Sonst schickte „Erneut senden“ das Audio von Diktat 1 ins Ziel von Diktat 2.
        Assert.False(f.Controller.CanRetry);
        Assert.Equal("nothing", await f.Controller.RetryAsync());
    }

    [Fact]
    public async Task Wiederholbarer_Fehler_des_letzten_Diktats_haelt_sein_Audio_weiter()
    {
        using var f = Fixture();
        var stop1 = await RecordAndStopAsync(f, 1);
        _pipeline.FailWithNetwork(1);
        Assert.Equal("error", await stop1.Within());
        Assert.True(f.Controller.CanRetry);
    }

    // (g) Linux: zwei toggle-Aufrufe über das IPC-Verb

    [AvaloniaFact]
    public async Task Linux_toggle_zweimal_unter_400_ms_waehrend_der_Verbesserung_ueberspringt()
    {
        var data = Path.Combine(Path.GetTempPath(), "wl-next-" + Guid.NewGuid().ToString("N"));
        var output = new CaptureSink();
        try
        {
            var host = new AppHost(new AppOptions(data), new MemorySecretStore(), _ => _pipeline, new FakeRecorder(), output);
            Assert.Equal("recording", (await host.ExecuteAsync("toggle", [])).Message);
            var stop = host.ExecuteAsync("toggle", []);
            await Waits.For(() => _pipeline.Refining(1));
            Assert.Equal("recording", (await host.ExecuteAsync("toggle", [])).Message);
            Assert.Equal("skipped", (await host.ExecuteAsync("toggle", [])).Message);
            Assert.Equal("done", (await stop.Within()).Message);
            Assert.Equal(["Diktat 1"], output.Written);
            Assert.Equal(1, _pipeline.Runs);
            Assert.Equal(DictationState.Idle, host.Controller.State);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Linux_toggle_waehrend_der_Verbesserung_startet_die_naechste_Aufnahme()
    {
        var data = Path.Combine(Path.GetTempPath(), "wl-next-" + Guid.NewGuid().ToString("N"));
        var output = new CaptureSink();
        try
        {
            var host = new AppHost(new AppOptions(data), new MemorySecretStore(), _ => _pipeline, new FakeRecorder(), output);
            await host.ExecuteAsync("toggle", []);
            var stop = host.ExecuteAsync("toggle", []);
            await Waits.For(() => _pipeline.Refining(1));
            Assert.Equal("recording", (await host.ExecuteAsync("toggle", [])).Message);
            _pipeline.Release(1);
            Assert.Equal("done", (await stop.Within()).Message);
            Assert.Equal(["[KI] Diktat 1"], output.Written);
            Assert.Equal(DictationState.Recording, host.Controller.State);
            Assert.Equal("cancelled", (await host.ExecuteAsync("cancel", [])).Message);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }
}
