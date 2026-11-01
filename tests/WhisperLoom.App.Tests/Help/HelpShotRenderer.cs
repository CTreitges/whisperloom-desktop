using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Tests.Help;

/// <summary>
/// Nimmt den Bereich einer Szene auf und zeichnet die Markierungen aus <c>screens.json</c> ein: Rahmen in Akzentfarbe um das
/// Ziel und eine Ziffer (1, 2, …) oben links, in der Reihenfolge der Liste. Ziele: <c>path:&lt;Einstellung&gt;</c> (Zeile) oder
/// <c>name:&lt;Name&gt;</c> (Steuerelement, auch aus Vorlagen). Höhe: bis zum Ende des Inhalts, höchstens die Fensterhöhe.
/// Doku: docs.avaloniaui.net/docs/reference/controls/rendertargetbitmap (abgerufen 2026-11-01).
/// </summary>
internal static class HelpShotRenderer
{
    /// <summary>Ziffer über dem Rahmen statt links daneben (Zusatz „@top“, z. B. bei Knöpfen nebeneinander).</summary>
    public static bool OnTop(string marker) => marker.EndsWith("@top", StringComparison.Ordinal);

    public static Control Find(Control root, string marker)
    {
        var target = marker.Split('@')[0];
        var (kind, value) = (target[..target.IndexOf(':')], target[(target.IndexOf(':') + 1)..]);
        var all = root.GetVisualDescendants().OfType<Control>().Concat(root.GetLogicalDescendants().OfType<Control>()).Distinct();
        var found = kind switch
        {
            "path" => all.OfType<SettingRow>().FirstOrDefault(r => r.Path == value && r.IsEffectivelyVisible),
            "name" => all.FirstOrDefault(c => c.Name == value && c.IsEffectivelyVisible),
            "text" => all.OfType<TextBlock>().FirstOrDefault(t => t.Text == Localization.Loc.Instance[value] && t.IsEffectivelyVisible),
            _ => null,
        };
        return found ?? throw new InvalidOperationException($"Markierung nicht gefunden oder unsichtbar: {marker}");
    }

    /// <summary>Rechteck des Ziels relativ zum Aufnahmebereich.</summary>
    public static Rect BoundsIn(Control target, Control root)
    {
        var origin = target.TranslatePoint(default, root) ?? throw new InvalidOperationException($"{target.Name}: nicht im Bereich");
        return new Rect(origin, target.Bounds.Size);
    }

    public static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        // Übergänge (Pfeil von „Erweitert“, Fokus) zu Ende laufen lassen.
        Thread.Sleep(300);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Aufnahme mit Markierungen; Rückgabe = die Rechtecke der Markierungen (für Tests).</summary>
    public static IReadOnlyList<Rect> Save(HelpScene scene, IReadOnlyList<string> markers, string path)
    {
        Settle(scene.Window);
        var root = scene.Target;
        var rects = markers.Select(m => BoundsIn(Find(root, m), root)).ToList();
        var height = scene.CropToContent ? Math.Min(root.Bounds.Height, ContentHeight(root)) : root.Bounds.Height;
        var size = new Size(Math.Ceiling(root.Bounds.Width), Math.Ceiling(height));
        var pixels = new PixelSize((int)size.Width, (int)size.Height);
        var accent = Brush(scene.Window, "Loom.Accent");
        var onAccent = Brush(scene.Window, "Loom.OnAccent");
        var background = Brush(scene.Window, "Loom.Background");

        // Aus dem gerenderten Fenster ausschneiden: gleiche Kantenglättung wie auf dem Bildschirm (ein eigenes
        // RenderTargetBitmap mit transparentem Grund lässt Text verschmiert wirken).
        var frame = scene.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("kein Frame");
        var origin = root.TranslatePoint(default, scene.Window) ?? default;
        using var final = new RenderTargetBitmap(pixels);
        using (var ctx = final.CreateDrawingContext())
        {
            ctx.FillRectangle(background, new Rect(size));
            ctx.DrawImage(frame, new Rect(origin, size), new Rect(size));
            var pen = new Pen(accent, 2.5);
            for (var i = 0; i < rects.Count; i++)
            {
                // Rahmen knapp innerhalb des Ziels, damit benachbarte Zeilen getrennt bleiben; Ziffer links außen neben dem
                // Rahmen (verdeckt keinen Inhalt), ohne Platz dort auf der Ecke.
                var r = rects[i].Deflate(1.5).Intersect(new Rect(size).Deflate(2));
                ctx.DrawRectangle(null, pen, r, 6, 6);
                var center = OnTop(markers[i]) && r.Y >= 26
                    ? new Point(r.X + r.Width / 2, r.Y - 14)
                    : r.X >= 26
                    ? new Point(r.X - 14, r.Y + Math.Min(r.Height / 2, 20))
                    : new Point(Math.Max(r.X, 12), Math.Max(r.Y, 12));
                ctx.DrawEllipse(accent, new Pen(background, 2), center, 11, 11);
                var text = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12, onAccent);
                ctx.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        final.Save(path, PngBitmapEncoderOptions.Default);
        return rects;
    }

    /// <summary>Höhe des Inhalts der Seite: gewünschte Höhe (mit Rand) des Inhalts im ersten ScrollViewer.</summary>
    private static double ContentHeight(Control root) =>
        root.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { Content: Control content } scroll
            ? content.DesiredSize.Height + scroll.TranslatePoint(default, root)!.Value.Y
            : root.Bounds.Height;

    private static IBrush Brush(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Teal;
}
