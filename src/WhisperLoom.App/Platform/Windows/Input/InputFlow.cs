using System.ComponentModel;
using System.Text.Json.Nodes;
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
/// Ablauf der Tastenkürzel auf dem UI-Thread: Halten (Pille), Umschalten (Overlay mit Enter/Strg+Enter/Rücktaste und
/// Knöpfen), Stufe (Hinweis). Das Zielfenster wird beim Start gemerkt und an die <see cref="InsertOutputSink"/> gegeben.
/// </summary>
public sealed class InputFlow
{
    public static readonly TimeSpan StageHintDuration = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(4);

    private readonly DictationController _controller;
    private readonly InsertOutputSink _sink;
    private readonly IInputPresenter _ui;
    private readonly Func<IntPtr> _foreground;
    private readonly Action<bool> _overlayKeys;
    private Mode _mode;
    private IntPtr _target;

    private enum Mode
    {
        None,
        Hold,
        Toggle,
    }

    /// <param name="overlayKeys">Schaltet im Hook Enter/Strg+Enter/Rücktaste ans Overlay (true) bzw. wieder frei.</param>
    public InputFlow(DictationController controller, InsertOutputSink sink, IInputPresenter ui, Func<IntPtr> foreground, Action<bool> overlayKeys)
    {
        _controller = controller;
        _sink = sink;
        _ui = ui;
        _foreground = foreground;
        _overlayKeys = overlayKeys;
        Overlay = new OverlayViewModel(PauseResume, Discard, enter => _ = SendAsync(enter));
        controller.PropertyChanged += OnControllerChanged;
        sink.Inserted += outcome =>
        {
            if (outcome.IsFallback())
            {
                FallbackToast?.Invoke(outcome);
            }
        };
    }

    public OverlayViewModel Overlay { get; }

    /// <summary>Text liegt nur in der Zwischenablage (Ziel weg, Admin-Fenster …); kommt vom Ausgabe-Thread.</summary>
    public event Action<InsertOutcome>? FallbackToast;

    public string ModeName => _mode.ToString().ToLowerInvariant();

    public async Task HandleAsync(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.HoldStart when _controller.State == DictationState.Idle:
                if (Begin(Mode.Hold))
                {
                    _ui.ShowPill(_target);
                }
                break;
            case HotkeyAction.HoldEnd when _mode == Mode.Hold:
                // Modus zuerst zurück: das Ende kommt von uns, nicht von außen (OnControllerChanged).
                _mode = Mode.None;
                _ui.SetPill(0, processing: true);
                var result = await _controller.StopAsync();
                _ui.HidePill();
                if (result == "error")
                {
                    Toast(_controller.LastErrorKey ?? "Overlay_Error");
                }
                break;
            case HotkeyAction.HoldCancel when _mode == Mode.Hold:
                _mode = Mode.None;
                _controller.Cancel();
                _sink.Disarm();
                _ui.HidePill();
                break;
            case HotkeyAction.Toggle when _mode == Mode.Toggle && _controller.IsRecording:
                await SendAsync(false);
                break;
            case HotkeyAction.Toggle when _controller.State == DictationState.Idle:
                if (Begin(Mode.Toggle))
                {
                    Overlay.ErrorKey = null;
                    Overlay.State = OverlayState.Recording;
                    Overlay.Stage = StageLabel();
                    Tick();
                    _ui.ShowOverlay(Overlay, _target);
                    _overlayKeys(true);
                }
                break;
            case HotkeyAction.OverlaySend:
                await SendAsync(false);
                break;
            case HotkeyAction.OverlaySendEnter:
                await SendAsync(true);
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
    }

    public JsonObject Status()
    {
        var status = _ui.Status();
        status["mode"] = ModeName;
        status["overlayState"] = Overlay.State.ToString().ToLowerInvariant();
        status["lastInsert"] = _sink.LastOutcome?.ToString();
        return status;
    }

    private bool Begin(Mode mode)
    {
        _target = _foreground();
        _sink.Arm(_target);
        var started = _controller.Start();
        if (started != "recording")
        {
            _sink.Disarm();
            Toast(_controller.LastErrorKey ?? "Overlay_Error");
            return false;
        }
        _mode = mode;
        return true;
    }

    private async Task SendAsync(bool pressEnter)
    {
        if (_mode != Mode.Toggle || !_controller.IsRecording)
        {
            return;
        }
        _mode = Mode.None;
        _overlayKeys(false);
        _sink.SetPressEnter(pressEnter);
        Overlay.State = OverlayState.Processing;
        var result = await _controller.StopAsync();
        if (result == "error")
        {
            Overlay.ErrorKey = _controller.LastErrorKey;
            Overlay.State = OverlayState.Error;
            await Task.Delay(ErrorDuration);
            if (_mode == Mode.None && Overlay.State == OverlayState.Error)
            {
                _ui.HideOverlay();
            }
            return;
        }
        _ui.HideOverlay();
    }

    private void Discard()
    {
        if (_mode != Mode.Toggle || !_controller.IsRecording)
        {
            return;
        }
        _mode = Mode.None;
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
        _overlayKeys(false);
        _sink.Disarm();
        if (mode == Mode.Toggle)
        {
            _ui.HideOverlay();
        }
        else
        {
            _ui.HidePill();
        }
    }

    private void PauseResume()
    {
        if (_mode != Mode.Toggle)
        {
            return;
        }
        if (_controller.State == DictationState.Paused)
        {
            _controller.Resume();
            Overlay.State = OverlayState.Recording;
        }
        else if (_controller.Pause() == "paused")
        {
            Overlay.State = OverlayState.Paused;
        }
    }

    private void NextStage()
    {
        try
        {
            _controller.SetStage("next");
        }
        catch (ArgumentException)
        {
            return;
        }
        Overlay.Stage = StageLabel();
        _ui.ShowHint("stage", Loc.Instance["Stage_Label"], StageLabel(), StageHintDuration, _foreground());
    }

    /// <summary>Toast für einen Fallback (Text liegt in der Zwischenablage).</summary>
    public void ShowFallback(InsertOutcome outcome) =>
        _ui.ShowHint("toast", "WhisperLoom", Loc.Instance["Insert_" + outcome], ToastDuration, _target);

    private void Toast(string key) => _ui.ShowHint("toast", "WhisperLoom", Loc.Instance[key], ToastDuration, _target);

    private string StageLabel() => Loc.Instance["Stage_" + _controller.Stage.Key()];
}
