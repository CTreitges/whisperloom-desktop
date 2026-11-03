using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Views;

/// <summary>
/// Key-Feld. Fokus folgt der Aktion: Eingabe offen → Feld „Neuer API-Key“, gelöscht → „Rückgängig“, Eingabe zu → die
/// Hauptaktion des neuen Zustands (der gedrückte Knopf ist dann meist ausgeblendet).
/// </summary>
public partial class KeyFieldView : UserControl
{
    private KeyFieldViewModel? _vm;

    public KeyFieldView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnChanged;
        }
        _vm = DataContext as KeyFieldViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnChanged;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Nur wenn der Fokus schon im Key-Feld liegt: Laden oder Änderungen von außen ziehen den Fokus nicht her.
        if (_vm is null || !IsKeyboardFocusWithin)
        {
            return;
        }
        Control? target = e.PropertyName switch
        {
            nameof(KeyFieldViewModel.IsReplacing) => _vm.IsReplacing ? NewKeyBox : _vm.IsSet ? CheckButton : EnterButton,
            nameof(KeyFieldViewModel.CanUndo) => _vm.CanUndo ? UndoKeyButton : _vm.IsSet ? CheckButton : null,
            _ => null,
        };
        if (target is not null)
        {
            Dispatcher.UIThread.Post(() => target.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
    }
}
