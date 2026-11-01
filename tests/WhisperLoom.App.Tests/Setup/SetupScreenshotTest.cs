using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Setup;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>
/// Headless-Screenshots aller Assistent-Schritte (<c>p6-*.png</c>, ≤ 1200 px): nur mit <c>WLD_SCREENSHOT_DIR</c>,
/// sonst übersprungen.
/// </summary>
public sealed class SetupScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static void Save(SetupWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void GoTo(SetupViewModel vm, SetupStep step)
    {
        while (vm.Current != step)
        {
            vm.Next();
        }
    }

    [AvaloniaFact]
    public async Task Render_alle_Schritte()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        Loc.Instance.SetLanguage("de");
        using (var s = new SetupFixture(recordHotkey: () => Task.FromResult<string?>("Ctrl+Win")))
        {
            s.Secrets.Values[SecretNames.Stt] = "test-key-not-real-62";
            await s.Settings.LoadKeysAsync();
            var window = new SetupWindow(s.Vm);
            window.Show();
            Save(window, "p6-1-sprache.png");

            GoTo(s.Vm, SetupStep.Microphone);
            s.Settings.Microphone = s.Settings.Microphones[1];
            s.Mic.Start();
            s.Mic.Tick();
            Save(window, "p6-2-mikrofon-pegel.png");

            GoTo(s.Vm, SetupStep.Recognition);
            await s.Settings.SttKey.CheckCommand.ExecuteAsync(null);
            Save(window, "p6-3-erkennung-cloud.png");
            s.Settings.Local.Engine = s.Settings.Local.Engines.Single(e => e.Value == "offline");
            Save(window, "p6-3b-erkennung-lokal.png");
            s.Settings.Local.Engine = s.Settings.Local.Engines.Single(e => e.Value == "online");

            GoTo(s.Vm, SetupStep.Stage);
            Save(window, "p6-4-stufe.png");

            GoTo(s.Vm, SetupStep.Hotkey);
            await s.Vm.Hotkey.TestCommand.ExecuteAsync(null);
            Save(window, "p6-5-hotkey-test-windows.png");

            GoTo(s.Vm, SetupStep.Autostart);
            s.Vm.AutostartEnabled = true;
            Save(window, "p6-6-autostart.png");

            GoTo(s.Vm, SetupStep.Done);
            Save(window, "p6-7-fertig-windows.png");
            window.Close();
        }

        using (var s = new SetupFixture(windows: false))
        {
            var window = new SetupWindow(s.Vm);
            window.Show();
            GoTo(s.Vm, SetupStep.Hotkey);
            Save(window, "p6-5b-kuerzel-linux.png");
            GoTo(s.Vm, SetupStep.Done);
            Save(window, "p6-7b-fertig-linux.png");
            window.Close();
        }

        var policy = new PolicySettings { Locked = ["language", "desktop.uiLanguage", "engine", "stt", "secrets.stt", "refine"] };
        using (var s = new SetupFixture(policy: policy))
        {
            var window = new SetupWindow(s.Vm);
            window.Show();
            Save(window, "p6-8-richtlinie-weniger-schritte.png");
            window.Close();
        }
    }
}
