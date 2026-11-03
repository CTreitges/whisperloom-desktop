using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WhisperLoom.App.Design;
using WhisperLoom.App.ViewModels.Updates;

namespace WhisperLoom.App.Views.Updates;

/// <summary>Fenster „Version wählen“ (W3-CHAN): Tray-Menü und Einstellungsseite „Allgemein“.</summary>
public partial class VersionListWindow : Window
{
    public VersionListWindow()
    {
        InitializeComponent();
        SystemTheme.Follow(this, Application.Current?.PlatformSettings);
    }

    public VersionListWindow(VersionListViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>Esc schließt (UX-3 Befund 4); das Fenster, aus dem es geöffnet wurde, bekommt den Fokus zurück.</summary>
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Avalonia.Input.Key.Escape && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            e.Handled = true;
            Close();
        }
    }

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VersionListViewModel vm && (sender as Control)?.DataContext is VersionRow row)
        {
            await vm.InstallAsync(row);
        }
    }
}
