using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Komfort der Tastenkürzel (W2-OUT, Prüfrunde 1): Längen-Takt (Hinweis ab 10 min, bei 12 min hält die Aufnahme an bzw. beim
/// Halten wird gesendet) und „Erneut senden“ im Overlay. Kürzel während der Verarbeitung = nächste Aufnahme, Doppeldruck bzw.
/// Escape = ohne KI (W3-NEXT, <see cref="DictationController.TryDoublePress"/>).
/// </summary>
public sealed partial class InputFlow
{
    /// <summary>So lange bleibt das Overlay nach einem wiederholbaren Fehler offen (Knopf „Erneut senden“).</summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(12);

    /// <summary>Zählt jedes per Kürzel begonnene Diktat (<see cref="Begin"/>, W3-NEXT): wem gehören Overlay und Pille?</summary>
    private int _run;

    /// <summary>
    /// Kürzel (Umschalten oder Halten) während „Wird verarbeitet …“ (W3-NEXT): sofort die nächste Aufnahme; das laufende Diktat
    /// fügt sein Ergebnis danach ins eigene Ziel ein. Ein Doppeldruck startet nichts (<see cref="Begin"/> → "skipped").
    /// </summary>
    private bool StartNext(HotkeyAction action)
    {
        if (action is not (HotkeyAction.Toggle or HotkeyAction.HoldStart) || _mode != Mode.None || !_controller.CanStartNext)
        {
            return false;
        }
        // Overlay bzw. Pille des laufenden Diktats gehören ab jetzt der neuen Aufnahme.
        _ui.HideOverlay();
        _ui.HidePill();
        var hold = action == HotkeyAction.HoldStart;
        if (Begin(hold ? Mode.Hold : Mode.Toggle, _foreground()))
        {
            if (hold)
            {
                _ui.ShowPill(_target);
            }
            else
            {
                OpenOverlay(StageLabel());
            }
        }
        return true;
    }

    /// <summary>
    /// Hat nach <paramref name="run"/> schon das nächste Diktat per Kürzel begonnen (W3-NEXT; auch wenn es inzwischen selbst
    /// verarbeitet wird), gehören Overlay, Pille und gemerktes Ziel ihm: vom fertigen Diktat bleibt nur ein Fehler-Hinweis.
    /// </summary>
    private bool Superseded(int run, StopOutcome outcome)
    {
        if (run == _run)
        {
            return false;
        }
        if (outcome.Code == "error")
        {
            ErrorToast(outcome.ErrorKey ?? "Overlay_Error");
        }
        return true;
    }

    /// <summary>Hinweis nach dem Halten: Fehler bzw. „ohne KI eingefügt“ – aus dem Ergebnis genau dieses Diktats.</summary>
    private void AfterResult(StopOutcome outcome)
    {
        if (outcome.Code == "error")
        {
            ErrorToast(outcome.ErrorKey ?? "Overlay_Error");
        }
        else if (outcome.Code == "done")
        {
            HintIfRawOnly(outcome.Refined);
        }
    }

    /// <summary>
    /// KI gescheitert, Rohtext eingefügt (Android: Rohtext mit Hinweis, W2-OUT): Hinweis, sofern kein anderer wichtiger ist
    /// (Kopiert, Passwortfeld).
    /// </summary>
    private void HintIfRawOnly(Core.Contracts.RefinedText? refined)
    {
        if (refined?.Skipped is not null && _sink.LastOutcome is { } outcome && outcome.Hint() == Core.Output.OutputHint.None)
        {
            Toast("Output_Hint_RawOnly");
        }
    }

    /// <summary>Längen-Takt aus <see cref="Tick"/>.</summary>
    private void LengthTick()
    {
        if (_mode == Mode.None)
        {
            return;
        }
        switch (_controller.CheckLength())
        {
            case "long":
                Toast("Recording_Long");
                break;
            case "capped" when _mode == Mode.Hold:
                // Beim Halten gibt es kein Weiter: wie Loslassen senden (die spätere echte Freigabe läuft ins Leere).
                Toast("Recording_Capped");
                _ = HandleAsync(HotkeyAction.HoldEnd);
                break;
            case "capped":
                Overlay.State = OverlayState.Paused;
                Toast("Recording_Capped");
                break;
        }
    }

    /// <summary>Knopf „Erneut senden“ im Overlay: gemerktes Audio ins selbe Ziel.</summary>
    private async Task RetryFromOverlayAsync()
    {
        if (!_controller.CanRetry || _mode != Mode.None)
        {
            return;
        }
        Overlay.CanRetry = false;
        Overlay.ErrorKey = null;
        Overlay.State = OverlayState.Processing;
        _sink.Arm(_target, null, null, trailingSpace: _controller.Settings.Polish.TrailingSpace, leadingSpace: true);
        var run = _run;
        var result = await _controller.RetryAsync();
        if (Superseded(run, new StopOutcome(result, _controller.LastErrorKey)))
        {
            return;
        }
        _sink.Disarm();
        if (result == "error")
        {
            Overlay.ErrorKey = _controller.LastErrorKey;
            Overlay.CanRetry = _controller.CanRetry;
            Overlay.State = OverlayState.Error;
            return;
        }
        _ui.HideOverlay();
    }

    /// <summary>Hinweis aus dem Längen-Takt der App (Diktate ohne Kürzel).</summary>
    public void ShowNotice(string key) => Toast(key);

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

    /// <summary>Fehler-Toast; ist „Erneut senden“ möglich, mit Hinweis aufs Tray-Menü.</summary>
    private void ErrorToast(string key)
    {
        var text = Loc.Instance[key];
        _ui.ShowHint("toast", "WhisperLoom", _controller.CanRetry ? Loc.Instance.Format("Error_WithRetry", text) : text, ErrorDuration, _target);
    }

    private void Toast(string key) => _ui.ShowHint("toast", "WhisperLoom", Loc.Instance[key], ToastDuration, _target);

    /// <summary>Stufe des laufenden Diktats; mit Profil als „Profil · Stufe“.</summary>
    private string StageLabel()
    {
        var stage = Loc.Instance["Stage_" + _controller.EffectiveStage.Key()];
        return _controller.ActiveProfile is { } m ? $"{AppProfiles.DisplayName(m.Profile)} · {stage}" : stage;
    }
}
