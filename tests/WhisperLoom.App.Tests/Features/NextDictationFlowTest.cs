using Avalonia.Headless.XUnit;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Windows;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// W3-NEXT über die Windows-Tastenkürzel (<see cref="InputFlow"/> gegen den echten Controller): Taste während der Verbesserung =
/// nächste Aufnahme, das laufende Diktat fügt verbessert in sein eigenes Ziel ein; Doppeldruck bzw. Escape = Rohtext.
/// </summary>
public sealed class NextDictationFlowTest : IDisposable
{
    private readonly GatedRefinePipeline _pipeline = new();
    private readonly InsertRig _rig = new();
    private readonly FakePresenter _ui = new();
    private readonly ControllerFixture _f;
    private readonly InsertOutputSink _sink;
    private readonly InputFlow _flow;

    public NextDictationFlowTest()
    {
        var settings = new WhisperLoomSettings
        {
            Engine = "online",
            Refine = new RefineSettings { Stage = "polish" },
            Polish = new PolishSettings { TrailingSpace = false },
        };
        _sink = new InsertOutputSink(new CaptureSink(), _rig.Inserter, () => InsertMode.Paste, () => _f!.Controller.Settings.Output);
        _f = new ControllerFixture(settings, _sink, pipeline: _pipeline);
        _flow = new InputFlow(_f.Controller, _sink, _ui, () => _rig.Windows.Current, _ => { });
    }

    public void Dispose() => _f.Dispose();

    /// <summary>Diktat 1 per Umschalten aufnehmen und senden, bis seine Verbesserung läuft.</summary>
    private async Task<Task> ToggleDictationAsync(int n)
    {
        await _flow.HandleAsync(HotkeyAction.Toggle);
        _f.Now += TimeSpan.FromSeconds(1);
        var send = _flow.HandleAsync(HotkeyAction.Toggle);
        await Waits.For(() => _pipeline.Refining(n));
        return send;
    }

    // (a) Umschalten

    [AvaloniaFact]
    public async Task Umschalten_waehrend_der_Verbesserung_startet_Aufnahme_2_und_Diktat_1_fuegt_verbessert_ein()
    {
        var send1 = await ToggleDictationAsync(1);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.Equal(DictationState.Recording, _f.Controller.State);
        Assert.True(_ui.Overlay);
        Assert.Equal(OverlayState.Recording, _flow.Overlay.State);
        _flow.Tick();
        Assert.True(_flow.Overlay.PreviousPending); // Hinweis: voriges Diktat wird noch verbessert, Esc = ohne KI

        _pipeline.Release(1);
        await send1.Within();
        Assert.Equal("[KI] Diktat 1", _rig.Keys.Target);
        // Aufnahme 2 läuft weiter, ihr Overlay bleibt offen.
        Assert.Equal(DictationState.Recording, _f.Controller.State);
        Assert.True(_ui.Overlay);
        Assert.Equal(OverlayState.Recording, _flow.Overlay.State);
        _flow.Tick();
        Assert.False(_flow.Overlay.PreviousPending);

        _f.Now += TimeSpan.FromSeconds(1);
        var send2 = _flow.HandleAsync(HotkeyAction.Toggle);
        await Waits.For(() => _pipeline.Refining(2));
        _pipeline.Release(2);
        await send2.Within();
        Assert.Equal("[KI] Diktat 1[KI] Diktat 2", _rig.Keys.Target);
        Assert.False(_ui.Overlay);
        await Waits.For(() => HistoryCount() == 2);
    }

