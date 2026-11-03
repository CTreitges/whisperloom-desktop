using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Styling;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Management;
using WhisperLoom.App.Tests.Settings;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Szenen des Einstellungsfensters mit Fake-Daten (nur Test-Keys <c>test-key-not-real-n</c>, Beispiel-Firma): je Seite das
/// ganze Fenster mit Seitenleiste, Höhe an den Inhalt angepasst.
/// </summary>
internal static class UxSettingsScenes
{
    public const double Width = 920;

    /// <summary>Optionen einer Szene: Einstellungen vorbereiten, Firma anbinden, nach dem Öffnen etwas tun.</summary>
    internal sealed record PageSetup(
        Func<WhisperLoomSettings>? Settings = null,
        Action<ControllerFixture>? Arrange = null,
        bool Company = false,
        bool Joined = false,
        Func<SettingsWindow, Task>? Act = null);

    public static async Task<UxScene> PageAsync(string page, ThemeVariant theme, PageSetup? setup = null)
    {
        setup ??= new PageSetup();
        var settings = setup.Settings?.Invoke() ?? new WhisperLoomSettings { Language = "de", Engine = "online" };
        ManagementRig? rig = setup.Company ? new ManagementRig(settings, uiThread: true) : null;
        var f = rig?.F ?? new ControllerFixture(settings);
        if (rig is not null && setup.Joined)
        {
            Demo(rig.Server);
            rig.Server.AutoApprove = true;
            await rig.JoinAsync();
        }
        f.Controller.Settings.Selection.FloatingButton = true;
        setup.Arrange?.Invoke(f);
        var store = new MemorySecretStore();
        store.Values[SecretNames.Stt] = "test-key-not-real-9";
        store.Values["custom:labor"] = "test-key-not-real-8";
        var vm = new SettingsViewModel(f.Controller, store, new FakeAccessChecker(), ["USB-Mikrofon"]) { Company = rig?.Service };
        await vm.LoadKeysAsync();
        SettingsShellViewModel.LastPage = page;
        var window = new SettingsWindow(new SettingsShellViewModel(vm)) { Width = Width, Height = 660, RequestedThemeVariant = theme };
        window.Show();
        SettingsUi.Show(window, page);
        if (window.Shell!.SelectedPage is ConnectionsPageViewModel providers)
        {
            await providers.LoadKeysAsync();
        }
        if (setup.Act is not null)
        {
            await setup.Act(window);
        }
        UxStage.FitHeight(window);
        SettingsShellViewModel.LastPage = "dictation";
        return new UxScene(window, window, (IDisposable?)rig ?? f);
    }

    /// <summary>Beispiel-Firma wie in den Firmen-Screenshots: Sprache und Stufe Fest, „immer kopieren“ als Vorgabe.</summary>
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

    /// <summary>Firma mit festen Erkennungs-Zeilen und einer Vorgabe (Verwaltet-Zeile, Etikett „Firmenvorgabe“).</summary>
    public static void Managed(ControllerFixture f)
    {
        f.Controller.Settings.Management.TenantName = "Beispiel GmbH";
        f.Controller.Settings.Stt.Model = "gpt-transcribe";
        f.Controller.Policy = new PolicySettings
        {
            Locked = ["stt.providerId", "secrets.stt", "stt.baseUrl", "stt.readTimeoutSec"],
            Defaults = new Dictionary<string, JsonElement>
            {
                ["stt.model"] = JsonSerializer.SerializeToElement("gpt-transcribe"),
            },
        };
    }

    /// <summary>Eigener Anbieter, eigenes Whisper-Modell und lokaler Server (Seiten „Anbieter“ und „Lokale Modelle“).</summary>
    public static WhisperLoomSettings LocalDemo()
    {
        var s = new WhisperLoomSettings { Language = "de", Engine = "offline" };
        s.Providers.Custom.Add(new CustomProvider
        {
            Id = "labor", Name = "Labor-Server", Protocol = "openai", BaseUrl = "https://llm.labor.example/v1", Models = ["whisper-large-v3"],
        });
        s.Offline.CustomModels.Add(new CustomWhisperModel { Name = "whisper-de-finetune", Path = "D:/Modelle/ggml-de-finetune.bin" });
        LocalSettings.Set(s, new LocalSettings
        {
            TextEngine = "bundled",
            Servers = [new LocalServer { Name = "LM Studio", Kind = "text", BaseUrl = "http://127.0.0.1:1234/v1", Model = "qwen2.5-7b-instruct" }],
        });
        return s;
    }

    /// <summary>Zwei App-Profile wie in Christofs Vorlage (Terminal mit Prompt, Outlook mit Verschönern).</summary>
    public static WhisperLoomSettings WithProfiles()
    {
        var s = new WhisperLoomSettings { Language = "de", Engine = "online" };
        var desktop = SettingsStore.Desktop(s);
        desktop.Profiles.Add(new AppProfile { Name = "Terminal", Process = "WindowsTerminal.exe", Stage = "prompt", PressEnter = false });
        desktop.Profiles.Add(new AppProfile { Name = "Outlook", Process = "OUTLOOK.EXE", Stage = "beautify", PressEnter = false });
        SettingsStore.SetDesktop(s, desktop);
        return s;
    }
}
