using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;

namespace WhisperLoom.App.Platform.Windows.Overlay;

/// <summary>Fokusfreies Overlay (<see cref="NoActivate"/>), folgt dem Systemthema (Punkt 3); Inhalt aus <see cref="OverlayViewModel"/>.</summary>
public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        NoActivate.Apply(this);
        Design.SystemTheme.Follow(this, Application.Current?.PlatformSettings);
    }

    public OverlayWindow(OverlayViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>Knöpfe in Bildschirm-Pixeln (Mitte) für Status und Smoke: {"pause": [x, y], …}; nur sichtbare.</summary>
    public JsonObject ButtonCenters()
    {
        var result = new JsonObject();
        foreach (var (name, button) in new[] { ("pause", PauseButton), ("discard", DiscardButton), ("send", SendButton), ("stage", StageButton), ("retry", RetryButton) })
        {
            if (!button.IsVisible)
            {
                continue;
            }
            var center = new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
            if (button.TranslatePoint(center, this) is { } inWindow)
            {
                var screen = this.PointToScreen(inWindow);
                result[name] = new JsonArray(screen.X, screen.Y);
            }
        }
        return result;
    }
}
