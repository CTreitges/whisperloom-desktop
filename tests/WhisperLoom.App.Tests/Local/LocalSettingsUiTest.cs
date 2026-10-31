using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Local;

/// <summary>Tab „Lokal“ headless: Modelle, Rechenweg, Ollama-Hinweis; Speichern schreibt Engine, Modell und Abschnitt <c>local</c>.</summary>
public sealed class LocalSettingsUiTest
{
    private static (SettingsWindow Window, SettingsViewModel Vm, ControllerFixture F) Open()
    {
        var f = new ControllerFixture();
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        var window = new SettingsWindow(vm);
        window.Show();
        var tab = window.GetLogicalDescendants().OfType<TabItem>().Single(t => t.Name == "TabLocal");
        tab.IsSelected = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, vm, f);
    }

    [AvaloniaFact]
    public void TabZeigtModelleRechenwegUndOllama()
    {
        Loc.Instance.SetLanguage("de");
        var (window, _, f) = Open();
        using (f)
        {
            var texts = UiTest.TextsOf(window).ToList();
            Assert.Contains("Modelle der lokalen Erkennung", texts);
            Assert.Contains(texts, t => t.StartsWith("Small (190 MB)", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.StartsWith("Large v3 Turbo", StringComparison.Ordinal));
            Assert.Contains("noch nicht gestartet", texts);
            Assert.Contains("Ollama-Modell", texts);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SpeichernUebernimmtLokaleEinstellungen()
    {
        Loc.Instance.SetLanguage("de");
        var (window, vm, f) = Open();
        using (f)
        {
            var local = vm.Local;
            local.Engine = local.Engines.Single(e => e.Value == "offline");
            local.SelectedModel = local.ModelChoices.Single(m => m.Value == "large-v3-turbo");
            local.Backend = local.Backends.Single(b => b.Value == "cpu");
            local.Rule = local.Rules.Single(r => r.Value == "online_local");
            local.OllamaModel = " qwen3:4b ";
            local.Accurate = false;
            await vm.Save();

            var s = f.Controller.Settings;
            Assert.Equal("offline", s.Engine);
            Assert.Equal("large-v3-turbo", s.Offline.Model);
            Assert.Equal("online_local", s.Offline.Refine);
            Assert.False(s.Offline.Accurate);
            var saved = LocalSettings.Of(s);
            Assert.Equal(("cpu", "qwen3:4b"), (saved.Backend, saved.OllamaModel));
            Assert.Equal("gemma4_e2b", s.Offline.LocalLlmModel);
            Assert.Contains("\"local\"", File.ReadAllText(f.Store.FilePath), StringComparison.Ordinal);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OhneLokaleTeileKeinDownloadUndKeinOllamaAufruf()
    {
        Loc.Instance.SetLanguage("de");
        var (window, vm, f) = Open();
        using (f)
        {
            await vm.Local.RefreshOllama();
            await vm.Local.Models[0].Download();
            Assert.False(vm.Local.OllamaMissing);
            Assert.Equal("", vm.Local.Models[0].Status);
            Assert.False(vm.Local.Models[0].Installed);
            window.Close();
        }
    }

    /// <summary>Screenshot des Tabs nur mit <c>WLD_SCREENSHOT_DIR</c> (wie <see cref="ScreenshotTest"/>).</summary>
    [AvaloniaFact]
    public void Screenshot()
    {
        var dir = Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");
        if (dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        Loc.Instance.SetLanguage("de");
        var (window, _, f) = Open();
        using (f)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            Assert.NotNull(frame);
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, "p4-settings-local.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.Close();
        }
    }

    /// <summary>Prüfer-Befund 5: Neuladen leert die Liste, die gebundene ComboBox setzt null — die Auswahl bleibt, kein Absturz.</summary>
    [AvaloniaFact]
    public async Task OllamaNeuLadenBehaeltDieAuswahlOhneOllama()
    {
        Loc.Instance.SetLanguage("de");
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var dead = $"http://127.0.0.1:{((System.Net.IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();
        var settings = new WhisperLoomSettings();
        LocalSettings.Set(settings, new LocalSettings { OllamaModel = "qwen3:4b", OllamaUrl = dead });
        using var f = new ControllerFixture(settings);
        await using var parts = new LocalParts(new Cli.AppOptions(f.DataDir), () => f.Controller.Settings, new Services.DesktopSettings());
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker(), local: parts);
        var window = new SettingsWindow(vm);
        window.Show();
        window.GetLogicalDescendants().OfType<TabItem>().Single(t => t.Name == "TabLocal").IsSelected = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        await vm.Local.RefreshOllama();
        Assert.True(vm.Local.OllamaMissing);
        Assert.Equal("qwen3:4b", vm.Local.OllamaModel);
        Assert.Contains("Ollama nicht gefunden", vm.Local.OllamaStatus, StringComparison.Ordinal);
        window.Close();
    }

    [Fact]
    public void UnbekannteWerteFallenAufStandard()
    {
        var s = new WhisperLoomSettings { Engine = null };
        s.Offline.Model = "medium";
        s.Offline.Refine = "quatsch";
        var vm = new LocalViewModel(s, null);
        Assert.Equal(("online", "small", "local", "auto"), (vm.Engine.Value, vm.SelectedModel.Value, vm.Rule.Value, vm.Backend.Value));
    }
}
