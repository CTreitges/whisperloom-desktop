namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>Seite „Tastenkürzel“: an/aus und je Aktion eine Zeile aus demselben Template; Linux: Hinweis auf Desktop-Kürzel.</summary>
public sealed class HotkeysPageViewModel(SettingsViewModel session, string toggleCommand) : SettingsPageViewModel("hotkeys", "Hotkeys", "Gray", session)
{
    public HotkeysViewModel Hotkeys => Session.Hotkeys;

    public bool IsLinux => !Hotkeys.Available;

    /// <summary>Befehl für ein Kürzel der Desktop-Umgebung (Linux).</summary>
    public string ToggleCommand { get; } = toggleCommand;

    /// <summary>Befehl „Markierung verarbeiten“ für ein Kürzel der Desktop-Umgebung (Linux, W2-SEL).</summary>
    public string SelectionCommand => ToggleCommand.EndsWith(" toggle", StringComparison.Ordinal)
        ? ToggleCommand[..^" toggle".Length] + " process-selection"
        : "WhisperLoom process-selection";
}
