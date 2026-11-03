using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Management;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// Einstellungen „Markierung verarbeiten“ (W2-SEL): Gruppe mit den drei Startern auf der Seite Tastenkürzel, jeder einzeln
/// schaltbar und gespeichert; Firmen-Fest blendet die Maus-Starter aus und installiert bei Fest-aus keinen Maus-Hook.
/// </summary>
public sealed class SelectionSettingsTest
{
    private static SettingsWindow Open(ControllerFixture f) => Open(f, out _);

    private static SettingsWindow Open(ControllerFixture f, out SettingsViewModel vm)
    {
        vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        var window = new SettingsWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public async Task Gruppe_mit_drei_Startern_jeder_einzeln_schaltbar_und_gespeichert()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = Open(f, out var vm);
        SettingsUi.Show(window, "dictation");

        var group = window.GetVisualDescendants().OfType<SettingsGroup>().Single(g => g.TitleKey == "G_Selection");
        Assert.True(group.IsShown);
        if (!OperatingSystem.IsWindows())
        {
            // Linux: statt der drei Windows-Starter der Befehl für ein Kürzel der Desktop-Umgebung.
            Assert.EndsWith("process-selection", SettingsUi.Named<SelectableTextBlock>(window, "SelectionCommand").Text);
            window.Close();
            return;
        }
        foreach (var path in new[] { "selection.hotkeyCombo", "selection.floatingButton", "selection.ctrlRightClick" })
        {
            Assert.True(SettingsUi.Row(window, path).IsShown, path);
        }
        Assert.Equal(SelectionSettings.DefaultHotkey, SettingsUi.Named<TextBlock>(window, "SelectionHotkeyValue").Text);
        Assert.False(SettingsUi.Named<ToggleSwitch>(window, "FloatingButtonToggle").IsChecked); // ab Werk aus
        Assert.False(SettingsUi.Named<ToggleSwitch>(window, "CtrlRightClickToggle").IsChecked);

        SettingsUi.Named<ToggleSwitch>(window, "FloatingButtonToggle").IsChecked = true;
        SettingsUi.Named<ToggleSwitch>(window, "CtrlRightClickToggle").IsChecked = true;
        SettingsUi.Named<ToggleSwitch>(window, "SelectionHotkeyToggle").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        await vm.Save();

        var saved = f.Store.Load().Selection;
        Assert.Equal(("", true, true), (saved.HotkeyCombo, saved.FloatingButton, saved.CtrlRightClick));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Profil_kann_den_Knopf_abschalten_Erben_wird_nicht_geschrieben()
    {
        using var f = new ControllerFixture();
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        vm.Profiles.Suggestions[0].AddCommand.Execute(null);
        vm.Profiles.Suggestions[0].AddCommand.Execute(null);
        Assert.True(vm.Profiles.Rows[0].ShowFloatingButton); // Wie allgemein
        vm.Profiles.Rows[1].ShowFloatingButton = false; // Schalter „Schwebenden Knopf zeigen“ aus = hier nie

        await vm.Save();

        var saved = SettingsStore.Desktop(f.Store.Load()).Profiles;
        Assert.Equal([null, false], saved.Select(p => p.FloatingButton));
        var profiles = JsonNode.Parse(File.ReadAllText(f.Store.FilePath))!["desktop"]!["profiles"]!.AsArray();
        Assert.Equal([false, true], profiles.Select(p => p!.AsObject().ContainsKey("floatingButton")));
    }

    [AvaloniaFact]
    public void Abgeschaltete_Kombi_zaehlt_nicht_als_Konflikt()
    {
        var desktop = new DesktopSettings { HotkeyReinsert = "Ctrl+Alt+Shift+S" };
        var vm = new HotkeysViewModel(desktop, null, new SelectionSettings());
        Assert.NotEqual("", vm.Selection.Warnings);

        vm.SelectionOn = false;
        Assert.Equal("", vm.Reinsert.Warnings);
        vm.Apply();
    }

    [AvaloniaFact]
    public void Firmen_Fest_blendet_die_Maus_Starter_aus_die_Kombi_bleibt()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Management.TenantName = "Beispiel GmbH";
        f.Controller.Policy = new PolicySettings { Locked = ["selection"] };
        var window = Open(f);
        SettingsUi.Show(window, "dictation");

        Assert.False(SettingsUi.Row(window, "selection.floatingButton").IsShown);
        Assert.False(SettingsUi.Row(window, "selection.ctrlRightClick").IsShown);
        Assert.True(SettingsUi.Row(window, "selection.hotkeyCombo").IsShown); // Tastenkürzel sind nie Fest
        window.Close();
    }

