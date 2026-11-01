using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;

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
        DataContext = shell;
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
            if (row.FindAncestorOfType<Expander>() is { } expander)
            {
                expander.IsExpanded = true;
            }
            row.BringIntoView();
            var target = (row.Content as Control)?.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(i => i.Focusable && i.IsEffectivelyEnabled)
                ?? row.Content as InputElement;
            target?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
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
