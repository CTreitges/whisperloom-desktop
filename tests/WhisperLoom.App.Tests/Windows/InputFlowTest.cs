using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Auf dem Avalonia-Thread (seriell mit den UI-Tests, die die globale Sprache umschalten). Ablauf der Tastenkürzel gegen den echten <see cref="DictationController"/> mit Fake-Pipeline und Fake-Adaptern.</summary>
public sealed class InputFlowTest : IDisposable
{
    private readonly InsertRig _rig = new();
    private readonly FakePresenter _ui = new();
    private readonly ControllerFixture _f;
    private readonly InputFlow _flow;
    private readonly List<bool> _overlayKeys = [];

    public InputFlowTest()
    {
        // Keine Sprachumschaltung hier: Loc ist global, die UI-Tests schalten sie auf dem UI-Thread.
        var settings = new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" } };
        Sink = new InsertOutputSink(new CaptureSink(), _rig.Inserter, () => InsertMode.Paste);
        _f = new ControllerFixture(settings, Sink);
        _f.Pipeline.RawText = "Hallo Welt";
        _flow = new InputFlow(_f.Controller, Sink, _ui, () => _rig.Windows.Current, _overlayKeys.Add);
    }

    private InsertOutputSink Sink { get; }

    public void Dispose() => _f.Dispose();

    [AvaloniaFact]
    public async Task Toggle_opens_overlay_and_second_press_sends_into_the_target()
    {
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.True(_ui.Overlay);
        Assert.Equal(DictationState.Recording, _f.Controller.State);
        Assert.Equal(OverlayState.Recording, _flow.Overlay.State);
        Assert.Equal([true], _overlayKeys);
        Assert.Equal((IntPtr)42, _ui.LastTarget);

        // Nutzer klickt woanders hin: eingefügt wird trotzdem ins gemerkte Ziel.
        _rig.Windows.Alive.Add(7);
        _rig.Windows.Current = 7;
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.False(_ui.Overlay);
        Assert.Equal([true, false], _overlayKeys);
        Assert.Equal(DictationState.Idle, _f.Controller.State);
        Assert.Equal(_f.Controller.LastResult, _rig.Keys.Target);
        Assert.Equal((IntPtr)42, _rig.Windows.Current);
        Assert.Equal(0, _rig.Keys.Enters);
    }

