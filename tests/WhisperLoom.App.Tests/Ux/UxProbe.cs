using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>Ein bedienbares Element mit Lage, Größe und Namen (AutomationPeer).</summary>
internal sealed record UxTarget(Control Control, Rect Box, string Name)
{
    public string Kind => Control.GetType().Name;

    public string Label => string.IsNullOrEmpty(Control.Name) ? Kind : $"{Kind}#{Control.Name}";
}

/// <summary>Ein Text mit Vorder- und wirksamem Hintergrund und dem Kontrast nach WCAG 2.x.</summary>
internal sealed record UxTextContrast(TextBlock Text, Color Foreground, Color Background, double Ratio, bool Large);

/// <summary>Eine Textkante: Inhaltsbeginn eines Textes bzw. Rahmenbeginn eines umrandeten Steuerelements.</summary>
internal sealed record UxEdge(Control Control, double X, string Region, bool Bordered, string Sample);

/// <summary>
/// Messungen an einer gerenderten Ansicht für die Ux-Tests und das Audit: bedienbare Elemente (Größe, Name), Kontrast je Text,
/// Textkanten je Bereich, Schriftgrößen, Platzhalter. Rein lesend; die Regeln stehen in den Testklassen.
/// Doku: docs.avaloniaui.net/docs/concepts/accessibility/automation-peers (abgerufen 2026-11-02).
/// </summary>
internal static class UxProbe
{
    public static IEnumerable<Control> Visible(Control root) =>
        root.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0);

    private static bool InScrollBar(Control c) => c.FindAncestorOfType<ScrollBar>() is not null;

    /// <summary>Steuerelemente, die man anklicken oder fokussieren kann (ohne Teile von Rollbalken und Textfeldern).</summary>
    public static bool IsInteractive(Control c) => c switch
    {
        ScrollBar or Thumb or RepeatButton => false,
        _ when InScrollBar(c) => false,
        Button or ComboBox or TextBox or ListBoxItem or Slider or MenuItem => c.FindAncestorOfType<TextBox>() is null,
        _ => false,
    };

    public static List<UxTarget> Targets(Control root) =>
        [.. Visible(root).Where(IsInteractive).Select(c => new UxTarget(c, BoxIn(c, root), NameOf(c)))];

    public static string NameOf(Control c)
    {
        var peer = ControlAutomationPeer.CreatePeerForElement(c);
        return peer.GetName() ?? "";
    }

    public static Rect BoxIn(Control c, Visual root) =>
        c.TranslatePoint(default, root) is { } p ? new Rect(p, c.Bounds.Size) : new Rect(c.Bounds.Size);

    // ---- Kontrast ----

    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    public static double Ratio(Color a, Color b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Legt <paramref name="top"/> (mit Alpha) auf <paramref name="bottom"/> (deckend).</summary>
    public static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static Color? ColorOf(IBrush? brush) => brush is ISolidColorBrush s
        ? Color.FromArgb((byte)Math.Round(s.Color.A * s.Opacity), s.Color.R, s.Color.G, s.Color.B)
        : null;

    private static IBrush? BackgroundOf(Visual v) => v switch
    {
        Border b => b.Background,
        Panel p => p.Background,
        ContentPresenter cp => cp.Background,
        TemplatedControl t => t.Background,
        _ => null,
    };

    /// <summary>Wirksamer Hintergrund hinter <paramref name="c"/>: Flächen der Vorfahren von innen nach außen gemischt.</summary>
    public static Color BackgroundBehind(Visual c)
    {
        var layers = new List<Color>();
        for (Visual? v = c.GetVisualParent(); v is not null; v = v.GetVisualParent())
        {
            if (ColorOf(BackgroundOf(v)) is { A: > 0 } color)
            {
                layers.Add(color);
                if (color.A == 255)
                {
                    break;
                }
            }
        }
        var result = Colors.Black;
        for (var i = layers.Count - 1; i >= 0; i--)
        {
            result = Over(layers[i], result);
        }
        return result;
    }

    private static double OpacityOf(Visual c)
    {
        var o = 1.0;
        for (Visual? v = c; v is not null; v = v.GetVisualParent())
        {
            o *= v.Opacity;
        }
        return o;
    }

    /// <summary>Kontrast je sichtbarem Text (deaktivierte Elemente ausgenommen: WCAG 1.4.3 nimmt inaktive Bedienelemente aus).</summary>
    public static List<UxTextContrast> TextContrast(Control root) =>
        [.. Visible(root).OfType<TextBlock>()
            .Where(t => t.IsEffectivelyEnabled && OpacityOf(t) >= 0.05 && !string.IsNullOrWhiteSpace(t.Text ?? string.Concat(t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? [])))
            .Select(t =>
            {
                var bg = BackgroundBehind(t);
                var fg = ColorOf(t.Foreground) ?? Colors.Black;
                fg = Color.FromArgb((byte)Math.Round(fg.A * OpacityOf(t)), fg.R, fg.G, fg.B);
                var shown = Over(fg, bg);
                var large = t.FontSize >= 24 || (t.FontSize >= 18.66 && t.FontWeight >= FontWeight.Bold);
                return new UxTextContrast(t, shown, bg, Ratio(shown, bg), large);
            })];

    /// <summary>
    /// Rahmen, an denen man ein Bedienelement erkennt (WCAG 1.4.11, 3:1): Rand von Textfeldern und Auswahllisten, äußerer Rand
    /// eines Schalters im Aus-Zustand bzw. seine Füllung im An-Zustand, Kästchen von Checkboxen. Knöpfe mit Text sind über
    /// ihre Beschriftung erkennbar und nicht dabei.
    /// </summary>
    public static List<(Control Control, string Part, Color Stroke, Color Background, double Ratio)> UiContrast(Control root)
    {
        var result = new List<(Control, string, Color, Color, double)>();
        void Add(Control owner, string part, IBrush? brush)
        {
            if (ColorOf(brush) is { } stroke && owner.IsEffectivelyEnabled)
            {
                var bg = BackgroundBehind(owner);
                var shown = Over(stroke, bg);
                result.Add((owner, part, shown, bg, Ratio(shown, bg)));
            }
        }
        foreach (var c in Visible(root))
        {
            switch (c)
            {
                case TextBox or ComboBox when IsInteractive(c):
                    Add(c, "Rand", ((TemplatedControl)c).BorderBrush);
                    break;
                case ToggleSwitch ts:
                    var outer = ts.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "OuterBorder");
                    var knobBounds = ts.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "SwitchKnobBounds");
                    if (ts.IsChecked == true && knobBounds is not null)
                    {
                        Add(ts, "Füllung an", knobBounds.Background);
                    }
                    else if (outer is not null)
                    {
                        Add(ts, "Rand aus", outer.BorderBrush);
                    }
                    break;
                case CheckBox cb:
                    var box = cb.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "NormalRectangle");
                    if (box is not null)
                    {
                        Add(cb, cb.IsChecked == true ? "Kästchen an" : "Kästchen aus", cb.IsChecked == true ? box.Background : box.BorderBrush);
                    }
                    break;
            }
        }
        return result;
    }

    // ---- Textkanten ----

    /// <summary>
    /// Bereich eines Elements für die Textkante: spätere Spalten eines Grids, rechts/mittig ausgerichtete Gruppen und Folge-Elemente
    /// einer waagrechten Reihe haben eine eigene Kante (Richtlinie 02 §2.1, Ausnahmen).
    /// </summary>
    public static string? RegionOf(Control c, Control root)
    {
        var key = "";
        Visual child = c;
        for (var v = c.GetVisualParent(); v is not null && !ReferenceEquals(child, root); child = v, v = v.GetVisualParent())
        {
            if (child is Avalonia.Layout.Layoutable l && l.HorizontalAlignment is Avalonia.Layout.HorizontalAlignment.Right or Avalonia.Layout.HorizontalAlignment.Center)
            {
                return null;
            }
            switch (v)
            {
                case Grid g when child is Control cc && Grid.GetColumn(cc) > 0:
                    key = $"{g.GetHashCode():x}:{Grid.GetColumn(cc)}/" + key;
                    break;
                case StackPanel { Orientation: Avalonia.Layout.Orientation.Horizontal } sp when sp.Children.Count > 0 && !ReferenceEquals(sp.Children[0], child):
                case WrapPanel wp when wp.Children.Count > 0 && !ReferenceEquals(wp.Children[0], child):
                    return null;
                case DockPanel when child is Control dc && DockPanel.GetDock(dc) == Dock.Right:
                    return null;
            }
        }
        return key == "" ? "main" : key;
    }

    private static bool HasBorder(TemplatedControl t) =>
        t.BorderThickness.Left > 0 && ColorOf(t.BorderBrush) is { A: > 0 };

    /// <summary>Textkanten der Ansicht: Texte (Inhaltsbeginn) außerhalb von Steuerelementen und Rahmenbeginn umrandeter Felder/Knöpfe.</summary>
    public static List<UxEdge> Edges(Control root)
    {
        var edges = new List<UxEdge>();
        foreach (var c in Visible(root))
        {
            var inControl = c.FindAncestorOfType<Button>() is not null || c.FindAncestorOfType<TextBox>() is not null
                || c.FindAncestorOfType<ComboBox>() is not null || c.FindAncestorOfType<ListBoxItem>() is not null;
            if (c is TextBlock t && !inControl && !string.IsNullOrWhiteSpace(t.Text) && RegionOf(t, root) is { } region)
            {
                edges.Add(new UxEdge(t, BoxIn(t, root).X + t.Padding.Left, region, false, Sample(t.Text)));
            }
            else if (c is TextBox or ComboBox or Button && IsInteractive(c) && c is TemplatedControl tc && RegionOf(c, root) is { } r2)
            {
                var link = c.Classes.Contains("link");
                var x = BoxIn(c, root).X + (link ? tc.Padding.Left : 0);
                if (link || HasBorder(tc) || c is TextBox or ComboBox)
                {
                    edges.Add(new UxEdge(c, x, r2, !link, Sample(c is Button { Content: string s } ? s : c.Name ?? c.GetType().Name)));
                }
            }
        }
        return edges;
    }

    private static string Sample(string? text) => text is null ? "" : text.Length <= 28 ? text : text[..27] + "…";

    public static IEnumerable<IGrouping<double, TextBlock>> FontSizes(Control root) =>
        Visible(root).OfType<TextBlock>().Where(t => !string.IsNullOrWhiteSpace(t.Text)).GroupBy(t => t.FontSize).OrderBy(g => g.Key);

    public static List<TextBox> Placeholders(Control root) =>
        [.. Visible(root).OfType<TextBox>().Where(t => !string.IsNullOrEmpty(t.PlaceholderText))];
}
