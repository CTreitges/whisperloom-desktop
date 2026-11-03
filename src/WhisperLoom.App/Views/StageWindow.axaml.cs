using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WhisperLoom.App.Design;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Views;

/// <summary>
/// Bühnenansicht. Tasten: Strg+Plus/Minus Zoom, Strg+0 Standardgröße, Strg+D „Dunkel für Beamer“, F11 Vollbild.
/// Folgt dem Systemthema (UX-3, Entscheidung 19); „Dunkel für Beamer“ macht sie dunkel, Hochkontrast gewinnt.
/// </summary>
public partial class StageWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private Action? _applyTheme;

    public StageWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => (DataContext as StageViewModel)?.Tick();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    /// <param name="theme">Hell/Dunkel/Kontrast des Systems; null = aus den Plattform-Einstellungen (Tests ersetzen es).</param>
    public StageWindow(StageViewModel viewModel, IThemeSource? theme = null) : this()
    {
        DataContext = viewModel;
        theme ??= Application.Current?.PlatformSettings is { } platform ? new PlatformThemeSource(platform) : null;
        if (theme is not null)
        {
            _applyTheme = SystemTheme.Follow(this, theme, () => viewModel.DarkForProjector);
        }
        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnViewModelChanged;
            viewModel.Dispose();
        };
    }

    private StageViewModel? ViewModel => DataContext as StageViewModel;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StageViewModel.DarkForProjector))
        {
            _applyTheme?.Invoke();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.F11:
                WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
                e.Handled = true;
                break;
            case Key.OemPlus or Key.Add when ctrl:
                ViewModel?.ZoomIn();
                e.Handled = true;
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                ViewModel?.ZoomOut();
                e.Handled = true;
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                ViewModel?.ZoomReset();
                e.Handled = true;
                break;
            case Key.D when ctrl:
                ViewModel?.ToggleDark();
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    private void OnCopyRaw(object? sender, RoutedEventArgs e) => Copy(ViewModel?.RawText);

    private void OnCopyResult(object? sender, RoutedEventArgs e) => Copy(ViewModel?.ResultText);

    private async void Copy(string? text)
    {
        // Doku: Avalonia 12 Avalonia.Base.xml, ClipboardExtensions.SetTextAsync (abgerufen 2026-10-30).
        if (!string.IsNullOrEmpty(text) && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
