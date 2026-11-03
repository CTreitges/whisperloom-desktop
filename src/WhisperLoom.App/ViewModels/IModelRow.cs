using CommunityToolkit.Mvvm.Input;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// Zeile der Modell-Liste (zielstruktur.md §7, B30): Optionsfeld + Name + Größe + „Empfohlen“, rechts der Zustand –
/// „Installiert · Löschen“ (fragt inline mit Name und Größe nach), „Herunterladen“ oder Fortschritt mit „Anhalten“.
/// Gemeinsam für Whisper-Modelle (<see cref="LocalModelViewModel"/>) und mitgelieferte Textmodelle (<see cref="TextModelViewModel"/>).
/// </summary>
public interface IModelRow
{
    string Name { get; }

    string Size { get; }

    bool Recommended { get; }

    bool IsSelected { get; set; }

    bool Installed { get; }

    bool CanDownload { get; }

    bool IsDownloading { get; }

    double Progress { get; }

    /// <summary>Fortschritt oder Ergebnis als Satz; leer = nichts zu melden.</summary>
    string Status { get; }

    /// <summary>Zusatz unter dem Namen (z. B. zu wenig Arbeitsspeicher); leer = keiner.</summary>
    string Note { get; }

    bool DeletePending { get; }

    string DeleteQuestion { get; }

    string DownloadName { get; }

    string CancelName { get; }

    string DeleteName { get; }

    IAsyncRelayCommand DownloadCommand { get; }

    IRelayCommand CancelCommand { get; }

    IRelayCommand AskDeleteCommand { get; }

    IRelayCommand KeepCommand { get; }

    IAsyncRelayCommand DeleteCommand { get; }
}
