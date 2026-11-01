using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// Abnahme W2-UI1 Nr. 7: Headless-Screenshots jeder Seite (hell/dunkel, DE/EN), der Suche und einer Seite mit
/// Fest/Vorgabe. Nur mit <c>WLD_SCREENSHOT_DIR</c>, sonst übersprungen; Breite ≤ 1200 px.
/// </summary>
public sealed class SettingsScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    /// <summary>Firma mit festen Erkennungs-Zeilen und zwei Vorgaben (eine davon vom Nutzer geändert).</summary>
    private static void Demo(ControllerFixture f)
    {
        f.Controller.Settings.Management.TenantName = "Beispiel GmbH";
        f.Controller.Settings.Stt.Model = "whisper-1";
        f.Controller.Settings.Management.Overrides = ["stt.model"];
        f.Controller.Policy = new Core.Settings.PolicySettings
        {
            Locked = ["engine", "language", "stt.providerId", "secrets.stt", "stt.baseUrl", "stt.readTimeoutSec"],
            Defaults = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["stt.model"] = System.Text.Json.JsonSerializer.SerializeToElement("gpt-transcribe"),
                ["sttFallback.enabled"] = System.Text.Json.JsonSerializer.SerializeToElement(false),
            },
        };
    }

    private static void Save(SettingsWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static async Task<(SettingsWindow Window, ControllerFixture F)> Open(string lang, ThemeVariant theme, bool managed = false)
    {
        Loc.Instance.SetLanguage(lang);
        var f = new ControllerFixture(new WhisperLoomSettings { Language = lang, Engine = "online" });
        if (managed)
        {
            Demo(f);
        }
        var store = new MemorySecretStore();
        store.Values[SecretNames.Stt] = "test-key-not-real-9";
        var vm = new SettingsViewModel(f.Controller, store, new FakeAccessChecker(), microphones: ["USB Mic"]);
        await vm.LoadKeysAsync();
        var shell = new SettingsShellViewModel(vm);
        var window = new SettingsWindow(shell) { Width = 980, Height = 760, RequestedThemeVariant = theme };
        window.Show();
        return (window, f);
    }

    [AvaloniaFact]
    public async Task Render_every_page_light_dark_de_en()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        foreach (var lang in new[] { "de", "en" })
        {
            foreach (var (theme, label) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                var (window, f) = await Open(lang, theme);
                using (f)
                {
                    var index = 1;
                    foreach (var page in SettingsUi.PageIds)
                    {
                        SettingsUi.Show(window, page);
                        Save(window, $"{index++}-{page}-{label}-{lang}.png");
                    }
                    window.Close();
                }
            }
        }
        Loc.Instance.SetLanguage("de");
    }

    [AvaloniaFact]
    public async Task Render_search_validation_and_managed_states()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        foreach (var (theme, label) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            var (window, f) = await Open("de", theme);
            using (f)
            {
                window.Shell!.SearchText = "modell";
                Save(window, $"search-{label}-de.png");
                window.Shell.SearchText = "";

                // Ungültige Eingabe inline, „Erweitert“ offen, „Eigenes Modell …“ gewählt.
                var page = (RecognitionPageViewModel)window.Shell.Page("recognition");
                page.AdvancedOpen = true;
                SettingsUi.Show(window, "recognition");
                window.Settings!.SttBaseUrl = "ftp://falsch";
                page.SttModel.Selected = page.SttModel.Items[^1];
                Save(window, $"validation-{label}-de.png");
                window.Close();
            }

            var (managed, g) = await Open("de", theme, managed: true);
            using (g)
            {
                SettingsUi.Show(managed, "recognition");
                Save(managed, $"managed-{label}-de.png");
                managed.Close();
            }

            var (en, h) = await Open("en", theme);
            using (h)
            {
                en.Shell!.SearchText = "proxy";
                Save(en, $"search-{label}-en.png");
                en.Close();
            }
        }
        Loc.Instance.SetLanguage("de");
    }
}
