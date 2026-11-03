using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace WhisperLoom.App.Design;

/// <summary>
/// Ein Symbol des eigenen Sets (<c>Loom.Icon.*</c>, erzeugt von <c>tools/gen_icons.py</c>): Kontur der Geometrie im
/// 24-px-Raster, Strich 1,75 mit runden Enden, Farbe aus <see cref="Foreground"/> (erbt wie Text). Skaliert auf die Größe
/// (16 neben Text, 20 in Knöpfen und Leiste, 24 allein; zielstruktur.md §8). Ersetzt <c>PathIcon</c>, das nur füllt.
/// Doku: docs.avaloniaui.net/docs/custom-controls/custom-drawn-controls (Render, AffectsRender, PushTransform, abgerufen 2026-11-03).
/// </summary>
public sealed class LoomIcon : Control
{
    /// <summary>Rastergröße der Quellen.</summary>
    public const double Grid = 24;

    /// <summary>Strichbreite im Raster.</summary>
    public const double Stroke = 1.75;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LoomIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LoomIcon>();

    static LoomIcon()
    {
        AffectsRender<LoomIcon>(DataProperty, ForegroundProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(Grid, availableSize.Width), Math.Min(Grid, availableSize.Height));

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Foreground is not { } brush)
        {
            return;
        }
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Grid;
        if (scale <= 0)
        {
            return;
        }
        var offset = new Vector((Bounds.Width - (Grid * scale)) / 2, (Bounds.Height - (Grid * scale)) / 2);
        var pen = new Pen(brush, Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
        {
            context.DrawGeometry(null, pen, data);
        }
    }
}
