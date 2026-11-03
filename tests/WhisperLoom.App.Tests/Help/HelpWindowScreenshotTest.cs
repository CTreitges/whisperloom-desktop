using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Views.Help;

namespace WhisperLoom.App.Tests.Help;

/// <summary>
/// W2-HELP Nr. 7: Hilfe-Fenster als Bild – Übersicht, ein Abschnitt, Suche; hell/dunkel; DE/EN. Nur mit
/// <c>WLD_SCREENSHOT_DIR</c>, sonst übersprungen; Breite ≤ 1200 px.
/// </summary>
public sealed class HelpWindowScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static void Save(HelpWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Thread.Sleep(300);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void Render_overview_section_and_search()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        foreach (var lang in Loc.Languages)
        {
            Loc.Instance.SetLanguage(lang);
            foreach (var (theme, label) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                var window = new HelpWindow(new HelpViewModel()) { Width = 1000, Height = 760, RequestedThemeVariant = theme };
                window.Show();
                Save(window, $"help-1-overview-{label}-{lang}.png");
                window.ShowTopic("selection");
                Save(window, $"help-2-section-{label}-{lang}.png");
                window.Help!.SearchText = "Proxy"; // während der Suche ist kein Abschnitt gewählt (eine Wahl beendet sie)
                Save(window, $"help-3-search-{label}-{lang}.png");
                window.Close();
            }
        }
        Loc.Instance.SetLanguage("de");
    }
}
