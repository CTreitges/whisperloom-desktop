using Avalonia;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// „Markierung verarbeiten“ (W2-SEL): Markierung erfassen → Stufen-Wähler am Cursor (fokusfrei; Tasten kommen über den Hook) →
/// gewählte Stufe auf die Markierung anwenden und sie im Ziel ersetzen (Fokus-Prüfung der Ausgabe aus W2-OUT; passt das Ziel
/// nicht mehr: Zwischenablage + Hinweis). Der letzte Eintrag „Nach Anweisung umschreiben …“ nimmt wie das frühere Umschreiben
/// eine gesprochene Anweisung auf. Starter: Tastenkombi, schwebender Knopf, Strg+Rechtsklick, Tray, CLI.
/// </summary>
public sealed partial class InputFlow
{
    /// <summary>Schlüssel des Wähler-Eintrags „Nach Anweisung umschreiben …“.</summary>
    public const string InstructionKey = "instruction";

    /// <summary>Nach dieser Zeit ohne Wahl schließt der Wähler von selbst.</summary>
    public static readonly TimeSpan PickerTimeout = TimeSpan.FromSeconds(60);

    /// <summary>So lange bleibt der schwebende Knopf ohne Klick sichtbar.</summary>
    public static readonly TimeSpan ButtonDuration = TimeSpan.FromSeconds(4);

    private StagePickerViewModel? _picker;
    private DateTimeOffset _pickerSince;
    private DateTimeOffset? _buttonSince;

    /// <summary>Teile für „Markierung verarbeiten“; null = nicht verfügbar.</summary>
    public SelectionWiring? Selection { get; init; }

    /// <summary>Der offene Wähler (Status, Tests); null = keiner.</summary>
    public StagePickerViewModel? Picker => _picker;

    /// <summary>Tray-Eintrag „Markierung verarbeiten …“: das Fenster vor dem Tray-Klick wieder nach vorn, dann wie die Kombi.</summary>
    public async Task<string> ProcessSelectionFromTrayAsync()
    {
        var target = Selection?.LastExternal() ?? IntPtr.Zero;
        if (target == IntPtr.Zero || Selection is null || !Selection.Activate(target))
        {
            _target = target;
            Toast("Rewrite_TargetChanged");
            return "no-target";
        }
        return await StartSelectionAsync(SelectionTrigger.Tray, target, null);
    }

    /// <summary>CLI bzw. IPC <c>process-selection [--stage]</c>: Markierung des Vordergrundfensters (Desktop-Kürzel).</summary>
    public Task<string> ProcessSelectionFromCliAsync(string? stage) => StartSelectionAsync(SelectionTrigger.Cli, _foreground(), null, stage);

    /// <summary>
    /// Markierung des Zielfensters erfassen und den Wähler zeigen bzw. mit <paramref name="stage"/> (CLI) sofort verarbeiten.
    /// Ohne Markierung: Hinweis (Strg+Rechtsklick: Rechtsklick nachspielen); Passwortfeld: nichts.
    /// </summary>
    /// <returns>"picker", Ergebnis der Verarbeitung, "empty", "password", "busy", "stage-locked", "unavailable" oder ein Fehler.</returns>
    public async Task<string> StartSelectionAsync(SelectionTrigger trigger, IntPtr target, PixelPoint? at, string? stage = null)
    {
        if (Selection is not { } s)
        {
            return "unavailable";
        }
        HideButton();
        if (_busy || _controller.State != DictationState.Idle || _mode != Mode.None)
        {
            return "busy";
        }
        if (_controller.RewriteBlocked)
        {
            _target = target;
            Toast("Rewrite_Locked");
            return "stage-locked";
        }
        SelectionRead read;
        _busy = true;
        try
        {
            var console = _describe is not null && SelectionCapture.IsConsole(_describe(target).Process);
            read = await s.Reader.ReadAsync(target, console, CancellationToken.None);
        }
        finally
        {
            _busy = false;
        }
        _target = target;
        if (read.Kind != SelectionReadKind.Text)
        {
            return NoSelection(trigger, read);
        }
        if (_controller.State != DictationState.Idle || _mode != Mode.None)
        {
            return "busy";
        }
        _selection = read.Text;
        if (stage is not null)
        {
            return await ChooseSelectionAsync(stage);
        }
        OpenPicker(at ?? s.Anchor(target));
        return "picker";
    }

