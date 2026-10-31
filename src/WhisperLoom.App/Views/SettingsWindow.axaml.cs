using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Views;

public partial class SettingsWindow : Window
{
    // Doku: docs.avaloniaui.net/docs/concepts/services/storage-provider (abgerufen 2026-10-31).
    private static readonly FilePickerFileType JsonFiles = new("JSON") { Patterns = ["*.json"], MimeTypes = ["application/json"] };

    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private SyncViewModel? Sync => (DataContext as SettingsViewModel)?.Sync;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Sync is not { } sync)
        {
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
            sync.Status = Loc.Instance["Sync_Invalid"];
        }
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Sync is not { } sync)
        {
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
            sync.Preview(await reader.ReadToEndAsync());
        }
        catch (IOException)
        {
            sync.Status = Loc.Instance["Sync_Invalid"];
        }
    }
}
