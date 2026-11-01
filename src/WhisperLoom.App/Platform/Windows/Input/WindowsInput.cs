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
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private TaskCompletionSource<Hotkey?>? _recording;

    private WindowsInput(DictationController controller, InsertOutputSink sink, AppLog log)
    {
        _controller = controller;
        _log = log;
        var windows = new Win32Windows();
        Flow = new InputFlow(controller, sink, new InputPresenter(TargetCenter), windows.Foreground,
            active => _hook!.Update(m => m.OverlayKeysActive = active));
        Flow.FallbackToast += outcome => Dispatcher.UIThread.Post(() => Flow.ShowFallback(outcome));
        // Log nur Metadaten: Ergebnis, Prozess und Fokus-Klasse des Fensters, das die Eingabe bekam — nie Text.
        sink.Inserted += outcome => Dispatcher.UIThread.Post(() => _log.Event(
            "insert",
            ("status", outcome.ToString()),
            ("process", sink.LastReport?.Process),
            ("sameTarget", sink.LastReport?.SameTarget),
            ("focusClass", sink.LastReport?.FocusClass)));
        _hook = new KeyboardHook(_machine, OnHookAction);
        _tick.Tick += (_, _) => Flow.Tick();
        _tick.Start();
        ApplySettings();
        controller.PropertyChanged += OnControllerChanged;
    }

    public InputFlow Flow { get; }

    public bool HookRunning => _hook.IsRunning;

    /// <summary>Echte Ausgabe-Senke für Windows: Einfügen per Tastenkürzel, sonst die innere Senke.</summary>
    public static InsertOutputSink CreateSink(Output.IOutputSink inner, Func<InsertMode> mode) =>
        new(inner, new TextInserter(new Win32TextClipboard(), new Win32KeySender(), new Win32Windows()), mode);

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
    }

    private void OnHookAction(HotkeyAction action, Hotkey? recorded)
    {
        // Hook-Thread: nur weiterreichen, sofort zurückkehren.
        if (action is HotkeyAction.Recorded or HotkeyAction.RecordCancelled)
        {
            _recording?.TrySetResult(recorded);
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
        });
    }

    private static PixelPoint? TargetCenter(IntPtr target) =>
        target != IntPtr.Zero && Win32Native.GetWindowRect(target, out var r) ? ScreenPlacement.Center(r.Left, r.Top, r.Right, r.Bottom) : null;
}
