using System.ComponentModel;
using System.Text.Json.Nodes;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>Anzeige der fokusfreien Fenster; Avalonia: <c>InputPresenter</c>, Tests: Fake.</summary>
public interface IInputPresenter
{
    void ShowOverlay(OverlayViewModel overlay, IntPtr target);

    void HideOverlay();

    void ShowPill(IntPtr target);

    void SetPill(double level, bool processing);

    void HidePill();

    /// <param name="kind">"stage" (Stufen-Hinweis) oder "toast".</param>
    void ShowHint(string kind, string caption, string message, TimeSpan duration, IntPtr target);

    /// <summary>Sichtbare Fenster für <c>status --json</c> (Smoke): overlay, buttons, pill, hint.</summary>
    JsonObject Status();
}

/// <summary>
/// Ablauf der Tastenkürzel auf dem UI-Thread: Halten (Pille), Umschalten (Overlay mit Enter/Rücktaste, Knöpfen und
/// Stufen-Wähler), Stufe (Hinweis), dazu die Extras (WLD-P5b): erneut einfügen, Markierung umschreiben (Overlay nimmt die
/// Anweisung auf) und App-Profile des Zielfensters. Das Zielfenster wird beim Start gemerkt und an die
/// <see cref="InsertOutputSink"/> gegeben.
/// </summary>
public sealed partial class InputFlow
{
    public static readonly TimeSpan StageHintDuration = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(4);

    private readonly DictationController _controller;
    private readonly InsertOutputSink _sink;
    private readonly IInputPresenter _ui;
    private readonly Func<IntPtr> _foreground;
    private readonly Action<bool> _overlayKeys;
    private readonly Func<IntPtr, WindowInfo>? _describe;
    private readonly SelectionCapture? _capture;
    private Mode _mode;
    private IntPtr _target;
    private string _selection = "";
    private bool _busy;

    private enum Mode
    {
        None,
        Hold,
        Toggle,
        Rewrite,
    }

    /// <param name="overlayKeys">Schaltet im Hook Enter/Rücktaste ans Overlay (true) bzw. wieder frei.</param>
    /// <param name="describe">Prozess und Titel eines Fensters für die App-Profile; null = keine Profile.</param>
    /// <param name="capture">Markierung holen (Umschreiben); null = nicht verfügbar.</param>
    public InputFlow(
        DictationController controller,
        InsertOutputSink sink,
        IInputPresenter ui,
        Func<IntPtr> foreground,
        Action<bool> overlayKeys,
        Func<IntPtr, WindowInfo>? describe = null,
        SelectionCapture? capture = null)
    {
        _controller = controller;
        _sink = sink;
        _ui = ui;
        _foreground = foreground;
        _overlayKeys = overlayKeys;
        _describe = describe;
        _capture = capture;
        Overlay = new OverlayViewModel(PauseResume, Discard, () => _ = SendAsync()) { Retry = () => _ = RetryFromOverlayAsync() };
        controller.PropertyChanged += OnControllerChanged;
        sink.Inserted += outcome =>
        {
            if (outcome.Hint() != Core.Output.OutputHint.None)
            {
                FallbackToast?.Invoke(outcome);
            }
        };
    }

    public OverlayViewModel Overlay { get; }

    /// <summary>
    /// Hinweis nach der Ausgabe (W2-OUT): Text liegt in der Zwischenablage („Kopiert – Strg+V“: kein Eingabefeld, Ziel weg,
    /// Admin-Fenster …) oder Passwortfeld („nicht eingefügt“); kommt vom Ausgabe-Thread.
    /// </summary>
    public event Action<InsertOutcome>? FallbackToast;

    public string ModeName => _mode.ToString().ToLowerInvariant();

