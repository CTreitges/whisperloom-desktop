using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views.Settings;

namespace WhisperLoom.App.Views;

/// <summary>
/// Einstellungsfenster = nur Hülle und Navigation (W2-UI1). Tastatur: Tab-Reihenfolge Suche → Seitenleiste → Seite,
/// Pfeiltasten in der Seitenleiste, Strg+F = Suche, Esc leert die Suche bzw. schließt. Hell/Dunkel folgt dem System.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel session) : this(new SettingsShellViewModel(session))
    {
    }

    public SettingsWindow(SettingsShellViewModel shell) : this()
    {
        // PERF-2: Seiten-Views einmal bauen und wiederverwenden; nach dem Öffnen im Leerlauf vorbauen.
        PageHost.ContentTemplate = Pages = new PageViewCache(this, Staging);
        shell.Pages.CollectionChanged += (_, _) => Pages.Clear();
        Opened += (_, _) => Prebuild(shell);
        DataContext = shell;
        // Seitenwechsel einen Dispatcher-Durchlauf nach der Auswahl (Hintergrund-Priorität): Die Markierung in der
        // Seitenleiste wird zuerst gezeichnet, dann erst die Seite angehängt (Rückmeldung sofort statt nach dem Anhängen).
        PageHost.Content = shell.SelectedPage;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsShellViewModel.SelectedPage))
            {
                Dispatcher.UIThread.Post(() => ShowSelectedPage(shell), DispatcherPriority.Background);
            }
        };
        shell.RevealRequested += Reveal;
        ResultList.SelectionChanged += (_, _) =>
        {
            if (ResultList.SelectedItem is SearchResult result)
            {
                shell.Open(result);
            }
        };
        SystemTheme.Follow(this, Application.Current?.PlatformSettings);
        Closing += (_, _) => CommitFocusedText();
        Closed += (_, _) => shell.Dispose();
    }

    public SettingsShellViewModel? Shell => DataContext as SettingsShellViewModel;

    /// <summary>Für Bestand und Tests: die Arbeitskopie (Session) des Fensters.</summary>
    public SettingsViewModel? Settings => Shell?.Session;

    /// <summary>Gebaute Seiten-Views (PERF-2).</summary>
    internal PageViewCache Pages { get; } = null!;

    /// <summary>
    /// Hängt die gewählte Seite an. W3-PERF N6: Ein Tab direkt nach ↑/↓ kommt vor diesem Durchlauf an und fokussiert noch
    /// die alte Seite; die wird hier abgehängt und der Fokus wäre weg. Dann geht er zurück auf die Seitenleiste.
    /// </summary>
    private void ShowSelectedPage(SettingsShellViewModel shell)
    {
        var focusInOldPage = FocusManager?.GetFocusedElement() is Visual focused && PageHost.IsVisualAncestorOf(focused);
        PageHost.Content = shell.SelectedPage;
        if (focusInOldPage)
        {
            (PageList.ContainerFromItem(shell.SelectedPage!) as InputElement ?? PageList).Focus(NavigationMethod.Tab);
        }
    }

    /// <summary>
    /// Baut die übrigen Seiten nach dem Öffnen im Leerlauf vor (<see cref="PageViewCache.PrepareSteps"/>): ein Schritt je
    /// Dispatcher-Durchlauf mit Hintergrund-Priorität, Eingaben und Rendern gehen vor. Der erste Klick auf eine Seite kostet
    /// dann nur noch das Anhängen wie jeder weitere Wechsel.
    /// </summary>
    private void Prebuild(SettingsShellViewModel shell)
    {
        var pages = shell.Pages.ToList();
        var steps = pages.SelectMany(p => Pages.PrepareSteps(p, () => PageHost.Bounds.Size)).GetEnumerator();
        void Step()
        {
            // Fenster zu oder Seiten neu aufgebaut (Zurücksetzen, Import): Vorbau endet, der Rest entsteht beim Klick.
            if (!IsVisible || !pages.SequenceEqual(shell.Pages) || !steps.MoveNext())
            {
                steps.Dispose();
                return;
            }
            steps.Current();
            Dispatcher.UIThread.Post(Step, DispatcherPriority.Background);
        }
        Dispatcher.UIThread.Post(Step, DispatcherPriority.Background);
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
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (Shell is { IsSearching: true } shell)
            {
                shell.SearchText = "";
                SearchBox.Focus(NavigationMethod.Tab);
            }
            else if (Settings?.Hotkeys.All.Any(r => r.IsRecording) != true)
            {
                Close();
            }
        }
        else if (e.Key == Key.Down && e.Source == SearchBox && Shell?.IsSearching == true && ResultList.ItemCount > 0)
        {
            ResultList.SelectedIndex = 0;
            (ResultList.ContainerFromIndex(0) as InputElement)?.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }

    /// <summary>Zeile mit dem Pfad ins Bild holen und ihr Steuerelement fokussieren (nach dem Seitenwechsel).</summary>
    private void Reveal(string path)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var row = PageHost.GetVisualDescendants().OfType<SettingRow>().FirstOrDefault(r => r.Path == path)
                ?? PageHost.GetLogicalDescendants().OfType<SettingRow>().FirstOrDefault(r => r.Path == path);
            if (row is null)
            {
                return;
            }
            // „Erweitert“ ist zugeklappt nicht im sichtbaren Baum: über den logischen Baum aufklappen, dann Layout abwarten.
            if (row.FindLogicalAncestorOfType<Expander>() is { IsExpanded: false } expander)
            {
                expander.IsExpanded = true;
                UpdateLayout();
            }
            row.BringIntoView();
            var target = (row.Content as Control)?.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(i => i.Focusable && i.IsEffectivelyEnabled)
                ?? row.Content as InputElement;
            target?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Background);
    }

    /// <summary>Textfelder speichern beim Verlassen; beim Schließen per Esc gilt die letzte Eingabe auch.</summary>
    private void CommitFocusedText()
    {
        if (FocusManager?.GetFocusedElement() is TextBox box)
        {
            Avalonia.Data.BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
        }
    }
}
