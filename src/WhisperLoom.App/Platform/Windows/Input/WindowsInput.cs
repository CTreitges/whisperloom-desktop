using System.ComponentModel;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Threading;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.Pill;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Windows-Bedienung zusammengesetzt: Hook-Thread → UI-Thread → <see cref="InputFlow"/>, dazu Rekorder für die
/// Einstellungen. Kombis und Ausgabemodus folgen den Einstellungen (<see cref="DesktopSettings"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInput : IDisposable
{
    private readonly DictationController _controller;
    private readonly AppLog _log;
    private readonly HotkeyStateMachine _machine = new(null, null, null);
    private readonly KeyboardHook _hook;
    private readonly Features.Selection.MouseHookSwitch _mouse;
    private IntPtr _lastExternal;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private TaskCompletionSource<Hotkey?>? _recording;

    private WindowsInput(DictationController controller, InsertOutputSink sink, AppLog log)
    {
        _controller = controller;
        _log = log;
        var windows = new Win32Windows();
        _mouse = new Features.Selection.MouseHookSwitch(() => new MouseHook(new MouseGestures(), OnMouseAction));
        Flow = new InputFlow(controller, sink, new InputPresenter(TargetCenter), windows.Foreground,
            active => _hook!.Update(m => m.OverlayKeysActive = active),
            w => new Features.Profiles.WindowInfo(windows.ProcessName(w), Win32Windows.Title(w)))
        {
            Selection = new Features.Selection.SelectionWiring
            {
                Reader = new Features.Selection.SelectionReader(
                    new FocusProbe(new UiaFocusSource(), fallback: new MsaaFocusSource()), new UiaSelectionSource(),
                    new Features.Rewrite.SelectionCapture(new Win32TextClipboard(), new Win32KeySender(), windows), windows),
                Ui = new Features.Selection.SelectionPresenter(noActivate: true, TargetCenter),
                PickerKeys = active => _hook!.Update(m => m.PickerKeysActive = active),
                DismissOnKey = active => _hook!.Update(m => m.DismissOnKey = active),
                TransientChanged = rects => _mouse.Update(m => m.Transient = rects),
                Anchor = SelectionNative.Anchor,
                WindowAt = SelectionNative.WindowAt,
                ReplayRightClick = MouseHook.ReplayRightClick,
                Activate = windows.Activate,
                LastExternal = () => _lastExternal,
            },
        };
        Flow.FallbackToast += outcome => Dispatcher.UIThread.Post(() => Flow.ShowFallback(outcome));
        // Log nur Metadaten: Ergebnis, Prozess und Fokus-Klasse des Fensters, das die Eingabe bekam — nie Text.
        sink.Inserted += outcome => Dispatcher.UIThread.Post(() => _log.Event(
            "insert",
            ("status", outcome.ToString()),
            ("process", sink.LastReport?.Process),
            ("sameTarget", sink.LastReport?.SameTarget),
            ("focusClass", sink.LastReport?.FocusClass)));
        // Escape überspringt nur, solange eine Verbesserung läuft (W3-NEXT); abgefragt im Hook-Thread.
        _machine.SkipKeyActive = () => controller.IsRefining;
        _hook = new KeyboardHook(_machine, OnHookAction);
        _tick.Tick += (_, _) =>
        {
            // Tray-Eintrag „Markierung verarbeiten …“: der Klick ins Tray nimmt den Fokus, gemerkt wird das Fenster davor.
            if (SelectionNative.ExternalForeground() is var external && external != IntPtr.Zero)
            {
                _lastExternal = external;
            }
            Flow.Tick();
        };
        _tick.Start();
        ApplySettings();
        controller.PropertyChanged += OnControllerChanged;
    }

    public InputFlow Flow { get; }

    public bool HookRunning => _hook.IsRunning;

    /// <summary>Maus-Hook installiert (nur mit eingeschaltetem Maus-Starter, W2-SEL).</summary>
    public bool MouseHookRunning => _mouse.Installed;

    /// <summary>
    /// Echte Ausgabe-Senke für Windows: Einfügen per Tastenkürzel mit Fokus-Prüfung über UI Automation (W2-OUT), sonst die
    /// innere Senke.
    /// </summary>
    public static InsertOutputSink CreateSink(Output.IOutputSink inner, Func<InsertMode> mode, Func<Core.Contracts.Settings.OutputSettings>? output = null) =>
        new(inner, new TextInserter(new Win32TextClipboard(), new Win32KeySender(), new Win32Windows(), focus: new FocusProbe(new UiaFocusSource(), fallback: new MsaaFocusSource()),
            caret: new CaretContext(new UiaCaretSource())), mode, output);

    /// <summary>Startet Hook und Ablauf; null, wenn Windows den Hook verweigert (Log-Eintrag).</summary>
    public static WindowsInput? Start(DictationController controller, InsertOutputSink sink, AppLog log)
    {
        var input = new WindowsInput(controller, sink, log);
        if (!input._hook.Start())
        {
            log.Event("hotkeys", ("status", "unavailable"));
            input.Dispose();
            return null;
        }
        log.Event("hotkeys", ("status", "on"));
        return input;
    }

    /// <summary>Nimmt die nächste Tastenkombination auf (alle Tasten geschluckt, Esc bricht ab, nach 10 s Schluss).</summary>
    public async Task<Hotkey?> RecordAsync()
    {
        _recording?.TrySetResult(null);
        var tcs = _recording = new TaskCompletionSource<Hotkey?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hook.Update(m => m.StartRecording());
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (done != tcs.Task)
        {
            _hook.Update(m => m.StopRecording());
            tcs.TrySetResult(null);
        }
        return await tcs.Task;
    }

    public void Dispose()
    {
        _controller.PropertyChanged -= OnControllerChanged;
        _tick.Stop();
        _hook.Dispose();
        _mouse.Dispose();
    }

    private void OnHookAction(HotkeyAction action, Hotkey? recorded)
    {
        // Hook-Thread: nur weiterreichen, sofort zurückkehren.
        if (action is HotkeyAction.Recorded or HotkeyAction.RecordCancelled)
        {
            _recording?.TrySetResult(recorded);
            return;
        }
        if (action == HotkeyAction.PickerKey && recorded is { } key)
        {
            Dispatcher.UIThread.Post(() => Flow.PickerKey(key));
            return;
        }
        Dispatcher.UIThread.Post(async () =>
        {
            _log.Event("hotkey", ("action", action.ToString()));
            await Flow.HandleAsync(action);
        });
    }

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DictationController.Settings))
        {
            ApplySettings();
        }
    }

    private void ApplySettings()
    {
        var desktop = SettingsStore.Desktop(_controller.Settings);
        var on = desktop.HotkeysEnabled;
        _hook.Update(m =>
        {
            m.Hold = on ? Hotkey.ParseOr(desktop.HotkeyHold, Hotkey.DefaultHold) : null;
            m.Toggle = on ? Hotkey.ParseOr(desktop.HotkeyToggle, Hotkey.DefaultToggle) : null;
            m.Stage = on ? Hotkey.ParseOr(desktop.HotkeyStage, Hotkey.DefaultStage) : null;
            m.Reinsert = on ? Hotkey.ParseOr(desktop.HotkeyReinsert, Hotkey.DefaultReinsert) : null;
            var combo = _controller.Settings.Selection.HotkeyCombo;
            m.Selection = on && combo.Length > 0 ? Hotkey.ParseOr(combo, Hotkey.DefaultSelection) : null;
        });
        if (!_mouse.Apply(_controller.Settings.Selection))
        {
            _log.Event("mousehook", ("status", "unavailable"));
        }
    }

    /// <summary>Maus-Hook-Thread: nur weiterreichen, sofort zurückkehren.</summary>
    private void OnMouseAction(MouseDecision decision)
    {
        var at = new PixelPoint(decision.X, decision.Y);
        Dispatcher.UIThread.Post(async () =>
        {
            switch (decision.Action)
            {
                case MouseAction.CtrlRightClick:
                    _log.Event("selection.start", ("trigger", "ctrlRightClick"));
                    await Flow.OnCtrlRightClickAsync(at);
                    break;
                case MouseAction.SelectionGesture:
                    await Flow.OnSelectionGestureAsync(at);
                    break;
                case MouseAction.Dismiss:
                    Flow.CancelPicker();
                    Flow.HideButton();
                    break;
            }
        });
    }

    private static PixelPoint? TargetCenter(IntPtr target) =>
        target != IntPtr.Zero && Win32Native.GetWindowRect(target, out var r) ? ScreenPlacement.Center(r.Left, r.Top, r.Right, r.Bottom) : null;
}
