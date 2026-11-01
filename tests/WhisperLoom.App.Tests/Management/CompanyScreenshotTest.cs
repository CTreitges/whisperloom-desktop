using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Setup;
using WhisperLoom.App.Tests.Settings;
using WhisperLoom.App.Tests.Setup;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;

namespace WhisperLoom.App.Tests.Management;

/// <summary>
/// Screenshots W2-ENR (Kriterium 9): Assistent (beide Pfade, wartet auf Freigabe), Seite „Firma“ (verbunden, gesperrt) und
/// eine Einstellungsseite mit ausgeblendeten festen Werten. Nur mit <c>WLD_SCREENSHOT_DIR</c>, Breite ≤ 1200 px.
/// </summary>
public sealed class CompanyScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static void Save(Window window, string name)
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

    private static void Demo(FakeManagementServer server)
    {
        server.TenantName = "Muster GmbH";
        server.Settings["language"] = "de";
        server.Settings["refine"] = new JsonObject { ["stage"] = "polish" };
        server.Settings["output"] = new JsonObject { ["alwaysCopy"] = true };
        server.Levels["language"] = "fixed";
        server.Levels["refine.stage"] = "fixed";
        server.Levels["output.alwaysCopy"] = "default";
    }

    [AvaloniaFact]
    public async Task Render_assistent_und_seite_firma()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        foreach (var (theme, label) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            Loc.Instance.SetLanguage("de");
            using (var r = new ManagementRig(uiThread: true))
            {
                Demo(r.Server);
                var settings = new SettingsViewModel(r.F.Controller, r.Secrets, new FakeAccessChecker(), ["USB-Mikrofon"]) { Company = r.Service };
                var gate = new SemaphoreSlim(0);
                var vm = new SetupViewModel(settings, new SetupState(r.F.DataDir), new FakeAutostart(), null, null, true, "whisperloom toggle",
                    r.Service, async (_, ct) => await gate.WaitAsync(ct));
                var window = new SetupWindow(vm) { RequestedThemeVariant = theme };
                window.Show();
                Save(window, $"enr-1-assistent-frage-{label}.png");
                vm.UseCompany = true;
                vm.Next();
                vm.Join!.Server = FakeManagementServer.Url;
                vm.Join.Code = FakeManagementServer.Code;
                Save(window, $"enr-2-assistent-firma-{label}.png");
                await vm.Join.JoinCommand.ExecuteAsync(null);
                Save(window, $"enr-3-assistent-wartet-{label}.png");
                r.Server.Set("approved");
                gate.Release();
                await vm.Join.Polling;
                Save(window, $"enr-4-assistent-freigegeben-weiter-{label}.png");
                window.Close();

                var session = new SettingsViewModel(r.F.Controller, r.Secrets, new FakeAccessChecker(), ["USB-Mikrofon"]) { Company = r.Service };
                await session.LoadKeysAsync();
                var settingsWindow = new SettingsWindow(new SettingsShellViewModel(session)) { Width = 980, Height = 760, RequestedThemeVariant = theme };
                settingsWindow.Show();
                SettingsUi.Show(settingsWindow, "company");
                Save(settingsWindow, $"enr-5-seite-firma-verbunden-{label}.png");
                SettingsUi.Show(settingsWindow, "recognition");
                Save(settingsWindow, $"enr-6-erkennung-fest-ausgeblendet-{label}.png");
                SettingsUi.Show(settingsWindow, "text");
                Save(settingsWindow, $"enr-7-text-fest-und-vorgabe-{label}.png");

                r.Server.Set("blocked");
                await r.Service.SyncAsync(CancellationToken.None);
                SettingsUi.Show(settingsWindow, "company");
                Save(settingsWindow, $"enr-8-seite-firma-gesperrt-{label}.png");
                settingsWindow.Close();
            }

            using (var single = new ManagementRig(uiThread: true))
            {
                var session = new SettingsViewModel(single.F.Controller, single.Secrets, new FakeAccessChecker()) { Company = single.Service };
                var settingsWindow = new SettingsWindow(new SettingsShellViewModel(session)) { Width = 980, Height = 760, RequestedThemeVariant = theme };
                settingsWindow.Show();
                SettingsUi.Show(settingsWindow, "company");
                Save(settingsWindow, $"enr-9-seite-firma-einzel-{label}.png");
                ((CompanyPageViewModel)settingsWindow.Shell!.Page("company")).OpenJoinCommand.Execute(null);
                Save(settingsWindow, $"enr-10-seite-firma-verbinden-{label}.png");
                settingsWindow.Close();
            }
        }
    }
}
