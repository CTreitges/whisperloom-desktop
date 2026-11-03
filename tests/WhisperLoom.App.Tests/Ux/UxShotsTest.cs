using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Screenshots aller Ansichten aus <see cref="UxViews"/> für Audit und Bericht (Vorher/Nachher): nur mit
/// <c>WLD_UX_SHOTS_DIR</c>, Sprache aus <c>WLD_UX_LANG</c> (Standard de), Auswahl per Präfix <c>WLD_UX_ONLY</c>, je Variante <c>&lt;id&gt;-&lt;variante&gt;.png</c>.
/// <c>WLD_UX_SCALE=2</c> rendert mit 200 % Textgröße (Dateiname <c>&lt;id&gt;-&lt;variante&gt;-200.png</c>) für den Bericht.
/// Breite ≤ 1200 px, Höhe = ganze Ansicht.
/// </summary>
public sealed class UxShotsTest
{
    [AvaloniaFact]
    public async Task Render_every_view_in_every_theme()
    {
        var dir = Environment.GetEnvironmentVariable("WLD_UX_SHOTS_DIR");
        if (dir is null)
        {
            Assert.Skip("WLD_UX_SHOTS_DIR nicht gesetzt");
            return;
        }
        var before = Loc.Instance.Language;
        Loc.Instance.SetLanguage(Environment.GetEnvironmentVariable("WLD_UX_LANG") ?? "de");
        Directory.CreateDirectory(dir);
        var scale = double.TryParse(Environment.GetEnvironmentVariable("WLD_UX_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1.0;
        var suffix = scale == 1.0 ? "" : $"-{scale * 100:0}";
        Design.TextScale.Apply(Avalonia.Application.Current!, scale);
        try
        {
            // WLD_UX_ONLY=setup- rendert nur Ansichten mit diesem Präfix (schneller beim Arbeiten an einem Fenster).
            var only = Environment.GetEnvironmentVariable("WLD_UX_ONLY") ?? "";
            foreach (var view in UxViews.All.Where(v => v.Id.StartsWith(only, StringComparison.Ordinal)))
            {
                foreach (var (name, theme) in UxViews.Themes)
                {
                    using var scene = await view.Build(theme);
                    UxStage.Settle(scene.Window);
                    var frame = scene.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException(view.Id);
                    Assert.True(frame.PixelSize.Width <= 1200, $"{view.Id}: Breite {frame.PixelSize.Width}");
                    frame.Save(Path.Combine(dir, $"{view.Id}-{name}{suffix}.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally
        {
            Design.TextScale.Apply(Avalonia.Application.Current!, 1.0);
            Loc.Instance.SetLanguage(before);
        }
    }
}
