using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Overlay mit Stufen-Wähler und Hinweis „Ohne KI eingefügt“ (W2-OUT).</summary>
public sealed class OverlayStageTest : IDisposable
{
    private readonly InsertRig _rig = new();
    private readonly FakePresenter _ui = new();
    private readonly ControllerFixture _f;
    private readonly InputFlow _flow;

    public OverlayStageTest()
    {
        var sink = new InsertOutputSink(new CaptureSink(), _rig.Inserter, () => InsertMode.Paste, () => _f!.Controller.Settings.Output);
        _f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "polish" }, Polish = new PolishSettings { TrailingSpace = false } }, sink);
        _f.Pipeline.RawText = "Hallo Welt";
        _flow = new InputFlow(_f.Controller, sink, _ui, () => _rig.Windows.Current, _ => { });
    }

    public void Dispose() => _f.Dispose();

    [AvaloniaFact]
    public async Task Overlay_bietet_den_Waehler_mit_der_aktuellen_Stufe()
    {
        Loc.Instance.SetLanguage("de");
        await _flow.HandleAsync(HotkeyAction.Toggle);
        var picker = _flow.Overlay.StagePicker!;
        Assert.Equal("polish", picker.Current);
        _flow.Overlay.ToggleStagesCommand.Execute(null);
        Assert.True(_flow.Overlay.IsStagePickerOpen);

        picker.Pick(picker.Options.Single(o => o.Key == "summarize"));
        Assert.Equal(RefineMode.Summarize, _f.Controller.Stage);
        Assert.Equal(Loc.Instance["Stage_summarize"], _flow.Overlay.Stage);
        Assert.False(_flow.Overlay.IsStagePickerOpen);

        await _flow.HandleAsync(HotkeyAction.OverlaySend);
        Assert.StartsWith("[summarize]", _rig.Keys.Target, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Esc_im_Waehler_schliesst_ihn_ohne_Wechsel()
    {
        await _flow.HandleAsync(HotkeyAction.Toggle);
        _flow.Overlay.ToggleStagesCommand.Execute(null);
        _flow.Overlay.StagePicker!.Cancel();
        Assert.False(_flow.Overlay.IsStagePickerOpen);
        Assert.Equal(RefineMode.Polish, _f.Controller.Stage);
        await _flow.HandleAsync(HotkeyAction.OverlayDiscard);
    }

    [AvaloniaFact]
    public async Task Bei_IT_Sperre_kein_Waehler()
    {
        _f.Controller.Policy = new Core.Settings.PolicySettings { Locked = ["refine.stage"] };
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.Null(_flow.Overlay.StagePicker);
        Assert.False(_flow.Overlay.CanPickStage);
        await _flow.HandleAsync(HotkeyAction.OverlayDiscard);
    }

    [AvaloniaFact]
    public async Task KI_gescheitert_Rohtext_eingefuegt_mit_Hinweis()
    {
        Loc.Instance.SetLanguage("de");
        _f.Pipeline.Failure = FakeFailure.Refine;
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.OverlaySend);
        Assert.Equal("Hallo Welt", _rig.Keys.Target);
        Assert.Contains(_ui.Hints, h => h.Message == Loc.Instance["Output_Hint_RawOnly"]);
    }

    [AvaloniaFact]
    public async Task Leerzeichen_danach_nur_beim_Diktat_wie_Android()
    {
        _f.Controller.Settings.Polish.TrailingSpace = true;
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        await _flow.HandleAsync(HotkeyAction.HoldEnd);
        Assert.Equal(_f.Controller.LastResult + " ", _rig.Keys.Target);

        // „Erneut einfügen“ bzw. Tray: ohne angehängtes Leerzeichen (Android hängt es nur beim Diktat an).
        var before = _rig.Keys.Target;
        await _flow.InsertTextAsync(42, "Nochmal");
        Assert.Equal(before + "Nochmal", _rig.Keys.Target);
    }

    [AvaloniaFact]
    public async Task Mit_KI_kein_Hinweis()
    {
        await _flow.HandleAsync(HotkeyAction.HoldStart);
        await _flow.HandleAsync(HotkeyAction.HoldEnd);
        Assert.DoesNotContain(_ui.Hints, h => h.Message == Loc.Instance["Output_Hint_RawOnly"]);
    }
}