    [AvaloniaFact]
    public async Task Fertiges_Diktat_1_schliesst_das_Overlay_von_Diktat_2_nicht_waehrend_2_noch_verarbeitet_wird()
    {
        var send1 = await ToggleDictationAsync(1);
        var send2 = await ToggleDictationAsync(2); // Aufnahme 2 begonnen und gesendet, wartet auf die Verbesserung
        Assert.Equal(OverlayState.Processing, _flow.Overlay.State);
        _pipeline.Release(1);
        await send1.Within();
        Assert.True(_ui.Overlay); // gehört Diktat 2, das noch verarbeitet wird
        Assert.Equal(OverlayState.Processing, _flow.Overlay.State);
        _pipeline.Release(2);
        await send2.Within();
        Assert.False(_ui.Overlay);
        Assert.Equal("[KI] Diktat 1[KI] Diktat 2", _rig.Keys.Target);
        await Waits.For(() => HistoryCount() == 2);
    }

    // (b) Halten

    [AvaloniaFact]
    public async Task Halten_waehrend_der_Verbesserung_startet_Aufnahme_2_und_Diktat_1_fuegt_verbessert_ein()
    {
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        _f.Now += TimeSpan.FromSeconds(1);
        var end1 = _flow.HandleAsync(HotkeyAction.HoldEnd);
        await Waits.For(() => _pipeline.Refining(1));
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        Assert.Equal(DictationState.Recording, _f.Controller.State);
        Assert.True(_ui.Pill);

        _pipeline.Release(1);
        await end1.Within();
        Assert.Equal("[KI] Diktat 1", _rig.Keys.Target);
        Assert.True(_ui.Pill); // die Pille gehört jetzt Aufnahme 2
        Assert.Equal("hold", _flow.ModeName);

        _f.Now += TimeSpan.FromSeconds(1);
        var end2 = _flow.HandleAsync(HotkeyAction.HoldEnd);
        await Waits.For(() => _pipeline.Refining(2));
        _pipeline.Release(2);
        await end2.Within();
        Assert.Equal("[KI] Diktat 1[KI] Diktat 2", _rig.Keys.Target);
        Assert.False(_ui.Pill);
        await Waits.For(() => HistoryCount() == 2);
    }

    // (c) Doppeldruck

