using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Screenshots aller Ansichten aus <see cref="UxViews"/> für Audit und Bericht (Vorher/Nachher): nur mit
/// <c>WLD_UX_SHOTS_DIR</c>, Sprache aus <c>WLD_UX_LANG</c> (Standard de), je Variante <c>&lt;id&gt;-&lt;variante&gt;.png</c>.
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
        try
        {
            foreach (var view in UxViews.All)
            {
                foreach (var (name, theme) in UxViews.Themes)
                {
                    using var scene = await view.Build(theme);
                    UxStage.Settle(scene.Window);
                    var frame = scene.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException(view.Id);
                    Assert.True(frame.PixelSize.Width <= 1200, $"{view.Id}: Breite {frame.PixelSize.Width}");
                    frame.Save(Path.Combine(dir, $"{view.Id}-{name}.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(before);
        }
    }
}
