using Avalonia.Headless.XUnit;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Windows;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Tastatur, deren Warten auf losgelassene Modifier erst auf ein Signal endet; davor läuft optional <see cref="OnWait"/>.</summary>
internal sealed class GateKeys(IKeySender inner) : IKeySender
{
    private TaskCompletionSource _gate = CompletedGate();

    /// <summary>Läuft beim Warten (z. B. ein anderes Fenster drängt sich vor).</summary>
    public Action? OnWait { get; set; }

    public void Close() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Open() => _gate.TrySetResult();

    public async Task WaitForModifiersReleasedAsync(TimeSpan max, CancellationToken ct)
    {
        OnWait?.Invoke();
        await inner.WaitForModifiersReleasedAsync(max, ct);
        await _gate.Task;
    }

    public bool SendCtrlV() => inner.SendCtrlV();

    public bool SendCopy(bool ctrlInsert) => inner.SendCopy(ctrlInsert);

    public bool SendEnter() => inner.SendEnter();

    public bool Type(string text) => inner.Type(text);

    private static TaskCompletionSource CompletedGate()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}

/// <summary>Regressionen der zweiten Prüfrunde (WLD-P5b, W/WLD-P5b/review.md R1–R3, D1).</summary>
public sealed class ReviewRound2Test : IDisposable
{
    private readonly InsertRig _rig = new();
    private readonly FakePresenter _ui = new();
    private readonly ControllerFixture _f;
    private readonly GateKeys _keys;
    private readonly InputFlow _flow;

    public ReviewRound2Test()
    {
        var sink = new InsertOutputSink(new CaptureSink(), _rig.Inserter, () => InsertMode.Paste);
        _f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" } }, sink);
        _f.Controller.Rewriter = new FakeRewriter();
        _keys = new GateKeys(_rig.Keys);
        _rig.Windows.Alive.Add(7);
        _flow = new InputFlow(_f.Controller, sink, _ui, () => _rig.Windows.Current, _ => { },
            _ => new WindowInfo("notepad", "")) { Selection = SelectionTesting.Wiring(_rig, keys: _keys) };
    }

    public void Dispose() => _f.Dispose();

    private static HistoryEntry Entry(int n) => new($"{1761825600000 + n}-{n:x8}", DateTimeOffset.UnixEpoch.AddMinutes(n), "de", 1000,
        "alt roh " + n, "Altes Diktat " + n, new Refinement(RefineMode.Polish), "Fake-STT");

    private static async Task Prefill(string dir, int count)
    {
        var store = new FileHistoryStore(dir, () => new HistorySettings());
        for (var n = 1; n <= count; n++)
        {
            await store.AddAsync(Entry(n), CancellationToken.None);
        }
    }

    // R1 — Verlauf aus: nichts aus alten Dateien.
    [AvaloniaFact]
    public async Task History_off_shows_nothing_from_old_files()
    {
        await Prefill(Path.Combine(_f.DataDir, "history"), 3);
        _f.Controller.Settings.History.Enabled = false;

        Assert.Empty(await _f.Controller.RecentAsync());
        Assert.Null(await _f.Controller.LatestTextAsync());
        Assert.Equal("no-entry", await _f.Controller.ReprocessAsync("polish"));
        Assert.Equal("no-entry", await _f.Controller.ReprocessAsync("polish", Entry(2).Id));
    }

    // R1 — Start mit Verlauf aus (z. B. Richtlinie bei geschlossener App): alte Diktate weg, Status/Tray/CLI leer.
    [AvaloniaFact]
    public async Task Start_with_history_off_clears_old_files()
    {
        var data = Path.Combine(_f.DataDir, "host");
        await Prefill(Path.Combine(data, "history"), 4);
        File.WriteAllText(Path.Combine(data, "settings.json"), """{"history":{"enabled":false}}""");

        var host = new AppHost(new AppOptions(data), new MemorySecretStore(), _ => new FakeDictationPipeline(), new FakeRecorder(), new CaptureSink());

        var dir = Path.Combine(data, "history");
        Assert.True(!Directory.Exists(dir) || !Directory.EnumerateFiles(dir).Any());
        Assert.Empty(await host.History.ListAsync(CancellationToken.None));
        var status = await host.StatusAsync();
        Assert.Empty(status["recent"]!.AsArray());
        Assert.Equal(0, status["history"]!["count"]!.GetValue<int>());
        Assert.Equal("no-entry", (await host.ExecuteAsync("recent-insert", ["0"])).Message);
        Assert.Equal("no-entry", (await host.ExecuteAsync("reinsert", [])).Message);
    }