    /// <summary>Linke Maustaste nach Ziehen bzw. Doppelklick losgelassen: schwebenden Knopf zeigen, wenn wohl Text markiert ist.</summary>
    public async Task OnSelectionGestureAsync(PixelPoint at)
    {
        if (Selection is not { } s || !_controller.Settings.Selection.FloatingButton || _busy || _mode != Mode.None
            || _controller.State != DictationState.Idle)
        {
            return;
        }
        var target = _foreground();
        if (ProfileFor(target) is { Profile.FloatingButton: false } || s.Reader is null)
        {
            return;
        }
        // Erst kurz warten: manche Programme setzen die Markierung erst nach dem Loslassen.
        await Task.Delay(120);
        if (_foreground() != target || _mode != Mode.None || !await s.Reader.MaySelectAsync(CancellationToken.None))
        {
            return;
        }
        s.Ui.ShowButton(new PixelPoint(at.X + 14, at.Y + 14), () => _ = StartSelectionAsync(SelectionTrigger.Button, target, at));
        _buttonSince = s.Now();
        s.DismissOnKey(true);
        s.TransientChanged(s.Ui.Transient());
    }

    /// <summary>Strg+Rechtsklick (geschluckt): mit Markierung den Wähler, sonst den Rechtsklick nachspielen.</summary>
    public Task<string> OnCtrlRightClickAsync(PixelPoint at)
    {
        var target = _foreground();
        // Klick auf ein anderes als das Vordergrundfenster: dessen Markierung ist nicht lesbar – normales Kontextmenü.
        if (Selection is { } s && s.WindowAt(at) is var under && under != IntPtr.Zero && under != target)
        {
            s.ReplayRightClick();
            return Task.FromResult("other-window");
        }
        return StartSelectionAsync(SelectionTrigger.CtrlRightClick, target, at);
    }

    /// <summary>Taste aus dem Hook an den offenen Wähler (↑/↓, Tab, 1–9, Enter, Esc).</summary>
    internal void PickerKey(Hotkey key)
    {
        if (_picker is null)
        {
            return;
        }
        var avaloniaKey = key.Key switch
        {
            0x09 => Avalonia.Input.Key.Tab,
            Hotkey.VkReturn => Avalonia.Input.Key.Enter,
            Hotkey.VkEscape => Avalonia.Input.Key.Escape,
            0x26 => Avalonia.Input.Key.Up,
            0x28 => Avalonia.Input.Key.Down,
            >= '1' and <= '9' => Avalonia.Input.Key.D1 + (key.Key - '1'),
            >= 0x61 and <= 0x69 => Avalonia.Input.Key.NumPad1 + (key.Key - 0x61),
            _ => Avalonia.Input.Key.None,
        };
        var modifiers = key.Modifiers.HasFlag(HotkeyModifiers.Shift) ? Avalonia.Input.KeyModifiers.Shift : Avalonia.Input.KeyModifiers.None;
        StagePicker.Handle(_picker, avaloniaKey, modifiers);
    }

    /// <summary>Wähler schließen ohne Wahl (Esc, andere Taste, Klick daneben, Fenster gewechselt, Zeit um).</summary>
    public void CancelPicker()
    {
        if (_mode != Mode.Picker)
        {
            return;
        }
        ClosePicker();
        _selection = "";
    }

    /// <summary>Knopf weg (Taste, Mausrad, Klick daneben, Zeit um).</summary>
    public void HideButton()
    {
        if (_buttonSince is null || Selection is not { } s)
        {
            return;
        }
        _buttonSince = null;
        s.Ui.HideButton();
        s.DismissOnKey(false);
        s.TransientChanged(s.Ui.Transient());
    }

    /// <summary>Gewählte Stufe auf die Markierung anwenden bzw. „Nach Anweisung umschreiben …“ starten.</summary>
    internal async Task<string> ChooseSelectionAsync(string key)
    {
        ClosePicker();
        var selection = _selection;
        if (selection.Length == 0)
        {
            return "no-selection";
        }
        if (key == InstructionKey)
        {
            if (!Begin(Mode.Rewrite, _target))
            {
                _selection = "";
                return "error";
            }
            OpenOverlay(Loc.Instance["Rewrite_Label"]);
            return "instruction";
        }
        _selection = "";
        // Ersetzt nur die Markierung: ohne Enter, ohne Leerzeichen davor/danach; Ausgabemodus wie das Profil des Ziels.
        _sink.Arm(_target, pressEnter: false, InsertModeOf(ProfileFor(_target)), trailingSpace: false, leadingSpace: false);
        _ui.ShowPill(_target);
        _ui.SetPill(0, processing: true);
        string result;
        try
        {
            result = await _controller.ProcessSelectionAsync(selection, key);
        }
        catch (ArgumentException)
        {
            result = "stage-unavailable";
        }
        _sink.Disarm();
        _ui.HidePill();
        if (result == "error")
        {
            ErrorToast(_controller.LastErrorKey ?? "Selection_Failed");
        }
        else if (result == "stage-unavailable")
        {
            Toast("Selection_StageUnavailable");
        }
        return result;
    }

