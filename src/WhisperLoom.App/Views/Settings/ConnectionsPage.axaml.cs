using Avalonia.Controls;
using WhisperLoom.App.ViewModels.Settings;

namespace WhisperLoom.App.Views.Settings;

/// <summary>Einstellungsseite „Verbindungen“ (ViewModel: <see cref="ConnectionsPageViewModel"/>).</summary>
public partial class ConnectionsPage : UserControl
{
    /// <summary>ViewModel, dessen Keys schon geladen sind (W3-PERF N10).</summary>
    private ConnectionsPageViewModel? _loaded;

    public ConnectionsPage()
    {
        InitializeComponent();
        // Der Seiten-Cache setzt den DataContext bei jedem Zeigen und Verlassen neu: Keys nur einmal je ViewModel lesen.
        // Neue ViewModels (Zurücksetzen, Import) bekommen ohnehin eine neue View (PageViewCache.Clear).
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConnectionsPageViewModel vm && vm != _loaded)
            {
                _loaded = vm;
                _ = vm.LoadKeysAsync();
            }
        };
    }
}
