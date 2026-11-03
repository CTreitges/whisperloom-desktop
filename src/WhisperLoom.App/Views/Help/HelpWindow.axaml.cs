using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Views.Help;

/// <summary>
/// Hilfe-Fenster (W2-HELP, UX-3). Tastatur: Tab-Reihenfolge Suche → Seitenleiste → Inhalt, Pfeiltasten in der Seitenleiste,
/// Strg+F = Suche, ↓ aus der Suche in die Treffer, ↑/↓ zwischen den Treffern, Enter öffnet den Treffer, Esc leert die Suche
/// bzw. schließt. Hell/Dunkel folgt dem System.
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow() => InitializeComponent();

    public HelpWindow(HelpViewModel help) : this()
    {
        DataContext = help;
        help.PropertyChanged += OnHelpChanged;
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

    /// <summary>Abschnitt zeigen und den Fokus in die Seitenleiste legen (für „?“, F1, Tray, Aufgaben und Treffer).</summary>
    public void ShowTopic(string topic)
    {
        Help?.Open(topic);
        // Fokus ohne Fokusrahmen (Programm-Sprung, keine Tastatur-Navigation); Pfeiltasten wirken sofort.
        SectionList.ContainerFromItem(Help?.SelectedSection!)?.Focus();
    }

    /// <summary>Knöpfe der Treffer in Reihenfolge (Tastatur, Tests).</summary>
    internal List<Button> ResultButtons => [.. ResultList.GetVisualDescendants().OfType<Button>()];

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
        else if (e.Key is Key.Down or Key.Up && e.KeyModifiers == KeyModifiers.None && Help?.IsSearching == true)
        {
            MoveInResults(e);
        }
    }

    /// <summary>↓ aus der Suche auf den ersten Treffer, ↑/↓ zwischen den Treffern, ↑ vom ersten zurück in die Suche.</summary>
    private void MoveInResults(KeyEventArgs e)
    {
        var buttons = ResultButtons;
        if (buttons.Count == 0)
        {
            return;
        }
        var at = e.Source is Visual source ? buttons.FindIndex(b => b == source || b.IsVisualAncestorOf(source)) : -1;
        if (e.Source == SearchBox && e.Key == Key.Down)
        {
            buttons[0].Focus(NavigationMethod.Directional);
        }
        else if (at >= 0)
        {
            var next = at + (e.Key == Key.Down ? 1 : -1);
            if (next < 0)
            {
                SearchBox.Focus(NavigationMethod.Directional);
            }
            else
            {
                buttons[Math.Min(next, buttons.Count - 1)].Focus(NavigationMethod.Directional);
            }
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    private void OnHelpChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HelpViewModel.SelectedSection) or nameof(HelpViewModel.SearchText))
        {
            ContentScroll.Offset = default;
        }
    }

    private void OnClearSearch(object? sender, RoutedEventArgs e)
    {
        if (Help is { } help)
        {
            help.SearchText = "";
            Dispatcher.UIThread.Post(() => SearchBox.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
    }

    private void OnResultClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HelpSearchResult result })
        {
            ShowTopic(result.Section.Id);
        }
    }

    private void OnTaskClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HelpTaskView task })
        {
            ShowTopic(task.Section.Id);
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
