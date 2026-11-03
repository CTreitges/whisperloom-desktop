using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.ViewModels.Settings;

namespace WhisperLoom.App.Views.Settings;

/// <summary>
/// Einstellungsseite (ViewModel: <see cref="OutputPageViewModel"/>). Setzt nach Aktionen der App-Profile den Fokus (K2: Fokus
/// bleibt beim Verschieben am Profil, springt nach dem Löschen auf „Rückgängig“, nach „Profil hinzufügen“ ins Feld „Name“) und
/// schließt die Liste „Laufende Programme“ mit Esc.
/// </summary>
public partial class OutputPage : UserControl
{
    private ProfilesViewModel? _profiles;

    public OutputPage()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_profiles is not null)
        {
            _profiles.FocusRequested -= OnFocusRequested;
        }
        _profiles = (DataContext as OutputPageViewModel)?.Profiles;
        if (_profiles is not null)
        {
            _profiles.FocusRequested += OnFocusRequested;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_profiles is not null)
        {
            _profiles.FocusRequested -= OnFocusRequested;
            _profiles = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnDataContextChanged(EventArgs.Empty);
    }

    private void OnFocusRequested(ProfileRowViewModel? row, ProfileFocus target) =>
        Dispatcher.UIThread.Post(() => Find(row, target)?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

    private Control? Find(ProfileRowViewModel? row, ProfileFocus target)
    {
        if (row is null)
        {
            return UndoButton.IsEffectivelyVisible ? UndoButton : ProfileAddButton;
        }
        var name = target switch
        {
            ProfileFocus.Name => "NameBox",
            ProfileFocus.MoveUp => "UpButton",
            ProfileFocus.MoveDown => "DownButton",
            _ => "OpenButton",
        };
        return ProfileList.ContainerFromItem(row)?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
    }

    /// <summary>Esc in der offenen Liste „Laufende Programme“: schließen, Fokus zurück auf den Knopf.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Source is not Control source)
        {
            return;
        }
        var panel = source.FindAncestorOfType<StackPanel>(includeSelf: true);
        while (panel is not null && panel.Name != "PickPanel")
        {
            panel = panel.FindAncestorOfType<StackPanel>();
        }
        if (panel?.DataContext is ProfileRowViewModel { PickOpen: true } row)
        {
            row.PickOpen = false;
            Dispatcher.UIThread.Post(() =>
                ProfileList.ContainerFromItem(row)?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "PickButton")?.Focus(NavigationMethod.Tab),
                DispatcherPriority.Loaded);
            e.Handled = true;
        }
    }
}