    public async Task HandleAsync(HotkeyAction action)
    {
        // Während Erfassung bzw. Einfügen (await) startet kein zweiter Ablauf mit eigener Zwischenablage-Sicherung.
        if (_busy && action is HotkeyAction.HoldStart or HotkeyAction.Toggle or HotkeyAction.Rewrite or HotkeyAction.Reinsert)
        {
            return;
        }
        if (SkipIfProcessing(action))
        {
            return;
        }
        switch (action)
        {
            case HotkeyAction.HoldStart when _controller.State == DictationState.Idle:
                if (Begin(Mode.Hold, _foreground()))
                {
                    _ui.ShowPill(_target);
                }
                break;
            case HotkeyAction.HoldEnd when _mode == Mode.Hold:
                // Modus zuerst zurück: das Ende kommt von uns, nicht von außen (OnControllerChanged).
                _mode = Mode.None;
                _ui.SetPill(0, processing: true);
                var result = await _controller.StopAsync();
                // Nichts eingefügt (Fehler, Abbruch): das nächste Ergebnis (Tray, CLI) darf nicht ins alte Ziel.
                _sink.Disarm();
                _ui.HidePill();
                if (result == "error")
                {
                    ErrorToast(_controller.LastErrorKey ?? "Overlay_Error");
                }
                else if (result == "done")
                {
                    HintIfRawOnly();
                }
                break;
            case HotkeyAction.HoldCancel when _mode == Mode.Hold:
                _mode = Mode.None;
                _controller.Cancel();
                _sink.Disarm();
                _ui.HidePill();
                break;
            case HotkeyAction.Toggle when _mode == Mode.Toggle && _controller.IsRecording:
            case HotkeyAction.Rewrite when _mode == Mode.Rewrite && _controller.IsRecording:
                await SendAsync();
                break;
            case HotkeyAction.Toggle when _controller.State == DictationState.Idle:
                if (Begin(Mode.Toggle, _foreground()))
                {
                    OpenOverlay(StageLabel());
                }
                break;
            case HotkeyAction.Rewrite when _controller.State == DictationState.Idle && _mode == Mode.None:
                await BeginRewriteAsync();
                break;
            case HotkeyAction.Reinsert when _controller.State == DictationState.Idle && _mode == Mode.None:
                await ReinsertAsync();
                break;
            case HotkeyAction.OverlaySend:
                await SendAsync();
                break;
            case HotkeyAction.OverlayDiscard:
                Discard();
                break;
            case HotkeyAction.Stage:
                NextStage();
                break;
        }
    }

    /// <summary>Aktualisiert Dauer und Pegel (Timer der Fenster, ≈ 10 Hz).</summary>
    public void Tick()
    {
        Overlay.Elapsed = _controller.Elapsed;
        Overlay.Level = _controller.Level;
        if (_mode == Mode.Hold && _controller.State == DictationState.Recording)
        {
            _ui.SetPill(_controller.Level, processing: false);
        }
        LengthTick();
    }

    public JsonObject Status()
    {
        var status = _ui.Status();
        status["mode"] = ModeName;
        status["overlayState"] = Overlay.State.ToString().ToLowerInvariant();
        status["overlayStage"] = Overlay.Stage;
        status["profile"] = _controller.ActiveProfile is { } m ? AppProfiles.DisplayName(m.Profile) : null;
        status["lastInsert"] = _sink.LastOutcome?.ToString();
        status["lastFocus"] = _sink.LastFocus?.ToString();
        status["lastInsertWindow"] = _sink.LastReport is { } r
            ? new JsonObject
            {
                ["hwnd"] = (long)r.Window,
                ["target"] = (long)r.Target,
                ["sameTarget"] = r.SameTarget,
                ["process"] = r.Process,
                ["focusClass"] = r.FocusClass,
            }
            : null;
        return status;
    }

    private bool Begin(Mode mode, IntPtr target)
    {
        _target = target;
        // Umschreiben ersetzt nur die Markierung: ohne Profil (Stufe, Ausgabe) und nie mit Enter danach.
        // Sonst Enter aus dem Profil (An/Aus) bzw. bei Erben (null) aus output.pressEnter (W2-OUT).
        var match = mode == Mode.Rewrite ? null : ProfileFor(target);
        _controller.ActiveProfile = match;
        _sink.Arm(target, mode == Mode.Rewrite ? false : match?.Profile.PressEnter, InsertModeOf(match),
            trailingSpace: mode != Mode.Rewrite && _controller.Settings.Polish.TrailingSpace, leadingSpace: mode != Mode.Rewrite);
        var started = _controller.Start();
        if (started != "recording")
        {
            _controller.ActiveProfile = null;
            _sink.Disarm();
            Toast(_controller.LastErrorKey ?? "Overlay_Error");
            return false;
        }
        _mode = mode;
        return true;
    }

    private void OpenOverlay(string stage)
    {
        Overlay.ErrorKey = null;
        Overlay.State = OverlayState.Recording;
        Overlay.Stage = stage;
        Overlay.IsStagePickerOpen = false;
        Overlay.StagePicker = _mode == Mode.Rewrite || _controller.StageLocked ? null : CreateStagePicker();
        Tick();
        _ui.ShowOverlay(Overlay, _target);
        _overlayKeys(true);
    }

    private async Task SendAsync()
    {
        if (_mode is not (Mode.Toggle or Mode.Rewrite) || !_controller.IsRecording)
        {
            return;
        }
        var rewrite = _mode == Mode.Rewrite;
        _mode = Mode.None;
        _overlayKeys(false);
        Overlay.IsStagePickerOpen = false;
        Overlay.State = OverlayState.Processing;
        var result = rewrite ? await _controller.StopAndRewriteAsync(_selection) : await _controller.StopAsync();
        _selection = "";
        // Nichts eingefügt (Fehler, Abbruch): das nächste Ergebnis (Tray, CLI) darf nicht ins alte Ziel gehen.
        _sink.Disarm();
        if (result == "error")
        {
            Overlay.ErrorKey = _controller.LastErrorKey;
            Overlay.CanRetry = _controller.CanRetry;
            Overlay.State = OverlayState.Error;
            await Task.Delay(Overlay.CanRetry ? RetryWindow : ErrorDuration);
            if (_mode == Mode.None && Overlay.State == OverlayState.Error)
            {
                _ui.HideOverlay();
            }
            return;
        }
        _ui.HideOverlay();
        if (!rewrite && result == "done")
        {
            HintIfRawOnly();
        }
    }

