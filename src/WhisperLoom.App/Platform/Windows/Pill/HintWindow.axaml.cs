using Avalonia.Controls;
using WhisperLoom.App.Platform.Windows.Overlay;

namespace WhisperLoom.App.Platform.Windows.Pill;

/// <summary>Kurzer Hinweis (Stufe gewechselt, ≈ 1,2 s) bzw. Toast (Text liegt in der Zwischenablage), fokusfrei.</summary>
public partial class HintWindow : Window
{
    public HintWindow()
    {
        InitializeComponent();
        NoActivate.Apply(this);
    }

    /// <summary>Art des Hinweises für Status und Smoke: "stage" oder "toast".</summary>
    public string Kind { get; private set; } = "";

    public string Text => Message.Text ?? "";

    public void SetContent(string kind, string caption, string message)
    {
        Kind = kind;
        Caption.Text = caption;
        Caption.IsVisible = caption.Length > 0;
        Message.Text = message;
    }
}
