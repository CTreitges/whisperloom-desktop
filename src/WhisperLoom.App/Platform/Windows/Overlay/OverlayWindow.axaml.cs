using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace WhisperLoom.App.Platform.Windows.Overlay;

/// <summary>Fokusfreies Overlay (<see cref="NoActivate"/>); Inhalt aus <see cref="OverlayViewModel"/>.</summary>
public partial class OverlayWindow : Window
{
    public static readonly IValueConverter IsProcessing =
        new FuncValueConverter<OverlayState, bool>(state => state == OverlayState.Processing);

    public OverlayWindow()
    {
        InitializeComponent();
        NoActivate.Apply(this);
    }

    public OverlayWindow(OverlayViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>Knöpfe in Bildschirm-Pixeln (Mitte) für Status und Smoke: {"pause": [x, y], …}.</summary>
    public JsonObject ButtonCenters()
    {
        var result = new JsonObject();
        foreach (var (name, button) in new[] { ("pause", PauseButton), ("discard", DiscardButton), ("send", SendButton), ("sendEnter", SendEnterButton) })
        {
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