    /// <summary>
    /// Kürzel bei offenem Wähler bzw. sichtbarem Knopf: „Schließen“ (andere Taste) und ein zweiter Druck der Kombi schließen nur;
    /// jedes andere Kürzel schließt den Wähler und läuft weiter.
    /// </summary>
    /// <returns>true = erledigt.</returns>
    private bool OnSelectionKey(HotkeyAction action)
    {
        if (action == HotkeyAction.Dismiss)
        {
            CancelPicker();
            HideButton();
            return true;
        }
        HideButton();
        if (_mode != Mode.Picker)
        {
            return false;
        }
        CancelPicker();
        return action == HotkeyAction.Selection;
    }

    private System.Text.Json.Nodes.JsonObject? SelectionStatus()
    {
        if (Selection is not { } s)
        {
            return null;
        }
        var status = s.Ui.Status();
        status["pickerOpen"] = _picker is not null;
        status["options"] = _picker is null ? null : new System.Text.Json.Nodes.JsonArray([.. _picker.Options.Select(o => (System.Text.Json.Nodes.JsonNode?)o.Key)]);
        status["highlighted"] = _picker?.HighlightedOption?.Key;
        status["buttonVisible"] = _buttonSince is not null;
        return status;
    }

    /// <summary>Wähler-Takt aus <see cref="Tick"/>: schließen, wenn das Ziel nicht mehr vorn ist oder die Zeit um ist; Knopf nach Ablauf weg.</summary>
    private void SelectionTick()
    {
        if (Selection is not { } s)
        {
            return;
        }
        if (_mode == Mode.Picker && (_foreground() != _target || s.Now() - _pickerSince > PickerTimeout))
        {
            CancelPicker();
        }
        if (_buttonSince is { } since && s.Now() - since > ButtonDuration)
        {
            HideButton();
        }
    }

    private string NoSelection(SelectionTrigger trigger, SelectionRead read)
    {
        _selection = "";
        if (trigger == SelectionTrigger.CtrlRightClick)
        {
            // Kein Fall für den Wähler: Strg+Rechtsklick tut, was er ohne WhisperLoom täte (Kontextmenü).
            Selection?.ReplayRightClick();
            return read.Kind.ToString().ToLowerInvariant();
        }
        switch (read.Kind)
        {
            case SelectionReadKind.Password:
                return "password"; // Passwortfeld: nichts, auch kein Hinweis, der den Inhalt verrät
            case SelectionReadKind.Empty:
                Toast("Selection_Empty");
                return "empty";
            default:
                Toast(read.ToastKey ?? "Selection_Empty");
                return "error";
        }
    }

    private void OpenPicker(PixelPoint? at)
    {
        var s = Selection!;
        var stages = _controller.SelectionStages().Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()])).ToList();
        stages.Add((InstructionKey, Loc.Instance["Selection_Instruction"]));
        var current = ProfileFor(_target) is { } match ? _controller.ProfileStage(match.Profile) : _controller.Stage;
        var picker = new StagePickerViewModel(stages, current.Key());
        picker.Chosen += key => _ = ChooseSelectionAsync(key);
        picker.Cancelled += CancelPicker;
        _picker = picker;
        _mode = Mode.Picker;
        _pickerSince = s.Now();
        s.PickerKeys(true);
        s.Ui.ShowPicker(picker, at, _target);
        s.TransientChanged(s.Ui.Transient());
    }

    private void ClosePicker()
    {
        if (_mode == Mode.Picker)
        {
            _mode = Mode.None;
        }
        _picker = null;
        if (Selection is { } s)
        {
            s.PickerKeys(false);
            s.Ui.HidePicker();
            s.TransientChanged(s.Ui.Transient());
        }
    }
}