    // R2 — Busy-Sperre: während der Erfassung startet kein zweiter Ablauf mit eigener Zwischenablage-Sicherung.
    [AvaloniaFact]
    public async Task Second_rewrite_or_reinsert_during_the_capture_is_ignored()
    {
        await _f.History.AddAsync(Entry(1), CancellationToken.None);
        _rig.Keys.Selection = "Markiert";
        _keys.Close();

        var first = _flow.HandleAsync(HotkeyAction.Selection);
        var second = _flow.HandleAsync(HotkeyAction.Selection);
        var reinsert = _flow.HandleAsync(HotkeyAction.Reinsert);
        await Task.Delay(50);
        _keys.Open();
        await Task.WhenAll(first, second, reinsert);

        Assert.Equal(1, _rig.Steps.Count(s => s == "save"));
        Assert.Equal(1, _rig.Steps.Count(s => s is "ctrl-c" or "ctrl-insert"));
        Assert.DoesNotContain("ctrl-v", _rig.Steps);
        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.NotNull(_flow.Picker);
    }

    // R3 — anderes Fenster vorn nach dem Warten auf die Modifier: keine Kopier-Kombi, Toast.
    [AvaloniaFact]
    public async Task No_copy_keys_when_another_window_came_to_the_front()
    {
        _rig.Keys.Selection = "Markiert";
        _keys.OnWait = () => _rig.Windows.Current = 7;

        await _flow.HandleAsync(HotkeyAction.Selection);

        Assert.DoesNotContain("ctrl-c", _rig.Steps);
        Assert.DoesNotContain("ctrl-insert", _rig.Steps);
        Assert.Equal(Loc.Instance["Rewrite_TargetChanged"], _ui.Hints[^1].Message);
        Assert.False(_ui.Overlay);
    }

    // R3 — Admin-Fenster: keine Kopier-Kombi, Toast.
    [AvaloniaFact]
    public async Task No_copy_keys_into_a_protected_window()
    {
        _rig.Keys.Selection = "Markiert";
        _rig.Windows.Elevated.Add(42);

        await _flow.HandleAsync(HotkeyAction.Selection);

        Assert.DoesNotContain("ctrl-c", _rig.Steps);
        Assert.Equal(Loc.Instance["Rewrite_TargetProtected"], _ui.Hints[^1].Message);
    }

    // D1 — Wiederherstellen scheitert zweimal: eigener Toast, kein Umschreiben; einmal: zweiter Versuch reicht.
    [AvaloniaFact]
    public async Task Failed_restore_gets_its_own_toast_and_one_retry()
    {
        _rig.Keys.Selection = "Markiert";
        _rig.Clipboard.FailRestores = 2;
        await _flow.HandleAsync(HotkeyAction.Selection);
        Assert.Equal(Loc.Instance["Rewrite_RestoreFailed"], _ui.Hints[^1].Message);
        Assert.Null(_flow.Picker);

        _rig.Clipboard.Text = "alt";
        _rig.Clipboard.FailRestores = 1;
        await _flow.HandleAsync(HotkeyAction.Selection);
        Assert.NotNull(_flow.Picker);
        Assert.Equal("alt", _rig.Clipboard.Text);
    }

    // D3 — Titel-Regex mit katastrophalem Backtracking: Zeitlimit, Profil greift nicht.
    [Fact]
    public void Regex_timeout_means_no_match()
    {
        var profile = new AppProfile { Process = "notepad.exe", TitlePattern = "^(a+)+$" };
        Assert.False(AppProfiles.Matches(profile, new WindowInfo("notepad", new string('a', 40) + "!")));
    }
}