    /// <summary>
    /// KI gescheitert, Rohtext eingefügt (Android: Rohtext mit Hinweis, W2-OUT): Hinweis, sofern kein anderer wichtiger ist
    /// (Kopiert, Passwortfeld).
    /// </summary>
    private void HintIfRawOnly()
    {
        if (_controller.LastRefined?.Skipped is not null && _sink.LastOutcome is { } outcome && outcome.Hint() == Core.Output.OutputHint.None)
        {
            Toast("Output_Hint_RawOnly");
        }
    }

    private void Discard()
    {
        if (_mode is not (Mode.Toggle or Mode.Rewrite) || !_controller.IsRecording)
        {
            return;
        }
        _mode = Mode.None;
        _selection = "";
        _overlayKeys(false);
        _controller.Cancel();
        _sink.Disarm();
        _ui.HideOverlay();
    }

    /// <summary>
    /// Aufnahme endet von außen (Tray „Stopp“, CLI/IPC toggle/stop/cancel): Overlay-Tasten sofort freigeben, Overlay bzw.
    /// Pille schließen und nichts ins gemerkte Ziel einfügen — das Ergebnis geht wie bei Tray/CLI in die Zwischenablage.
    /// Eigene Enden setzen <see cref="_mode"/> vorher zurück und landen nicht hier.
    /// </summary>
    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DictationController.State) || _mode == Mode.None
            || _controller.State is DictationState.Recording or DictationState.Paused)
        {
            return;
        }
        var mode = _mode;
        _mode = Mode.None;
        _selection = "";
        _overlayKeys(false);
        _sink.Disarm();
        if (mode == Mode.Hold)
        {
            _ui.HidePill();
        }
        else
        {
            _ui.HideOverlay();
        }
    }

    private void PauseResume()
    {
        if (_mode is not (Mode.Toggle or Mode.Rewrite))
        {
            return;
        }
        if (_controller.State == DictationState.Paused)
        {
            if (_controller.Resume() == "recording")
            {
                Overlay.State = OverlayState.Recording;
            }
        }
        else if (_controller.Pause() == "paused")
        {
            Overlay.State = OverlayState.Paused;
        }
    }

    /// <summary>Stufen-Hotkey: mit aktivem Profil (laufendes Diktat bzw. Vordergrundfenster) dessen Stufe, sonst die Standard-Stufe.</summary>
    private void NextStage()
    {
        var match = _mode == Mode.None ? ProfileFor(_foreground()) : _controller.ActiveProfile;
        string label;
        if (match is not null)
        {
            var stage = _controller.NextProfileStage(match.Index);
            label = $"{AppProfiles.DisplayName(match.Profile)} · {Loc.Instance["Stage_" + stage.Key()]}";
        }
        else
        {
            try
            {
                _controller.SetStage("next");
            }
            catch (ArgumentException)
            {
                return;
            }
            label = StageLabel();
        }
        if (_mode != Mode.Rewrite)
        {
            Overlay.Stage = label;
        }
        _ui.ShowHint("stage", Loc.Instance["Stage_Label"], label, StageHintDuration, _foreground());
    }

    /// <summary>Hinweis nach der Ausgabe: „Kopiert – Strg+V“ bzw. „Passwortfeld – nicht eingefügt“.</summary>
    public void ShowFallback(InsertOutcome outcome) => ShowHint(outcome.Hint());

    /// <summary>Hinweis anzeigen (auch für Tray-Kopieren und Ausgaben ohne Einfügen).</summary>
    public void ShowHint(Core.Output.OutputHint hint)
    {
        if (hint != Core.Output.OutputHint.None)
        {
            _ui.ShowHint("toast", "WhisperLoom", Loc.Instance[Output.OutputHints.Key(hint)], ToastDuration, _target);
        }
    }

    private void Toast(string key) => _ui.ShowHint("toast", "WhisperLoom", Loc.Instance[key], ToastDuration, _target);

    /// <summary>Stufe des laufenden Diktats; mit Profil als „Profil · Stufe“.</summary>
    private string StageLabel()
    {
        var stage = Loc.Instance["Stage_" + _controller.EffectiveStage.Key()];
        return _controller.ActiveProfile is { } m ? $"{AppProfiles.DisplayName(m.Profile)} · {stage}" : stage;
    }
}
