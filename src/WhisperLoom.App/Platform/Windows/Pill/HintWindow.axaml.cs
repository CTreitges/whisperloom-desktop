using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using WhisperLoom.App.Platform.Windows.Overlay;

namespace WhisperLoom.App.Platform.Windows.Pill;

/// <summary>Art eines Hinweises: bestimmt Symbol und Farbe des Balkens (overlay.html Befund 9).</summary>
public enum HintTone
{
    /// <summary>Stufe gewechselt (Stufen-Symbol, Akzent).</summary>
    Stage,

    /// <summary>Text liegt in der Zwischenablage (Zwischenablage-Symbol, Akzent).</summary>
    Copied,

    /// <summary>Auskunft, z. B. „Ohne KI eingefügt“ (Info-Symbol, Akzent).</summary>
    Info,

    /// <summary>Nicht eingefügt oder Grenze erreicht (Warn-Symbol, Warnfarbe).</summary>
    Warning,

    /// <summary>Diktat gescheitert (Fehler-Symbol, Fehlerfarbe).</summary>
    Error,
}

/// <summary>
/// Kurzer Hinweis (Stufe gewechselt, ≈ 1,2 s) bzw. Toast (Kopiert, Passwortfeld, Fehler …), fokusfrei, folgt dem Systemthema.
/// Ein neuer Hinweis ersetzt den vorigen (nie gestapelt).
/// </summary>
public partial class HintWindow : Window
{
    public HintWindow()
    {
        InitializeComponent();
        NoActivate.Apply(this);
        Design.SystemTheme.Follow(this, Application.Current?.PlatformSettings);
    }

    /// <summary>Art des Hinweises für Status und Smoke: "stage" oder "toast".</summary>
    public string Kind { get; private set; } = "";

    public HintTone Tone { get; private set; }

    /// <summary>Text des Hinweises mit eingesetzten Tasten (Status, Screenreader).</summary>
    public string Text { get; private set; } = "";

    /// <param name="message">Text; mit <paramref name="keys"/> eine Vorlage mit {0}, {1} … für die Tastenkappen.</param>
    public void SetContent(string kind, string caption, string message, HintTone tone = HintTone.Info, IReadOnlyList<string>? keys = null)
    {
        Kind = kind;
        Tone = tone;
        Caption.Text = caption;
        Caption.IsVisible = caption.Length > 0;
        Message.Keys = keys ?? [];
        Message.Template = message;
        Text = keys is { Count: > 0 } ? string.Format(System.Globalization.CultureInfo.InvariantCulture, message, [.. keys]) : message;
        var cls = tone switch
        {
            HintTone.Warning => "warning",
            HintTone.Error => "error",
            _ => null,
        };
        foreach (var target in new StyledElement[] { Bar, KindIcon })
        {
            target.Classes.Set("warning", cls == "warning");
            target.Classes.Set("error", cls == "error");
        }
        KindIcon.Data = this.FindResource(IconKey(tone)) as Geometry;
        AutomationProperties.SetName(Panel, caption.Length > 0 ? $"{caption}: {Text}" : Text);
    }

    internal static string IconKey(HintTone tone) => tone switch
    {
        HintTone.Stage => "Loom.Icon.TextStage",
        HintTone.Copied => "Loom.Icon.Clipboard",
        HintTone.Warning => "Loom.Icon.Warning",
        HintTone.Error => "Loom.Icon.Error",
        _ => "Loom.Icon.Info",
    };
}