    [AvaloniaFact]
    public async Task Doppeldruck_unter_400_ms_verwirft_Aufnahme_2_und_Diktat_1_fuegt_Rohtext_ein()
    {
        var send1 = await ToggleDictationAsync(1);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.Equal(DictationState.Recording, _f.Controller.State); // der erste Druck hat Aufnahme 2 gestartet
        _f.Now += TimeSpan.FromMilliseconds(200);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await send1.Within();
        Assert.Equal("Diktat 1", _rig.Keys.Target);
        Assert.False(_f.Controller.IsRecording);
        Assert.False(_ui.Overlay);
        Assert.Equal("none", _flow.ModeName);
        Assert.Equal(1, _pipeline.Runs);
        await Waits.For(() => HistoryCount() == 1);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, HistoryCount()); // kein Eintrag für die verworfene Aufnahme
    }

    [AvaloniaFact]
    public async Task Doppeldruck_beim_Halten_verwirft_und_fuegt_Rohtext_ein()
    {
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        _f.Now += TimeSpan.FromSeconds(1);
        var end1 = _flow.HandleAsync(HotkeyAction.HoldEnd);
        await Waits.For(() => _pipeline.Refining(1));
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        Assert.Equal(DictationState.Recording, _f.Controller.State); // der erste Druck hat Aufnahme 2 gestartet
        _f.Now += TimeSpan.FromMilliseconds(100);
        await _flow.HandleAsync(HotkeyAction.HoldEnd);
        _f.Now += TimeSpan.FromMilliseconds(100);
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        await end1.Within();
        Assert.Equal("Diktat 1", _rig.Keys.Target);
        Assert.False(_f.Controller.IsRecording);
        Assert.False(_ui.Pill);
        Assert.Empty(_ui.Hints); // gewollter Doppeldruck: kein Fehler-Hinweis
        Assert.Equal(1, _pipeline.Runs);
    }

    // (e) Escape

    [AvaloniaFact]
    public async Task Escape_waehrend_der_Verbesserung_fuegt_Rohtext_ein_und_Aufnahme_2_laeuft_weiter()
    {
        var send1 = await ToggleDictationAsync(1);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.SkipRefine);
        await send1.Within();
        Assert.Equal("Diktat 1", _rig.Keys.Target);
        Assert.Equal(DictationState.Recording, _f.Controller.State);
        Assert.True(_ui.Overlay);
        await _flow.HandleAsync(HotkeyAction.OverlayDiscard);
    }

    [Fact]
    public void Escape_gehoert_WhisperLoom_nur_waehrend_einer_Verbesserung()
    {
        var refining = false;
        var machine = new HotkeyStateMachine(Hotkey.DefaultHold, Hotkey.DefaultToggle, null) { SkipKeyActive = () => refining };
        Assert.Equal(HookDecision.Pass, machine.OnKey(new KeyInput(Hotkey.VkEscape, true)));
        Assert.Equal(new HookDecision(false), machine.OnKey(new KeyInput(Hotkey.VkEscape, false)));

        refining = true;
        Assert.Equal(new HookDecision(true, HotkeyAction.SkipRefine), machine.OnKey(new KeyInput(Hotkey.VkEscape, true)));
        Assert.Equal(new HookDecision(true), machine.OnKey(new KeyInput(Hotkey.VkEscape, false)));
        // Mit Modifier (z. B. Strg+Umschalt+Esc = Task-Manager) bleibt Escape der App.
        machine.OnKey(new KeyInput(Hotkey.VkLControl, true));
        Assert.Equal(new HookDecision(false), machine.OnKey(new KeyInput(Hotkey.VkEscape, true)));
    }

    [Fact]
    public void Escape_beim_Halten_ueberspringt_und_die_gehaltene_Aufnahme_laeuft_weiter()
    {
        var machine = new HotkeyStateMachine(Hotkey.DefaultHold, Hotkey.DefaultToggle, null) { SkipKeyActive = () => true };
        machine.OnKey(new KeyInput(Hotkey.VkLControl, true));
        Assert.Equal(HotkeyAction.HoldStart, machine.OnKey(new KeyInput(Hotkey.VkLWin, true)).Action);
        Assert.Equal(new HookDecision(true, HotkeyAction.SkipRefine), machine.OnKey(new KeyInput(Hotkey.VkEscape, true)));
        Assert.True(machine.Holding);
        Assert.True(machine.OnKey(new KeyInput(Hotkey.VkEscape, false)).Swallow);
        Assert.Equal(HotkeyAction.HoldEnd, machine.OnKey(new KeyInput(Hotkey.VkLWin, false)).Action);
    }

    // (f) Reihenfolge und eigenes Ziel

    [AvaloniaFact]
    public async Task Jedes_Diktat_geht_in_sein_eigenes_Ziel_und_in_Diktat_Reihenfolge()
    {
        var windows = new List<IntPtr>();
        _sink.Inserted += _ => windows.Add(_sink.LastReport!.Window);
        var send1 = await ToggleDictationAsync(1);
        _rig.Windows.Alive.Add(7);
        _rig.Windows.Current = 7; // Diktat 2 beginnt in einem anderen Fenster
        var send2 = await ToggleDictationAsync(2);
        _pipeline.Release(2);
        await Waits.For(() => _pipeline.Finished(2));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("", _rig.Keys.Target);

        _pipeline.Release(1);
        await Task.WhenAll(send1, send2).Within();
        Assert.Equal("[KI] Diktat 1[KI] Diktat 2", _rig.Keys.Target);
        Assert.Equal([(IntPtr)42, (IntPtr)7], windows);
        await Waits.For(() => HistoryCount() == 2);
    }

    private int HistoryCount()
    {
        var dir = Path.Combine(_f.DataDir, "history");
        return Directory.Exists(dir) && !Directory.EnumerateFiles(dir, "*.tmp").Any() ? Directory.EnumerateFiles(dir, "*.json").Count() : -1;
    }
}
