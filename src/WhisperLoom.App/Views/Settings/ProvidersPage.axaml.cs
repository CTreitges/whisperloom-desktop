using Avalonia.Controls;
using WhisperLoom.App.ViewModels.Settings;

namespace WhisperLoom.App.Views.Settings;

/// <summary>Einstellungsseite „Anbieter“ (ViewModel: <see cref="ProvidersPageViewModel"/>).</summary>
public partial class ProvidersPage : UserControl
{
    /// <summary>ViewModel, dessen Keys schon geladen sind (W3-PERF N10).</summary>
    private ProvidersPageViewModel? _loaded;

    public ProvidersPage()
    {
        InitializeComponent();
        // Der Seiten-Cache setzt den DataContext bei jedem Zeigen und Verlassen neu: Keys nur einmal je ViewModel lesen.
        // Neue ViewModels (Zurücksetzen, Import) bekommen ohnehin eine neue View (PageViewCache.Clear).
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ProvidersPageViewModel vm && vm != _loaded)
            {
                _loaded = vm;
                _ = vm.LoadKeysAsync();
            }
        };
    }
}
