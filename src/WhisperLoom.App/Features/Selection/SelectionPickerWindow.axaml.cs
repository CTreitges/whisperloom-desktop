using Avalonia.Controls;
using WhisperLoom.App.Design;
using WhisperLoom.App.Platform.Windows.Overlay;

namespace WhisperLoom.App.Features.Selection;

/// <summary>
/// Fenster des Stufen-Wählers am Cursor. <paramref name="noActivate"/> (Windows): nimmt der Ziel-App nie den Fokus – Klicks
/// wählen trotzdem (MA_NOACTIVATE), Tasten leitet der Tastatur-Hook weiter. Linux: normales Fenster, der Wähler hält selbst den
/// Tastaturfokus (<see cref="StagePicker"/>).
/// </summary>
public partial class SelectionPickerWindow : Window
{
    public SelectionPickerWindow()
    {
        InitializeComponent();
        SystemTheme.Follow(this, Avalonia.Application.Current?.PlatformSettings);
    }

    public SelectionPickerWindow(bool noActivate) : this()
    {
        if (noActivate)
        {
            NoActivate.Apply(this);
        }
        else
        {
            Topmost = true;
            CanResize = false;
            ShowInTaskbar = false;
            WindowDecorations = WindowDecorations.None;
            Opened += (_, _) => PickerView.Focus();
        }
    }

    public StagePicker View => PickerView;
}
