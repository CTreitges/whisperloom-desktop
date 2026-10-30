using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests;

/// <summary>
/// Headless-Screenshots (Skia) für die Doku und den Bericht: nur mit Umgebungsvariable
/// <c>WLD_SCREENSHOT_DIR</c>, sonst übersprungen. Fenster ≤ 1200 px breit.
/// </summary>
public class ScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static void Save(Avalonia.Controls.Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public async Task Render_stage_and_settings()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        Loc.Instance.SetLanguage("en");
        var settings = new WhisperLoomSettings { Language = "en", Refine = new RefineSettings { Stage = "prompt", PromptEnabled = true } };
        using var f = new ControllerFixture(settings);
        f.Pipeline.RawText = "so um I want a short email to the team that the demo moves to Sunday at ten and everyone should bring their laptop";

        var vm = new StageViewModel(f.Controller);
        var stage = new StageWindow(vm) { Width = 1200, Height = 675 };
        stage.Show();
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(7);
        vm.Tick();
        Save(stage, "p2-stage-recording.png");

        await f.Controller.StopAsync();
        f.Controller.LastResult = "Write a short email to the team: the demo moves to Sunday at 10:00, and everyone should bring their laptop.";
        vm.Tick();
        Save(stage, "p2-stage-result.png");
        stage.Close();

        var store = new MemorySecretStore();
        store.Values[SecretNames.Stt] = "test-key-not-real-5";
        var svm = new SettingsViewModel(f.Controller, store, new FakeAccessChecker());
        await svm.LoadKeysAsync();
        svm.SttKey.ReplaceCommand.Execute(null);
        svm.SttKey.NewValue = "test-key-not-real-6";
        var window = new SettingsWindow(svm) { Width = 900, Height = 680 };
        window.Show();
        Save(window, "p2-settings-masked-key.png");
        window.Close();
        Loc.Instance.SetLanguage("de");
    }
}
