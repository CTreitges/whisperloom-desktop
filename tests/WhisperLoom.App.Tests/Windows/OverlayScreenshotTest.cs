using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.Pill;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>
/// Headless-Screenshots der Windows-Bedienung (Goal-File A3.2): Overlay in allen Zuständen, Pegel-Pille, Stufen-Hinweis,
/// Tastenkürzel-Seite. Nur mit <c>WLD_SCREENSHOT_DIR</c>, sonst übersprungen. Auf dunklem Grund, ≤ 1200 px.
/// </summary>
public class OverlayScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static void Save(Window window, string name)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    private static T OnDark<T>(T window) where T : Window
    {
        // Transparente Fenster würden schwarz gerendert; für das Bild der App-Hintergrund.
        window.Background = (Avalonia.Media.IBrush)Avalonia.Application.Current!.FindResource("WlBackground")!;
        return window;
    }

    [AvaloniaFact]
    public async Task Render_overlay_pill_hint_and_hotkey_settings()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        Loc.Instance.SetLanguage("de");
        var vm = new OverlayViewModel(() => { }, () => { }, _ => { })
        {
            Stage = Loc.Instance["Stage_prompt"],
            Elapsed = TimeSpan.FromSeconds(14),
            Level = 0.55,
        };
        foreach (var (state, name) in new[] { (OverlayState.Recording, "recording"), (OverlayState.Paused, "paused"), (OverlayState.Processing, "processing"), (OverlayState.Error, "error") })
        {
            vm.State = state;
            vm.ErrorKey = state == OverlayState.Error ? "Error_Network" : null;
            Save(OnDark(new OverlayWindow(vm)), $"p3-overlay-{name}.png");
        }

        Save(OnDark(new PillWindow(new PillViewModel { Level = 0.6 })), "p3-pill.png");
        Save(OnDark(new PillWindow(new PillViewModel { Processing = true })), "p3-pill-processing.png");

        var hint = new HintWindow();
        hint.SetContent("stage", Loc.Instance["Stage_Label"], Loc.Instance["Stage_beautify"]);
        Save(OnDark(hint), "p3-stage-hint.png");

        var toast = new HintWindow();
        toast.SetContent("toast", "WhisperLoom", Loc.Instance["Insert_TargetProtected"]);
        Save(OnDark(toast), "p3-toast-uipi.png");

        using var f = new ControllerFixture();
        var svm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker(), recordHotkey: () => Task.FromResult<string?>(null));
        var settings = new Views.SettingsWindow(svm) { Width = 900, Height = 680 };
        settings.Show();
        settings.FindControl<TabItem>("TabHotkeys")!.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        Save(settings, "p3-settings-hotkeys.png");
    }
}
