using Avalonia;
using Avalonia.Controls;
using WhisperLoom.App.Platform.Windows.Overlay;

namespace WhisperLoom.App.Features.Selection;

/// <summary>Schwebender Knopf „Verarbeiten“ (W2-SEL), fokusfrei (<see cref="NoActivate"/>): ein Klick lässt die Ziel-App vorn.</summary>
public partial class SelectionButtonWindow : Window
{
    private Action? _click;

    public SelectionButtonWindow()
    {
        InitializeComponent();
        NoActivate.Apply(this);
        Design.SystemTheme.Follow(this, Application.Current?.PlatformSettings);
        ActionButton.Click += (_, _) => _click?.Invoke();
    }

    public Button Button => ActionButton;

    public void SetClick(Action click) => _click = click;

    /// <summary>Tastenkürzel „Markierung verarbeiten“ als Tastenkappe am Knopf; null bzw. leer = keine Kappe (Kürzel aus).</summary>
    public void SetShortcut(string? shortcut)
    {
        ShortcutText.Text = shortcut;
        ShortcutCap.IsVisible = !string.IsNullOrEmpty(shortcut);
    }

    /// <summary>Angezeigtes Kürzel (Tests); null = keine Kappe.</summary>
    public string? Shortcut => ShortcutCap.IsVisible ? ShortcutText.Text : null;
}
