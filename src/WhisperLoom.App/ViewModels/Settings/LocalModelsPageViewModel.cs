namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „Lokale Modelle“ (W2-LOCAL): Whisper-Modelle laden und löschen (Standard), eigene Whisper-Dateien (.bin/.gguf)
/// hinterlegen, mitgelieferte Textmodelle laden (RAM-Hinweis, Lizenz) und lokale OpenAI-kompatible Server eintragen.
/// Gewählt wird auf den Seiten „Erkennung“ bzw. „Text &amp; Stufen“.
/// </summary>
public sealed class LocalModelsPageViewModel(SettingsViewModel session) : SettingsPageViewModel("localmodels", "LocalModels", "Teal", session)
{
    public LocalViewModel Local => Session.Local;

    /// <summary>Datei aus der Dateiauswahl; Fehler steht danach an der Gruppe (<see cref="LocalViewModel.CustomModelError"/>).</summary>
    public string? AddCustomModel(string path) => Local.AddCustomModel(path);
}
