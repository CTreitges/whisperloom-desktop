using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Views.Settings;

/// <summary>Einstellungsseite „Erkennung“ (ViewModel: <see cref="RecognitionPageViewModel"/>); Dateiauswahl für eigene Whisper-Dateien.</summary>
public partial class RecognitionPage : UserControl
{
    public RecognitionPage() => InitializeComponent();

    // Doku: https://docs.avaloniaui.net/docs/basics/user-interface/file-dialogs (abgerufen 2026-11-01)
    private async void OnAddFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RecognitionPageViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Instance["Local_AddFile"],
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Loc.Instance["Local_FileType"]) { Patterns = [.. WhisperModels.Extensions.Select(x => "*" + x)] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.AddCustomModel(path);
        }
    }
}
