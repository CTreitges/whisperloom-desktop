namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>Seite „Ausgabe“: Einfügen-Art und App-Profile (Windows); unter Linux nur der Hinweis auf die Zwischenablage.</summary>
public sealed class OutputPageViewModel(SettingsViewModel session) : SettingsPageViewModel("output", "Output", "Green", session)
{
    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsLinux => !IsWindows;

    public Features.Profiles.ProfilesViewModel Profiles => Session.Profiles;
}
