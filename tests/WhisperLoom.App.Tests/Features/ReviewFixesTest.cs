using Avalonia.Headless.XUnit;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Windows;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Pipeline, deren Verbesserung an der Key-Ablage scheitert (wie <c>SettingsAccess.KeysAsync</c> → IOException).</summary>
internal sealed class KeyStoreDownPipeline : IDictationPipeline
{
    public Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct) =>
        Task.FromResult(new DictationResult("Hallo", "de", 1000, new RefinedText(request.Refinement, "Hallo"), "Fake-STT"));

    public Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct) =>
        throw new IOException("Ablage nicht erreichbar");
}

/// <summary>Regressionen der Review-Befunde (WLD-P5b, W/WLD-P5b/review-p5b.md); nur mit APIs, die es vor den Fixes gab.</summary>
public sealed class ReviewFixesTest : IDisposable
{
    private readonly InsertRig _rig = new();
    private readonly FakePresenter _ui = new();
    private readonly CaptureSink _inner = new();
    private readonly ControllerFixture _f;
    private readonly InputFlow _flow;

    public ReviewFixesTest()
    {
        Sink = new InsertOutputSink(_inner, _rig.Inserter, () => InsertMode.Paste);
        _f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" } }, Sink);
        _f.Pipeline.RawText = "Hallo Welt";
        _flow = new InputFlow(_f.Controller, Sink, _ui, () => _rig.Windows.Current, _ => { },
            _ => new WindowInfo("notepad", ""), new SelectionCapture(_rig.Clipboard, _rig.Keys, TimeSpan.Zero));
    }

    private InsertOutputSink Sink { get; }

    public void Dispose() => _f.Dispose();

    [AvaloniaFact]
    public async Task Cancel_during_processing_disarms_the_old_target()
    {
        _f.Pipeline.Delay = TimeSpan.FromMilliseconds(300);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        var send = _flow.HandleAsync(HotkeyAction.OverlaySend);
        await Task.Delay(50);
        _f.Controller.Cancel();
        await send;

        // Ein späteres Ergebnis (Tray, CLI, Neu verarbeiten) geht nicht per Strg+V ins alte Fenster.
        await Sink.WriteAsync("später", CancellationToken.None);
        Assert.Equal(["später"], _inner.Written);
        Assert.Equal("", _rig.Keys.Target);
    }

    [AvaloniaFact]
    public async Task Reprocess_with_an_unreachable_key_store_reports_an_error_instead_of_throwing()
    {
        var history = new FileHistoryStore(Path.Combine(_f.DataDir, "h2"), () => new HistorySettings());
        var controller = new DictationController(new FakeRecorder(), new KeyStoreDownPipeline(), new CaptureSink(), history, _f.Log,
            new WhisperLoomSettings(), _ => { });
        controller.Start();
        await controller.StopAsync();

        Assert.Equal("error", await controller.ReprocessAsync("polish"));
        Assert.Equal("Key_StoreError", controller.LastErrorKey);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    [AvaloniaFact]
    public async Task Reprocess_respects_an_it_locked_stage()
    {
        _f.Controller.Start();
        await _f.Controller.StopAsync();
        _f.Controller.Policy = new PolicySettings { Locked = ["refine.stage"] };
        var count = (await _f.History.ListAsync(CancellationToken.None)).Count;

        Assert.Equal("stage-locked", await _f.Controller.ReprocessAsync("beautify"));
        Assert.Equal(count, (await _f.History.ListAsync(CancellationToken.None)).Count);
    }

    [AvaloniaFact]
    public async Task Turning_history_off_clears_the_tray_list_and_the_last_result()
    {
        _f.Controller.Start();
        await _f.Controller.StopAsync();
        var changed = 0;
        _f.Controller.HistoryChanged += () => changed++;
        var off = SettingsJson.Deserialize(SettingsJson.Serialize(_f.Controller.Settings));
        off.History.Enabled = false;

        await _f.Controller.ApplySettingsAsync(off);

        Assert.Equal(1, changed);
        Assert.Equal("", _f.Controller.LastResult);
        Assert.Null(await _f.Controller.LatestTextAsync());
    }

    [AvaloniaFact]
    public async Task Null_profiles_from_a_foreign_file_do_not_break_the_hotkeys()
    {
        var settings = SettingsJson.Deserialize("""{"refine":{"stage":"off"},"desktop":{"profiles":[null,{"process":null,"stage":null},{"process":"notepad.exe","stage":"polish"}]}}""");
        await _f.Controller.ApplySettingsAsync(settings);

        Assert.Equal(3 - 1, SettingsStore.Desktop(_f.Controller.Settings).Profiles.Count);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.Equal(RefineMode.Polish, (await _f.History.ListAsync(CancellationToken.None))[0].Refinement.Mode);
    }

    [AvaloniaFact]
    public async Task Rewrite_is_refused_when_it_locked_the_stage_to_off()
    {
        _f.Controller.Policy = new PolicySettings { Locked = ["refine.stage"] };
        _f.Controller.Rewriter = new FakeRewriter();
        _rig.Keys.Selection = "Vertraulich";

        await _flow.HandleAsync(HotkeyAction.Rewrite);

        Assert.DoesNotContain("ctrl-c", _rig.Steps);
        Assert.Equal(DictationState.Idle, _f.Controller.State);
        Assert.False(_ui.Overlay);
        Assert.Equal("toast", _ui.Hints[^1].Kind);
    }
}
