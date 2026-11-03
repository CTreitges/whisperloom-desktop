using Avalonia.Headless.XUnit;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Einstellungen der Extras (WLD-P5b): Profil-Seite, neue Kürzel; Verlauf-Fenster „Neu verarbeiten“.</summary>
public sealed class SettingsExtrasTest
{
    [AvaloniaFact]
    public async Task Profiles_page_adds_suggestions_once_drops_empty_rows_and_saves()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        Assert.Empty(vm.Profiles.Rows);

        // Je fehlendem Programm ein Vorschlag (K2); übernommene verschwinden.
        vm.Profiles.Suggestions[0].AddCommand.Execute(null);
        vm.Profiles.Suggestions[0].AddCommand.Execute(null);
        Assert.Empty(vm.Profiles.Suggestions);
        Assert.Equal(["WindowsTerminal.exe", "OUTLOOK.EXE"], vm.Profiles.Rows.Select(r => r.Process));
        vm.Profiles.AddCommand.Execute(null);
        var row = vm.Profiles.Rows[^1];
        row.TitlePattern = "([";
        Assert.Contains(Loc.Instance["Profile_InvalidTitle"], row.TitleWarning);
        row.TitlePattern = "Claude";
        Assert.Equal("", row.TitleWarning);
        vm.Profiles.Rows[0].InsertMode = vm.Profiles.InsertModes.Single(c => c.Value == "type");
        vm.Profiles.Rows[0].PressEnter = true;
        vm.Profiles.Rows[1].DeleteCommand.Execute(null);
        vm.Hotkeys.Selection.Value = "Ctrl+Alt+Shift+U";

        await vm.Save();

        var saved = SettingsStore.Desktop(f.Store.Load());
        var profile = Assert.Single(saved.Profiles);
        Assert.Equal(("WindowsTerminal.exe", "prompt", "type", true), (profile.Process, profile.Stage, profile.InsertMode, profile.PressEnter));
        Assert.Equal("Ctrl+Alt+Shift+U", f.Store.Load().Selection.HotkeyCombo);
        Assert.Equal("Ctrl+Alt+Shift+V", saved.HotkeyReinsert);
    }

    [AvaloniaFact]
    public void Settings_window_shows_the_profiles_tab_with_rows()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var desktop = SettingsStore.Desktop(f.Controller.Settings);
        desktop.Profiles.Add(new AppProfile { Process = "OUTLOOK.EXE", Stage = "beautify" });
        SettingsStore.SetDesktop(f.Controller.Settings, desktop);
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        var window = new SettingsWindow(vm);
        window.Show();

        var row = Assert.Single(vm.Profiles.Rows);
        Assert.Equal(Loc.Instance["Stage_beautify"], row.Stage.Label);
        Assert.Equal("", row.InsertMode.Value);
        Assert.Equal("Wie allgemein (zurzeit Einfügen)", row.InsertMode.Label);
        Assert.Equal(OperatingSystem.IsWindows(), vm.Profiles.Available);
        window.Close();
    }

}
