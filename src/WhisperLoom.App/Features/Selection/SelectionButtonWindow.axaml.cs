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
        ActionButton.Click += (_, _) => _click?.Invoke();
    }

    public Button Button => ActionButton;

    public void SetClick(Action click) => _click = click;
}
