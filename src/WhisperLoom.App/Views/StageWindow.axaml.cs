using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Views;

/// <summary>Bühnenansicht. Tasten: Strg+Plus/Minus Zoom, Strg+0 Standardgröße, F11 Vollbild.</summary>
public partial class StageWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public StageWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => (DataContext as StageViewModel)?.Tick();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    public StageWindow(StageViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private StageViewModel? ViewModel => DataContext as StageViewModel;

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
