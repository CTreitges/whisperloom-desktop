using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace WhisperLoom.App.Views.Help;

/// <summary>Eine Form der Illustration: Geometrie, Token der Füllung bzw. des Strichs, Strichbreite, optionale Transformation.</summary>
public sealed record IllustrationShape(Geometry Geometry, string? Fill, string? Stroke, double StrokeWidth, Matrix? Transform);

/// <summary>
/// Zeigt eine Hilfe-Illustration (SVG aus <c>tools/gen_help_illustrations.py</c>, 320×240) in den Loom-Farben der aktuellen
/// Variante: Jede Form trägt ihr Token (<c>data-fill</c>/<c>data-stroke</c>), gezeichnet wird mit dem Pinsel
/// <c>Loom.&lt;Token&gt;</c> aus <c>Design/Tokens.axaml</c>. Liest nur dieses eingeschränkte SVG (Pfade, translate/scale).
/// Doku: docs.avaloniaui.net/docs/reference/controls/path (Pfad-Syntax wie SVG, abgerufen 2026-11-01).
/// </summary>
public sealed class HelpIllustration : Control
{
    public const double ImageWidth = 320;
    public const double ImageHeight = 240;

    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<HelpIllustration, string?>(nameof(Source));

    private static readonly Dictionary<string, IReadOnlyList<IllustrationShape>> Cache = [];
    private IReadOnlyList<IllustrationShape> _shapes = [];

    /// <summary>Füllung und Stift je Shape in der aktuellen Variante (PERF-4); null = neu auflösen.</summary>
    private (IBrush? Fill, IPen? Pen)[]? _paint;

    static HelpIllustration() => AffectsRender<HelpIllustration>(SourceProperty);

    /// <summary>Hell/Dunkel gewechselt: Pinsel der neuen Variante auflösen und neu zeichnen.</summary>
    public HelpIllustration()
    {
        ActualThemeVariantChanged += (_, _) => Repaint();
        // Prüfer PR #33 N3: auch geänderte Ressourcen (z. B. Tokens zur Laufzeit) neu auflösen.
        ResourcesChanged += (_, _) => Repaint();
    }

    private void Repaint()
    {
        _paint = null;
        InvalidateVisual();
    }

    /// <summary>avares-URI der SVG-Datei.</summary>
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public IReadOnlyList<IllustrationShape> Shapes => _shapes;

    public static IReadOnlyList<IllustrationShape> Load(string uri)
    {
        if (Cache.TryGetValue(uri, out var cached))
        {
            return cached;
        }
        using var stream = AssetLoader.Open(new Uri(uri));
        var shapes = Parse(XDocument.Load(stream));
        Cache[uri] = shapes;
        return shapes;
    }

    public static IReadOnlyList<IllustrationShape> Parse(XDocument svg) =>
    [
        .. svg.Descendants().Where(e => e.Name.LocalName == "path").Select(e => new IllustrationShape(
            Geometry.Parse((string)e.Attribute("d")!),
            (string?)e.Attribute("data-fill"),
            (string?)e.Attribute("data-stroke"),
            double.Parse((string?)e.Attribute("stroke-width") ?? "0", CultureInfo.InvariantCulture),
            ParseTransform((string?)e.Attribute("transform")))),
    ];

    /// <summary>„translate(x y) scale(s)“ bzw. Teile davon; anderes gibt es in den erzeugten Dateien nicht.</summary>
    public static Matrix? ParseTransform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var matrix = Matrix.Identity;
        double translateX = 0, translateY = 0;
        foreach (var part in text.Split(')', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var open = part.IndexOf('(');
            var name = part[..open].Trim();
            var values = part[(open + 1)..].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (name == "translate")
            {
                translateX = values[0];
                translateY = values.Length > 1 ? values[1] : 0;
            }
            else if (name == "scale")
            {
                matrix = Matrix.CreateScale(values[0], values.Length > 1 ? values[1] : values[0]);
            }
        }
        // SVG wendet von rechts nach links an: erst skalieren, dann verschieben.
        return matrix * Matrix.CreateTranslation(translateX, translateY);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? ImageWidth : Math.Min(availableSize.Width, ImageWidth);
        return new Size(width, width * ImageHeight / ImageWidth);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _shapes = Source is { } uri ? Load(uri) : [];
            _paint = null;
        }
    }

    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width / ImageWidth, Bounds.Height / ImageHeight);
        if (scale <= 0)
        {
            return;
        }
        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));
        var paint = Paint();
        for (var i = 0; i < _shapes.Count; i++)
        {
            var shape = _shapes[i];
            using var transform = shape.Transform is { } m ? context.PushTransform(m) : default(DrawingContext.PushedState?);
            context.DrawGeometry(paint[i].Fill, paint[i].Pen, shape.Geometry);
        }
    }

    /// <summary>
    /// Füllungen und Stifte aller Shapes, aufgelöst einmal je Quelle und Theme-Variante statt bei jedem Render
    /// (vorher: <c>TryFindResource</c> zweimal und ein neuer <see cref="Pen"/> je Shape und Frame).
    /// </summary>
    internal (IBrush? Fill, IPen? Pen)[] Paint() => _paint ??=
    [
        .. _shapes.Select(shape => (Brush(shape.Fill), Brush(shape.Stroke) is { } stroke
            ? (IPen?)new Pen(stroke, shape.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
            : null)),
    ];

    /// <summary>Pinsel des Tokens in der aktuellen Variante; unbekannt → nichts.</summary>
    public IBrush? Brush(string? token) =>
        token is not null && this.TryFindResource("Loom." + token, ActualThemeVariant, out var value) ? value as IBrush : null;
}
