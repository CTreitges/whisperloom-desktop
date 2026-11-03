using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Views;

/// <summary>
/// Key-Feld. Fokus folgt der Aktion: Eingabe offen → Feld „Neuer API-Key“, gelöscht → „Rückgängig“, sonst die Hauptaktion des
/// neuen Zustands (der gedrückte Knopf ist dann meist ausgeblendet). Abos nur, solange die Ansicht angezeigt wird; verschwindet
/// sie (Seitenwechsel, Fenster zu, Neuladen), verfällt „Rückgängig“ und der gelöschte Key wird vergessen (Prüfer-Befund UX-2/2).
/// </summary>
public partial class KeyFieldView : UserControl
{
    private KeyFieldViewModel? _vm;
    private bool _attached;
    private bool _follow;

    public KeyFieldView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Unsubscribe();
        _follow = false; // eine Aktion am alten Modell darf den Fokus nicht nachträglich herziehen (Nachprüfung B2)
        _vm = DataContext as KeyFieldViewModel;
        Subscribe();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Subscribe();
        _vm?.RefreshTexts();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _vm?.Forget();
        Unsubscribe();
        _attached = false;
        _follow = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe()
    {
        if (_vm is null || !_attached)
        {
            return;
        }
        Unsubscribe();
        _vm.PropertyChanged += OnChanged;
        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    private void Unsubscribe()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnChanged;
        }
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => _vm?.RefreshTexts();

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }
        if (e.PropertyName is nameof(KeyFieldViewModel.IsReplacing) or nameof(KeyFieldViewModel.CanUndo))
        {
            // Folge einer Aktion im Feld. Nur wenn der Fokus im Key-Feld liegt oder mit dem gedrückten (jetzt ausgeblendeten)
            // Knopf verloren ging – nie von einem anderen Element weg.
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            _follow = IsKeyboardFocusWithin || focused is null;
        }
        else if (e.PropertyName != nameof(KeyFieldViewModel.IsSet) || !_follow)
        {
            return; // IsSet allein (Laden beim Öffnen) zieht den Fokus nicht her
        }
        if (_follow)
        {
            // Ziel erst nach dem Ende der Aktion bestimmen: IsSet ändert sich in SaveKey/Undo erst nach IsReplacing bzw. CanUndo.
            Dispatcher.UIThread.Post(() => Target()?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
    }

    private Control? Target()
    {
        if (_vm is null)
        {
            return null;
        }
        Control target = _vm.IsReplacing ? NewKeyBox : _vm.CanUndo ? UndoKeyButton : _vm.IsSet ? CheckButton : EnterButton;
        return target.IsEffectivelyVisible ? target : null;
    }
}
