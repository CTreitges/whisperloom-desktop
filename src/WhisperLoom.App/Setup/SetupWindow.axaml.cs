using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WhisperLoom.App.Setup;

/// <summary>
/// Einrichtungs-Assistent: Pegel-Timer, Hilfe zum Schritt, Esc („Später fortsetzen?“) und Kopieren des Linux-Befehls.
/// Beim Schließen endet ein Pegel-Test und der Assistent löst seine Abos (<see cref="SetupViewModel.Dispose"/>).
/// </summary>
public partial class SetupWindow : Window
{
    private readonly DispatcherTimer _micTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };

    public SetupWindow() => InitializeComponent();

    public SetupWindow(SetupViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        Design.SystemTheme.Follow(this, Avalonia.Application.Current?.PlatformSettings);
        _micTimer.Tick += (_, _) => viewModel.Mic?.Tick();
        _micTimer.Start();
        viewModel.PropertyChanged += OnViewModelChanged;
        // Schließen per Fensterknopf: ein laufender Pegel-Test endet, die Aufnahme wird verworfen.
        Closed += (_, _) =>
        {
            _micTimer.Stop();
            viewModel.CloseRequested -= Close;
            viewModel.PropertyChanged -= OnViewModelChanged;
            if (viewModel.Mic is { Running: true } mic)
            {
                mic.Stop();
            }
            viewModel.Dispose();
        };
    }

    private SetupViewModel? ViewModel => DataContext as SetupViewModel;

    /// <summary>
    /// Sprung über die Schrittliste: Der fokussierte „Zurück zu Schritt n“-Knopf verschwindet mit dem Schrittwechsel; der Fokus
    /// geht auf das erste Bedienelement des Schritts, sonst auf den Hauptknopf (Prüfer-Fund PR #42).
    /// </summary>
    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Die Schrittliste wird vor „Current“ neu aufgebaut; der Fokus ist dann schon weg (null) oder steht noch in der Liste.
        var focused = FocusManager?.GetFocusedElement() as Visual;
        if (e.PropertyName != nameof(SetupViewModel.Current) || (focused is not null && !StepList.IsVisualAncestorOf(focused)))
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var first = StepContent.GetVisualDescendants().OfType<InputElement>()
                .FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
            (first ?? (ViewModel is { ShowNext: true } ? NextButton : FinishButton)).Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Hilfe-Abschnitt zum aktuellen Schritt (wie F1).</summary>
    private void OnHelp(object? sender, RoutedEventArgs e) =>
        Views.Help.HelpLauncher.Open(Views.Help.HelpLauncher.TopicFor(this));

    /// <summary>
    /// Esc fragt „Einrichtung später fortsetzen?“ (Fokus auf „Später fortsetzen“), ein zweites Esc nimmt die Frage zurück.
    /// Offene Auswahllisten und Felder behandeln Esc vorher selbst.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || ViewModel is not { } vm)
        {
            return;
        }
        if (vm.LaterPending)
        {
            vm.KeepGoing();
            Dispatcher.UIThread.Post(() => (vm.ShowNext ? NextButton : vm.IsLast ? FinishButton : SkipButton).Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
        else
        {
            vm.AskLater();
            Dispatcher.UIThread.Post(() => LaterButton.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
        e.Handled = true;
    }

    private async void OnCopyCommand(object? sender, RoutedEventArgs e)
    {
        // Doku: Avalonia 12 Avalonia.Base.xml, ClipboardExtensions.SetTextAsync (abgerufen 2026-10-30).
        if (ViewModel is { } vm && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(vm.ToggleCommand);
            Design.CopyFeedback.Show(CopyCommandText, CopyCommandDone);
        }
    }
}
