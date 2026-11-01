using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Extras des Tastenkürzel-Ablaufs (WLD-P5b): erneut einfügen, Tray-Verlauf, Markierung umschreiben und App-Profile des
/// Zielfensters. Während einer Erfassung bzw. eines Einfügens (<see cref="_busy"/>) startet kein zweiter Ablauf.
/// </summary>
public sealed partial class InputFlow
{
    /// <summary>
    /// Fügt <paramref name="text"/> in <paramref name="target"/> ein (Ausgabemodus des Profils, ohne Enter); ist das Ziel weg,
    /// liegt der Text in der Zwischenablage (Toast). Hotkey „erneut einfügen“ und Klick im Tray-Verlauf.
    /// </summary>
    /// <returns>Ergebnis des Einfügens (<see cref="InsertOutcome"/>), "busy" oder "error".</returns>
    public async Task<string> InsertTextAsync(IntPtr target, string text)
    {
        if (_busy || _controller.State != DictationState.Idle || _mode != Mode.None)
        {
            return "busy";
        }
        _busy = true;
        try
        {
            return await InsertCoreAsync(target, text);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<string> InsertCoreAsync(IntPtr target, string text)
    {
        _target = target;
        _sink.Arm(target, pressEnter: false, InsertModeOf(ProfileFor(target)));
        try
        {
            await _sink.WriteAsync(text, CancellationToken.None);
        }
        catch (IOException)
        {
            _sink.Disarm();
            Toast("Error_Output");
            return "error";
        }
        return _sink.LastOutcome?.ToString() ?? "done";
    }

    /// <summary>Klick im Tray-Verlauf: ins Ziel des letzten Diktats (Tray-Klick nimmt den Fokus), sonst Zwischenablage.</summary>
    public Task<string> InsertRecentAsync(string text) => InsertTextAsync(_sink.LastTarget, text);

    /// <summary>Markierung holen (Zwischenablage gesichert und wiederhergestellt), dann die Anweisung im Overlay aufnehmen.</summary>
    private async Task BeginRewriteAsync()
    {
        if (_capture is null)
        {
            return;
        }
        var target = _foreground();
        if (_controller.RewriteBlocked)
        {
            _target = target;
            Toast("Rewrite_Locked");
            return;
        }
        string? selection;
        _busy = true;
        try
        {
            selection = await _capture.CaptureAsync(_describe is not null && SelectionCapture.IsConsole(_describe(target).Process), CancellationToken.None);
        }
        catch (IOException)
        {
            selection = null;
        }
        finally
        {
            _busy = false;
        }
        if (selection is null)
        {
            _target = target;
            Toast("Rewrite_NothingSelected");
            return;
        }
        if (_controller.State != DictationState.Idle || _mode != Mode.None || !Begin(Mode.Rewrite, target))
        {
            return;
        }
        _selection = selection;
        OpenOverlay(Loc.Instance["Rewrite_Label"]);
    }

    /// <summary>Hotkey bzw. CLI „erneut einfügen“ ins Vordergrundfenster.</summary>
    /// <returns>Ergebnis des Einfügens, "no-entry" (Toast) oder "busy".</returns>
    public async Task<string> ReinsertAsync()
    {
        if (_busy || _controller.State != DictationState.Idle || _mode != Mode.None)
        {
            return "busy";
        }
        var target = _foreground();
        _busy = true;
        try
        {
            var text = await _controller.LatestTextAsync();
            if (string.IsNullOrEmpty(text))
            {
                _target = target;
                Toast("Reinsert_Nothing");
                return "no-entry";
            }
            return await InsertCoreAsync(target, text);
        }
        finally
        {
            _busy = false;
        }
    }

    private ProfileMatch? ProfileFor(IntPtr window) =>
        _describe is null || window == IntPtr.Zero
            ? null
            : AppProfiles.Resolve(SettingsStore.Desktop(_controller.Settings).Profiles, _describe(window));

    private static InsertMode? InsertModeOf(ProfileMatch? match) =>
        match is { Profile.InsertMode.Length: > 0 } ? InsertModes.FromKey(match.Profile.InsertMode) : null;
}