    [AvaloniaFact]
    public async Task Enter_sends_and_ctrl_enter_sends_with_enter()
    {
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.OverlaySend);
        Assert.Equal(0, _rig.Keys.Enters);
        Assert.NotEqual("", _rig.Keys.Target);

        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.OverlaySendEnter);
        Assert.Equal(1, _rig.Keys.Enters);
    }

    [AvaloniaFact]
    public async Task Backspace_discards_without_output()
    {
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.OverlayDiscard);
        Assert.False(_ui.Overlay);
        Assert.Equal(DictationState.Idle, _f.Controller.State);
        Assert.Equal(0, _f.Pipeline.Runs);
        Assert.Equal("", _rig.Keys.Target);
        Assert.Equal("alt", _rig.Clipboard.Text);
        // Ein späteres Diktat per CLI geht wieder an die normale Ausgabe.
        _f.Controller.Start();
        await _f.Controller.StopAsync();
        Assert.Empty(_rig.Steps);
    }

    [AvaloniaFact]
    public async Task Overlay_buttons_pause_without_counting_the_pause_then_send()
    {
        await _flow.HandleAsync(HotkeyAction.Toggle);
        _f.Now += TimeSpan.FromSeconds(2);
        _flow.Overlay.PauseResumeCommand.Execute(null);
        Assert.Equal(DictationState.Paused, _f.Controller.State);
        Assert.Equal(OverlayState.Paused, _flow.Overlay.State);
        _f.Now += TimeSpan.FromSeconds(30);
        _flow.Tick();
        Assert.Equal(TimeSpan.FromSeconds(2), _flow.Overlay.Elapsed);

        _flow.Overlay.PauseResumeCommand.Execute(null);
        _f.Now += TimeSpan.FromSeconds(1);
        _flow.Tick();
        Assert.Equal(TimeSpan.FromSeconds(3), _flow.Overlay.Elapsed);

        _flow.Overlay.SendEnterCommand.Execute(null);
        await WaitFor(() => _f.Controller.State == DictationState.Idle && !_ui.Overlay);
        Assert.Equal(1, _rig.Keys.Enters);
        Assert.False(_ui.Overlay);
    }

    [AvaloniaFact]
    public async Task Hold_shows_pill_and_inserts_on_release()
    {
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        Assert.True(_ui.Pill);
        Assert.False(_ui.Overlay);
        Assert.Equal(DictationState.Recording, _f.Controller.State);
        await _flow.HandleAsync(HotkeyAction.HoldEnd);
        Assert.False(_ui.Pill);
        Assert.Equal(_f.Controller.LastResult, _rig.Keys.Target);
        Assert.Equal("alt", _rig.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task Hold_cancel_discards()
    {
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        await _flow.HandleAsync(HotkeyAction.HoldCancel);
        Assert.False(_ui.Pill);
        Assert.Equal(DictationState.Idle, _f.Controller.State);
        Assert.Equal(0, _f.Pipeline.Runs);
    }

    [AvaloniaFact]
    public async Task Hold_end_without_hold_and_send_without_overlay_do_nothing()
    {
        await _flow.HandleAsync(HotkeyAction.HoldEnd);
        await _flow.HandleAsync(HotkeyAction.OverlaySend);
        await _flow.HandleAsync(HotkeyAction.OverlayDiscard);
        Assert.Equal(DictationState.Idle, _f.Controller.State);
        Assert.Empty(_rig.Steps);
    }

    [AvaloniaFact]
    public async Task Hotkeys_are_ignored_while_a_cli_dictation_runs()
    {
        _f.Controller.Start();
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.False(_ui.Pill);
        Assert.False(_ui.Overlay);
    }

    [AvaloniaFact]
    public async Task Stage_combo_switches_stage_and_shows_hint()
    {
        await _flow.HandleAsync(HotkeyAction.Stage);
        Assert.Equal("polish", _f.Controller.Stage.Key());
        var hint = Assert.Single(_ui.Hints);
        Assert.Equal("stage", hint.Kind);
        Assert.Equal(Loc.Instance["Stage_polish"], hint.Message);
    }

    [AvaloniaFact]
    public async Task Pipeline_error_shows_error_state_in_overlay()
    {
        _f.Pipeline.Failure = FakeFailure.SttHttp;
        await _flow.HandleAsync(HotkeyAction.Toggle);
        var send = _flow.HandleAsync(HotkeyAction.Toggle);
        await Task.Delay(50);
        Assert.Equal(OverlayState.Error, _flow.Overlay.State);
        Assert.Equal("Error_Http", _flow.Overlay.ErrorKey);
        Assert.True(_ui.Overlay);
        Assert.Equal("", _rig.Keys.Target);
        _ = send;
    }

    [AvaloniaFact]
    public async Task Hold_error_shows_toast()
    {
        _f.Pipeline.Failure = FakeFailure.SttNetwork;
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        await _flow.HandleAsync(HotkeyAction.HoldEnd);
        Assert.Contains(_ui.Hints, h => h.Kind == "toast" && h.Message == Loc.Instance["Error_Network"]);
    }

    [AvaloniaFact]
    public async Task Gone_target_raises_fallback_toast()
    {
        var toasts = new List<InsertOutcome>();
        _flow.FallbackToast += toasts.Add;
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        _rig.Windows.Alive.Clear();
        await _flow.HandleAsync(HotkeyAction.HoldEnd);
        Assert.Equal([InsertOutcome.TargetGone], toasts);
        Assert.Equal(_f.Controller.LastResult, _rig.Clipboard.Text);
        _flow.ShowFallback(InsertOutcome.TargetGone);
        Assert.Equal(Loc.Instance["Insert_TargetGone"], _ui.Hints[^1].Message);
    }

    [AvaloniaFact]
    public async Task Audio_failure_on_start_shows_toast_and_disarms()
    {
        _f.Recorder.Unavailable = true;
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.False(_ui.Overlay);
        Assert.Contains(_ui.Hints, h => h.Message == Loc.Instance["Error_Audio"]);
        _f.Recorder.Unavailable = false;
        _f.Controller.Start();
        await _f.Controller.StopAsync();
        Assert.Empty(_rig.Steps);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