    /// <summary>Befund H2 (WSLg): unter Linux stand „Tastenkürzel dafür … Ändern“ ohne Wirkung – nur zeigen, was gilt.</summary>
    [AvaloniaFact]
    public void Linux_zeigt_keine_Kuerzel_Zeile_der_Markierung()
    {
        var windows = new HotkeysViewModel(new DesktopSettings(), null, new SelectionSettings()) { Windows = true };
        Assert.True(windows.SelectionOn);
        Assert.True(windows.ShowSelectionCombo);
        var linux = new HotkeysViewModel(new DesktopSettings(), null, new SelectionSettings()) { Windows = false };
        Assert.True(linux.SelectionOn);
        Assert.False(linux.ShowSelectionCombo);

        var raised = new List<string?>();
        windows.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        windows.SelectionOn = false;
        Assert.False(windows.ShowSelectionCombo);
        Assert.Contains(nameof(HotkeysViewModel.ShowSelectionCombo), raised);
    }

    /// <summary>Befund H2 (WSLg), Prüfer-Fund UX-5: die Zeile auf der Seite hängt an <c>ShowSelectionCombo</c>, nicht an SelectionOn.</summary>
    [AvaloniaFact]
    public void Seite_blendet_die_Kuerzel_Zeile_unter_Linux_aus()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = Open(f, out var vm);
        SettingsUi.Show(window, "dictation");
        var row = SettingsUi.Rows(window).Single(r => r.TitleKey == "Hotkey_SelectionCombo");
        vm.Hotkeys.Windows = true; // Ausgangslage unabhängig vom Betriebssystem (CI läuft auch auf Linux; Prüfer-Fund UX-5)
        Dispatcher.UIThread.RunJobs();
        Assert.True(row.IsVisible);

