using Avalonia.Controls;

namespace WhisperLoom.App.Views.Settings;

/// <summary>Einstellungsseite „Anbieter“ (ViewModel: <see cref="ViewModels.Settings.ProvidersPageViewModel"/>).</summary>
public partial class ProvidersPage : UserControl
{
    public ProvidersPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _ = (DataContext as ViewModels.Settings.ProvidersPageViewModel)?.LoadKeysAsync();
    }
}
