using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;

namespace WhisperLoom.App.Views.Settings;

/// <summary>Seite „System“; Export und Import öffnen die Dateiauswahl des Systems.</summary>
public partial class SystemPage : UserControl
{
    // Doku: docs.avaloniaui.net/docs/concepts/services/storage-provider (abgerufen 2026-10-31).
    private static readonly FilePickerFileType JsonFiles = new("JSON") { Patterns = ["*.json"], MimeTypes = ["application/json"] };

    public SystemPage() => InitializeComponent();

    private SyncViewModel? Sync => (DataContext as SystemPageViewModel)?.Sync;

    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Sync is not { } sync || Storage is not { } storage)
        {
            return;
        }
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Instance["Sync_Export"],
            SuggestedFileName = SyncViewModel.SuggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [JsonFiles],
        });
        if (file is null)
        {
            return;
        }
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(sync.ExportJson());
            sync.ExportDone(file.Name);
        }
        catch (IOException)
        {
            sync.Status = Loc.Instance["Sync_ExportFailed"];
        }
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Sync is not { } sync || Storage is not { } storage)
        {
            return;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Instance["Sync_Import"],
            AllowMultiple = false,
            FileTypeFilter = [JsonFiles],
        });
        if (files.Count == 0)
        {
            return;
        }
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            await sync.Preview(await reader.ReadToEndAsync());
        }
        catch (IOException)
        {
            sync.Status = Loc.Instance["Sync_Invalid"];
        }
    }
}
