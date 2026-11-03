using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.History;

namespace WhisperLoom.App.Views;

/// <summary>
/// Verlaufsfenster (W2-OUT). Tastatur ohne Maus: ↑/↓ in der Liste, Enter einfügen, Strg+C kopieren, Entf löschen, Strg+Z
/// rückgängig, F2 bearbeiten (Strg+Enter speichert, Esc verwirft), Strg+F suchen (↓ bzw. Enter springt in die Liste, Esc leert).
/// In Textfeldern behalten die Tasten ihre normale Bedeutung.
/// </summary>
// Doku: docs.avaloniaui.net/docs/concepts/input/routed-events (Tunnel/Bubble, Handled; abgerufen 2026-11-01).
public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        SystemTheme.Follow(this, Application.Current?.PlatformSettings);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) => FocusList();
    }

    public HistoryWindow(HistoryViewModel viewModel) : this()
    {
        DataContext = viewModel;
        // Überschrift, Leerzustand und Ansagen sind berechnet: bei Sprachwechsel neu melden, Abo endet mit dem Fenster.
        void OnLanguage(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => viewModel.RefreshTexts();
        Localization.Loc.Instance.PropertyChanged += OnLanguage;
        Closed += (_, _) => Localization.Loc.Instance.PropertyChanged -= OnLanguage;
    }

    private HistoryViewModel? Vm => DataContext as HistoryViewModel;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        var none = e.KeyModifiers == KeyModifiers.None;

        // Bearbeiten: Strg+Enter speichert, Esc verwirft; sonst gehört die Taste dem Textfeld.
        if (focused is TextBox { Name: "EditBox" })
        {
            if (ctrl && e.Key == Key.Enter)
            {
                _ = SaveAndReturnAsync(vm);
                e.Handled = true;
            }
            else if (none && e.Key == Key.Escape)
            {
                // Geänderter Text: erst fragen, Fokus auf „Weiter bearbeiten“ (zerstörend nie Standard).
                vm.Detail?.CancelEdit();
                if (vm.Detail is { DiscardPending: true })
                {
                    FocusNamed("KeepEditButton");
                }
                else
                {
                    FocusList();
                }
                e.Handled = true;
            }
            return;
        }
        if (ctrl && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.Z && focused is not TextBox)
        {
            _ = vm.UndoCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }
        if (focused == SearchBox)
        {
            if (none && e.Key is Key.Down or Key.Enter)
            {
                FocusList();
                e.Handled = true;
            }
            else if (none && e.Key == Key.Escape && vm.Search.Length > 0)
            {
                vm.Search = "";
                e.Handled = true;
            }
            return;
        }
        // Der Stufen-Wähler bedient seine Tasten selbst (StagePicker).
        if (focused is StagePicker)
        {
            // Nach Wahl bzw. Abbruch zurück in die Liste.
            if (none && e.Key is Key.Enter or Key.Escape or (>= Key.D1 and <= Key.D9) or (>= Key.NumPad1 and <= Key.NumPad9))
            {
                FocusList();
            }
            return;
        }
        // Fokussierte Knöpfe (Bestätigen, Ersetzen, Chips) behalten ihre Tasten: Enter löst den Knopf aus, nicht „einfügen“.
        if (focused is TextBox or Button or Avalonia.Controls.Primitives.ToggleButton)
        {
            return;
        }
        // Markierter Text im Detail: Strg+C kopiert die Markierung wie gewohnt.
        if (focused is SelectableTextBlock { SelectedText.Length: > 0 })
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Enter when none:
                _ = vm.InsertSelectedCommand.ExecuteAsync(null);
                break;
            case Key.C when ctrl:
                _ = vm.CopySelectedCommand.ExecuteAsync(null);
                break;
            case Key.Delete when none:
                _ = DeleteAndKeepFocusAsync(vm);
                break;
            case Key.F2 when none:
                BeginEdit(vm);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void BeginEdit(HistoryViewModel vm)
    {
        vm.EditSelected();
        Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "EditBox")?.Focus(),
            DispatcherPriority.Loaded);
    }

    private async Task SaveAndReturnAsync(HistoryViewModel vm)
    {
        if (vm.Detail is { } detail)
        {
            await detail.SaveEdit();
        }
        FocusList();
    }

    private async Task DeleteAndKeepFocusAsync(HistoryViewModel vm)
    {
        await vm.DeleteSelected();
        FocusList();
    }

    /// <summary>Fokus auf die gewählte Zeile (sonst die Liste), damit ↑/↓ sofort wirken.</summary>
    private void FocusList() => Dispatcher.UIThread.Post(() =>
    {
        if (Entries.SelectedItem is not null && Entries.ContainerFromItem(Entries.SelectedItem) is { } item)
        {
            item.Focus();
        }
        else
        {
            Entries.Focus();
        }
    }, DispatcherPriority.Loaded);

    /// <summary>Fokus auf ein benanntes Element im Detail (liegt in einem Template, darum über den Visual-Baum).</summary>
    private void FocusNamed(string name) => Dispatcher.UIThread.Post(
        () => this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible)?.Focus(),
        DispatcherPriority.Loaded);

    /// <summary>„Abbrechen“ mit Änderungen: Rückfrage erscheint, Fokus auf „Weiter bearbeiten“ (UX-6 N14).</summary>
    private void OnCancelEditClick(object? sender, RoutedEventArgs e) => FocusNamed("KeepEditButton");

    /// <summary>„Weiter bearbeiten“: zurück ins Textfeld.</summary>
    private void OnKeepEditingClick(object? sender, RoutedEventArgs e) => FocusNamed("EditBox");

    /// <summary>Hilfe-Abschnitt „Verlauf“ (wie F1).</summary>
    private void OnHelp(object? sender, RoutedEventArgs e) => Help.HelpLauncher.Open("history");

    /// <summary>„Bearbeiten“ wie F2: Textfeld zeigen und fokussieren.</summary>
    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            BeginEdit(vm);
        }
    }

    /// <summary>„Neu verarbeiten“ öffnet den Wähler und gibt ihm den Fokus (Tastatur sofort nutzbar).</summary>
    private void OnReprocessClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<StagePicker>().FirstOrDefault(p => p.IsVisible)?.Focus(),
            DispatcherPriority.Loaded);
}
