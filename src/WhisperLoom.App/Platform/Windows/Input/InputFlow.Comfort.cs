using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Services;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Komfort der Tastenkürzel (W2-OUT, Prüfrunde 1): Kürzel während der Verbesserung = sofort ohne KI einfügen, Längen-Takt
/// (Hinweis ab 10 min, bei 12 min hält die Aufnahme an bzw. beim Halten wird gesendet) und „Erneut senden“ im Overlay.
/// </summary>
public sealed partial class InputFlow
{
    /// <summary>So lange bleibt das Overlay nach einem wiederholbaren Fehler offen (Knopf „Erneut senden“).</summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(12);

    /// <summary>Kürzel (Umschalten oder Halten) während „Wird verarbeitet …“: Verbesserung überspringen.</summary>
    private bool SkipIfProcessing(HotkeyAction action)
    {
        if (action is not (HotkeyAction.Toggle or HotkeyAction.HoldStart) || _controller.State != DictationState.Processing)
        {
            return false;
        }
        _controller.SkipRefine();
        return true;
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
        _sink.Arm(_target, null, null, trailingSpace: _controller.Settings.Polish.TrailingSpace);
        var result = await _controller.RetryAsync();
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

    /// <summary>Fehler-Toast; ist „Erneut senden“ möglich, mit Hinweis aufs Tray-Menü.</summary>
    private void ErrorToast(string key)
    {
        var text = Loc.Instance[key];
        _ui.ShowHint("toast", "WhisperLoom", _controller.CanRetry ? Loc.Instance.Format("Error_WithRetry", text) : text, ErrorDuration, _target);
    }
}
