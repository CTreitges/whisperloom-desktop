using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Headless-Screenshots für W2-SEL (Abnahme 7): Stufen-Wähler am Cursor (als Szene neben markiertem Text und als Fenster),
/// schwebender Knopf, Einstellungen „Markierung verarbeiten“ – hell und dunkel, DE und EN. Nur mit <c>WLD_SCREENSHOT_DIR</c>.
/// </summary>
public sealed class SelectionScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static void Save(Window window, string name)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    private static StagePickerViewModel Picker()
    {
        var stages = new[] { RefineMode.Polish, RefineMode.Beautify, RefineMode.Summarize }
            .Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()])).ToList();
        stages.Add(("instruction", Loc.Instance["Selection_Instruction"]));
        var picker = new StagePickerViewModel(stages, "polish");
        picker.Move(1); // Stufe 2 markiert, wie nach ↓
        return picker;
    }

    /// <summary>Szene: Dokument mit markiertem Satz, darunter der Wähler an der Schreibmarke bzw. der Knopf neben der Markierung.</summary>
    private static Window Scene(ThemeVariant theme, Control overlay, double overlayTop)
    {
        var app = Application.Current!;
        IBrush Brush(string key) => (IBrush)app.FindResource(theme, key)!;
        var selected = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#663584E4")),
            Child = new TextBlock { Text = "der Bericht ist fertig ich schick ihn dir morgen", FontSize = 16, Foreground = Brush("Loom.Text") },
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        line.Children.Add(new TextBlock { Text = "Hallo Anna,", FontSize = 16, Foreground = Brush("Loom.Text") });
        line.Children.Add(selected);
        var doc = new Border
        {
            Background = Brush("Loom.Surface"),
            BorderBrush = Brush("Loom.Separator"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 20),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Entwurf – E-Mail", FontSize = 12, Foreground = Brush("Loom.TextSecondary") },
                    line,
                    new TextBlock { Text = "Viele Grüße", FontSize = 16, Foreground = Brush("Loom.Text") },
                },
            },
        };
        var canvas = new Canvas { Width = 760, Height = 460 };
        Canvas.SetLeft(doc, 20);
        Canvas.SetTop(doc, 20);
        canvas.Children.Add(doc);
        Canvas.SetLeft(overlay, 130);
        Canvas.SetTop(overlay, overlayTop);
        canvas.Children.Add(overlay);
        return new Window { Width = 800, Height = 500, Content = canvas, Background = Brush("Loom.Background"), RequestedThemeVariant = theme };
    }

    [AvaloniaFact]
    public void Render_picker_button_and_settings()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        foreach (var lang in new[] { "de", "en" })
        {
            Loc.Instance.SetLanguage(lang);
            foreach (var (variant, theme) in new[] { ("dark", ThemeVariant.Dark), ("light", ThemeVariant.Light) })
            {
                var pickerWindow = new SelectionPickerWindow(noActivate: false) { DataContext = Picker(), RequestedThemeVariant = theme };
                var pickerContent = (Control)pickerWindow.Content!;
                pickerWindow.Content = null;
                pickerContent.Width = 290;
                pickerContent.DataContext = pickerWindow.DataContext;
                Save(Scene(theme, pickerContent, 92), $"sel-01-waehler-am-cursor-{variant}-{lang}.png");

                var buttonWindow = new SelectionButtonWindow { RequestedThemeVariant = theme };
                var button = (Control)buttonWindow.Content!;
                buttonWindow.Content = null;
                Save(Scene(theme, button, 86), $"sel-02-schwebender-knopf-{variant}-{lang}.png");

                using var f = new ControllerFixture();
                f.Controller.Settings.Selection.FloatingButton = true;
                var svm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
                var settings = new SettingsWindow(svm) { Width = 900, Height = 760, RequestedThemeVariant = theme };
                settings.Show();
                var page = Settings.SettingsUi.Show(settings, "dictation");
                Dispatcher.UIThread.RunJobs();
                page.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
                Save(settings, $"sel-03-einstellungen-markierung-{variant}-{lang}.png");
            }
        }
        Loc.Instance.SetLanguage("de");
    }
}
