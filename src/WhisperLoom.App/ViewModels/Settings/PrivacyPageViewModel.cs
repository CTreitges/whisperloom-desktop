namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>Seite „Datenschutz“: Verlauf (Ausschalten fragt nach) und Ablage der Keys.</summary>
public sealed class PrivacyPageViewModel(SettingsViewModel session) : SettingsPageViewModel("privacy", "Privacy", session);
