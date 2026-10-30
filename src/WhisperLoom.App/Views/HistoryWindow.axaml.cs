using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow() => InitializeComponent();

    public HistoryWindow(HistoryViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text } && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
