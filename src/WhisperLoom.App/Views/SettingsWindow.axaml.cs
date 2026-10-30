using Avalonia.Controls;
using Avalonia.Interactivity;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
