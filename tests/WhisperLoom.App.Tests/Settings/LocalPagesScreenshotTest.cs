using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// W2-LOCAL Nr. 6: Headless-Screenshots der Seiten „Erkennung“, „Text &amp; Stufen“, „Anbieter“ und „Lokale Modelle“ mit
/// Beispiel-Einträgen (hell/dunkel, DE/EN), ganze Seitenhöhe. Nur mit <c>WLD_SCREENSHOT_DIR</c>, sonst übersprungen.
/// </summary>
public sealed class LocalPagesScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static WhisperLoomSettings Demo(string lang)
    {
        var s = new WhisperLoomSettings { Language = lang, Engine = "offline" };
        s.Providers.Custom.Add(new CustomProvider { Id = "labor", Name = "Labor-Server", Protocol = "openai", BaseUrl = "https://llm.labor.example/v1", Models = ["whisper-large-v3", "qwen3-32b"] });
        s.Providers.Custom.Add(new CustomProvider { Id = "nas", Name = "NAS Ollama", Protocol = "ollama", BaseUrl = "http://192.168.1.20:11434", KeyOptional = true });
        s.Stt.ProviderId = "custom:labor";
        s.Offline.CustomModels.Add(new CustomWhisperModel { Name = "whisper-de-finetune", Path = "D:/Modelle/ggml-de-finetune.bin" });
        s.Offline.Model = "custom:whisper-de-finetune";
        LocalSettings.Set(s, new LocalSettings
        {
            TextEngine = "bundled",
            Servers = [new LocalServer { Name = "LM Studio", Kind = "text", BaseUrl = "http://127.0.0.1:1234/v1", Model = "qwen2.5-7b-instruct" },
                new LocalServer { Name = "faster-whisper", Kind = "stt", BaseUrl = "http://127.0.0.1:8000/v1" }],
        });
        return s;
    }

    [AvaloniaFact]
    public async Task Render_local_pages_light_dark_de_en()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        foreach (var lang in new[] { "de", "en" })
        {
            foreach (var (theme, themeName) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                Loc.Instance.SetLanguage(lang);
                using var f = new ControllerFixture(Demo(lang));
                var store = new MemorySecretStore();
                store.Values["custom:labor"] = "test-key-not-real-8";
                var vm = new SettingsViewModel(f.Controller, store, new FakeAccessChecker());
                await vm.LoadKeysAsync();
                var window = new SettingsWindow(new SettingsShellViewModel(vm)) { Width = 980, Height = 1500, RequestedThemeVariant = theme };
                window.Show();
                foreach (var page in new[] { "recognition", "text", "connections" })
                {
                    SettingsUi.Show(window, page);
                    if (window.Shell!.SelectedPage is ConnectionsPageViewModel providers)
                    {
                        await providers.LoadKeysAsync();
                    }
                    Save(window, $"local-{page}-{lang}-{themeName}.png");
                }
                window.Close();
            }
        }
    }

    private static void Save(SettingsWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Thread.Sleep(400);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
