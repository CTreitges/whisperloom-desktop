using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Views.Help;

/// <summary>
/// Hilfe-Fenster (W2-HELP). Tastatur: Tab-Reihenfolge Suche → Seitenleiste → Inhalt, Pfeiltasten in der Seitenleiste,
/// Strg+F = Suche, ↓ aus der Suche in die Treffer, Enter bzw. Auswahl öffnet den Treffer, Esc leert die Suche bzw. schließt.
/// Hell/Dunkel folgt dem System.
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow() => InitializeComponent();

    public HelpWindow(HelpViewModel help) : this()
    {
        DataContext = help;
        help.PropertyChanged += OnHelpChanged;
        ResultList.SelectionChanged += (_, _) =>
        {
            if (ResultList.SelectedItem is HelpSearchResult result)
            {
                help.SelectedSection = result.Section;
            }
        };
        SystemTheme.Follow(this, Application.Current?.PlatformSettings);
        Closed += (_, _) =>
        {
            help.PropertyChanged -= OnHelpChanged;
            help.Dispose();
        };
    }

    public HelpViewModel? Help => DataContext as HelpViewModel;

    /// <summary>Öffnet einen Link; Standard = Browser über den Launcher des Fensters. Tests ersetzen das.</summary>
    public Func<Uri, Task>? OpenUri { get; set; }

    /// <summary>Abschnitt zeigen und den Fokus in die Seitenleiste legen (für „?“, F1 und Tray).</summary>
    public void ShowTopic(string topic)
    {
        Help?.Open(topic);
        // Fokus ohne Fokusrahmen (Programm-Sprung, keine Tastatur-Navigation); Pfeiltasten wirken sofort.
        SectionList.ContainerFromItem(Help?.SelectedSection!)?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus(NavigationMethod.Tab);
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (Help is { IsSearching: true } help)
            {
                help.SearchText = "";
                SearchBox.Focus(NavigationMethod.Tab);
            }
            else
            {
                Close();
            }
        }
        else if (e.Key == Key.Down && e.Source == SearchBox && Help?.IsSearching == true && ResultList.ItemCount > 0)
        {
            ResultList.SelectedIndex = 0;
            (ResultList.ContainerFromIndex(0) as InputElement)?.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }

    private void OnHelpChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HelpViewModel.SelectedSection))
        {
            ContentScroll.Offset = default;
        }
    }

    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HelpSectionViewModel section })
        {
            ShowTopic(section.Id);
        }
    }

    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HelpLinkView link })
        {
            var uri = new Uri(link.Url);
            await (OpenUri is { } open ? open(uri) : Launcher.LaunchUriAsync(uri));
        }
    }
}
