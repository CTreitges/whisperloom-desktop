using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Extras des Tastenkürzel-Ablaufs (WLD-P5b): erneut einfügen, Tray-Verlauf und App-Profile des Zielfensters. Während einer Erfassung bzw. eines Einfügens (<see cref="_busy"/>) startet kein zweiter Ablauf.
/// </summary>
public sealed partial class InputFlow
{
    /// <summary>
    /// Fügt <paramref name="text"/> in <paramref name="target"/> ein (Ausgabemodus des Profils, ohne Enter); ist das Ziel weg,
    /// liegt der Text in der Zwischenablage (Toast). Hotkey „erneut einfügen“ und Klick im Tray-Verlauf.
    /// </summary>
    /// <returns>Ergebnis des Einfügens (<see cref="InsertOutcome"/>), "busy" oder "error".</returns>
    /// <param name="keepCopy">Text danach auch in der Zwischenablage lassen (Tray-Verlauf, W2-OUT).</param>
    public async Task<string> InsertTextAsync(IntPtr target, string text, bool keepCopy = false)
    {
        if (_busy || _controller.State != DictationState.Idle || _mode != Mode.None)
        {
            return "busy";
        }
        _busy = true;
        try
        {
            return await InsertCoreAsync(target, text, keepCopy);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<string> InsertCoreAsync(IntPtr target, string text, bool keepCopy = false)
    {
        _target = target;
        _sink.Arm(target, pressEnter: false, InsertModeOf(ProfileFor(target)), keepCopy);
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

    /// <summary>
    /// Klick im Tray-Verlauf: ins Ziel des letzten Diktats (Tray-Klick nimmt den Fokus) und immer auch in die Zwischenablage
    /// (W2-OUT: früher ging der Text verloren, wenn dort kein Eingabefeld den Fokus hatte – die alte Zwischenablage wurde
    /// wiederhergestellt). Kein Eingabefeld → nur Zwischenablage mit Hinweis.
    /// </summary>
    public Task<string> InsertRecentAsync(string text) => InsertTextAsync(_sink.LastTarget, text, keepCopy: true);

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

    /// <summary>Stufen-Wähler im Overlay (W2-OUT): Stufen wie im Tray, aktuelle = Stufe des laufenden Diktats.</summary>
    private Design.StagePickerViewModel CreateStagePicker()
    {
        var picker = new Design.StagePickerViewModel(
            RefineModes.SettingsFor(_controller.Settings.Refine.PromptEnabled).Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()])),
            _controller.EffectiveStage.Key());
        picker.Chosen += ChooseStage;
        picker.Cancelled += () => Overlay.IsStagePickerOpen = false;
        return picker;
    }

    /// <summary>Gewählte Stufe gilt wie der Stufen-Hotkey: fürs Profil des Ziels, sonst als Standard-Stufe.</summary>
    internal void ChooseStage(string key)
    {
        if (_controller.ActiveProfile is { } match)
        {
            _controller.SetProfileStage(match.Index, key);
        }
        else
        {
            try
            {
                _controller.SetStage(key);
            }
            catch (ArgumentException)
            {
                return;
            }
        }
        Overlay.Stage = StageLabel();
        if (Overlay.StagePicker is { } picker)
        {
            picker.Current = _controller.EffectiveStage.Key();
        }
        Overlay.IsStagePickerOpen = false;
    }
}
