using Avalonia.Controls;
using Avalonia.Threading;

namespace WhisperLoom.App.Setup;

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
        // Schließen per Fensterknopf: ein laufender Pegel-Test endet, die Aufnahme wird verworfen.
        Closed += (_, _) =>
        {
            _micTimer.Stop();
            viewModel.Join?.Deactivate();
            if (viewModel.Mic is { Running: true } mic)
            {
                mic.Stop();
            }
        };
    }
}
