using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Linux;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Features.Selection;

/// <summary>
/// „Markierung verarbeiten“ unter Linux (W2-SEL, Entscheidung Orchestrator): <c>WhisperLoom process-selection [--stage &lt;id&gt;]</c>
/// über ein Kürzel der Desktop-Umgebung. Liest die Primärauswahl, zeigt den Stufen-Wähler als eigenes Fenster (ohne
/// <c>--stage</c>) und legt das Ergebnis in die Zwischenablage (Hinweis „Kopiert – Strg+V“ kommt von der Ausgabe). Ein Einfügen ins
/// Ziel gibt es unter Linux nicht, deshalb auch kein „Nach Anweisung umschreiben“.
/// </summary>
internal sealed class LinuxSelectionFlow(
    DictationController controller,
    PrimarySelection primary,
    Func<StagePickerViewModel, Task<string?>> pick,
    Action<string> notify)
{
    /// <returns>"done", "error", "empty", "no-tool", "cancelled", "busy", "stage-locked" oder "stage-unavailable".</returns>
    public async Task<string> RunAsync(string? stage)
    {
        if (controller.State != DictationState.Idle)
        {
            return "busy";
        }
        var stages = controller.SelectionStages();
        if (stages.Count == 0)
        {
            notify(Loc.Instance["Rewrite_Locked"]);
            return "stage-locked";
        }
        var read = await primary.ReadAsync(CancellationToken.None);
        if (read.MissingTool is { } tool)
        {
            notify(Loc.Instance.Format("Selection_NoTool", tool));
            return "no-tool";
        }
        if (string.IsNullOrWhiteSpace(read.Text))
        {
            notify(Loc.Instance["Selection_Empty"]);
            return "empty";
        }
        if (stage is null)
        {
            var picker = new StagePickerViewModel(stages.Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()])), controller.Stage.Key());
            stage = await pick(picker);
            if (stage is null)
            {
                return "cancelled";
            }
        }
        string result;
        try
        {
            result = await controller.ProcessSelectionAsync(read.Text, stage);
        }
        catch (ArgumentException)
        {
            notify(Loc.Instance["Selection_StageUnavailable"]);
            return "stage-unavailable";
        }
        if (result == "error")
        {
            notify(Loc.Instance[controller.LastErrorKey ?? "Selection_Failed"]);
        }
        return result;
    }

    /// <summary>Wähler als eigenes kleines Fenster (Linux): Klick, ↑/↓, Tab, 1–9, Enter wählen, Esc bzw. Schließen bricht ab.</summary>
    public static Task<string?> PickInWindowAsync(StagePickerViewModel picker)
    {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new SelectionPickerWindow(noActivate: false) { DataContext = picker, WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen };
        picker.Chosen += key =>
        {
            done.TrySetResult(key);
            window.Close();
        };
        picker.Cancelled += () =>
        {
            done.TrySetResult(null);
            window.Close();
        };
        window.Closed += (_, _) => done.TrySetResult(null);
        window.Show();
        window.Activate();
        return done.Task;
    }
}