        vm.Hotkeys.Windows = false; // wie unter Linux, SelectionOn bleibt an
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Hotkeys.SelectionOn);
        Assert.False(row.IsVisible);
        window.Close();
    }

    /// <summary>Befund H2 (UIA-Dump): die Liste der Tastenkürzel war die einzige unbenannte Stelle.</summary>
    [AvaloniaFact]
    public void Liste_der_Tastenkuerzel_hat_einen_Namen()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = Open(f);
        SettingsUi.Show(window, "dictation");
        var rows = SettingsUi.Named<ItemsControl>(window, "HotkeyRows");
        Assert.Equal(Loc.Instance["G_HotkeysMain"], Avalonia.Automation.AutomationProperties.GetName(rows));
        window.Close();
    }

    public enum Source
    {
        Server,
        File,
    }

    /// <summary>Firma setzt beide Maus-Starter Fest auf aus, der Nutzer hatte sie lokal an: der Maus-Hook wird nie installiert.</summary>
    [Theory]
    [InlineData(Source.File)]
    [InlineData(Source.Server)]
    public async Task Fest_aus_installiert_den_Maus_Hook_gar_nicht(Source source)
    {
        var local = new WhisperLoomSettings();
        local.Selection.FloatingButton = true;
        local.Selection.CtrlRightClick = true;
        using var r = new ManagementRig(local);
        var settings = new JsonObject { ["selection"] = new JsonObject { ["floatingButton"] = false, ["ctrlRightClick"] = false } };
        var levels = new JsonObject { ["selection.floatingButton"] = "fixed", ["selection.ctrlRightClick"] = "fixed" };
        if (source == Source.Server)
        {
            r.Server.AutoApprove = true;
            r.Server.Settings = settings;
            r.Server.Levels = levels;
            await r.JoinAsync();
        }
        else
        {
            var file = (JsonObject)settings.DeepClone();
            file["policy"] = new JsonObject { ["levels"] = levels.DeepClone() };
            var path = Path.Combine(r.F.DataDir, "provision.json");
            File.WriteAllText(path, file.ToJsonString());
            r.F.Store.Save(r.Settings);
            Assert.Equal(0, await new SyncCommands(r.F.DataDir, r.User, TextWriter.Null, TextWriter.Null).RunAsync("provision", path, false, CancellationToken.None));
            r.F.Controller.Policy = r.Policies.Load();
            await r.F.Controller.ApplySettingsAsync(r.F.Store.Load());
        }
        // Auch eine lokal wieder eingeschaltete settings.json setzt Fest nicht außer Kraft (EnforceFixed beim Laden).
        var tampered = r.F.Store.Load();
        tampered.Selection.FloatingButton = true;
        var (enforced, _, _) = r.Policies.EnforceFixed(tampered, r.Policies.Load());

        var created = 0;
        using var mouse = new MouseHookSwitch(() =>
        {
            created++;
            throw new InvalidOperationException("Hook darf nicht entstehen");
        });
        mouse.Apply(r.F.Controller.Settings.Selection);
        mouse.Apply(enforced.Selection);

        Assert.Equal(0, created);
        Assert.False(mouse.Installed);
        Assert.Equal(ManagedState.Fixed, r.F.Controller.Policy.StateOf("selection.floatingButton"));
    }

    [Fact]
    public void Firma_kann_die_Kombi_nicht_Fest_setzen()
    {
        Assert.False(ManagedPaths.CanBeFixed("selection.hotkeyCombo"));
        Assert.True(ManagedPaths.CanBeFixed("selection.floatingButton"));
        Assert.True(ManagedPaths.CanBeFixed("selection.ctrlRightClick"));
    }

    // --- Controller ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Controller_verarbeitet_mit_Stufe_behaelt_Randleerraum_und_schreibt_Verlauf_mit_Quelle()
    {
        using var f = new ControllerFixture();
        Assert.Equal([RefineMode.Polish, RefineMode.Beautify, RefineMode.Summarize], f.Controller.SelectionStages());

        Assert.Equal("done", await f.Controller.ProcessSelectionAsync("  Zeile eins\r\n", "polish"));

        Assert.Equal(["  [polish] Zeile eins\r\n"], f.Sink.Written);
        var entry = Assert.Single(await f.History.ListAsync(CancellationToken.None));
        Assert.Equal(HistoryEntry.SourceSelection, entry.Source);
        Assert.Equal("", f.Controller.LastResult); // fremder Text gehört nicht in „erneut einfügen“
        Assert.DoesNotContain("Zeile eins", f.LogText);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Controller.ProcessSelectionAsync("x", "off"));
    }

    [Fact]
    public async Task Ohne_Verlauf_kein_Eintrag()
    {
        using var f = new ControllerFixture(new WhisperLoomSettings { History = new HistorySettings { Enabled = false } });
        Assert.Equal("done", await f.Controller.ProcessSelectionAsync("x", "beautify"));
        Assert.Empty(await f.History.ListAsync(CancellationToken.None));
    }

    [Fact]
    public void Randleerraum()
    {
        Assert.Equal(" B\n", DictationController.KeepEdges(" a\n", "B"));
        Assert.Equal("B", DictationController.KeepEdges("a", "  B  "));
        Assert.Equal("B", DictationController.KeepEdges("   ", "B"));
    }

    // --- CLI ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void CLI_process_selection_mit_und_ohne_Stufe()
    {
        Assert.Equal(("process-selection", 0), (CommandLine.Parse(["process-selection"]).Verb, CommandLine.Parse(["process-selection"]).Args.Count));
        Assert.Equal(["beautify"], CommandLine.Parse(["process-selection", "--stage", "beautify"]).Args);
        Assert.Throws<UsageException>(() => CommandLine.Parse(["toggle", "--stage", "beautify"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["process-selection", "--stage"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["process-selection", "extra"]));
    }

    [AvaloniaFact]
    public async Task Verb_ohne_Markierungsweg_meldet_unavailable()
    {
        using var f = new ControllerFixture();
        var host = new AppHost(new AppOptions(Path.Combine(f.DataDir, "host")), new MemorySecretStore(), _ => f.Pipeline, f.Recorder, f.Sink);
        var response = await host.ExecuteAsync("process-selection", []);
        Assert.False(response.Ok);
        Assert.Equal("unavailable", response.Message);

        host.ProcessSelection = (stage, tray) => Task.FromResult($"{stage}|{tray}");
        Assert.Equal("beautify|False", (await host.ExecuteAsync("process-selection", ["beautify"])).Message);
    }
}
