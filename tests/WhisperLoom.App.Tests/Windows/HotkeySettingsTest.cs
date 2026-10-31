using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tray;
using WhisperLoom.App.ViewModels;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Einstellungsseite Tastenkürzel, Einfüge-Modus und Tray nach dem Speichern.</summary>
public class HotkeySettingsTest
{
    [Fact]
    public void Defaults_are_the_goal_combos_and_paste()
    {
        var d = new DesktopSettings();
        Assert.True(d.HotkeysEnabled);
        Assert.Equal("Ctrl+Win", d.HotkeyHold);
        Assert.Equal("Ctrl+Space", d.HotkeyToggle);
        Assert.Equal("Ctrl+Alt+Space", d.HotkeyStage);
        Assert.Equal("paste", d.InsertMode);
    }

    // Avalonia-Thread: Save() schaltet die globale Oberflächensprache, wie die übrigen UI-Tests nur seriell.
    [AvaloniaFact]
    public async Task Recorder_sets_value_warns_about_duplicates_and_saves()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker(),
            recordHotkey: () => Task.FromResult<string?>("Ctrl+Space"));
        Assert.Contains(Loc.Instance["Hotkey_Conflict_Autocomplete"], vm.Hotkeys.Toggle.Warnings);
        Assert.Equal("", vm.Hotkeys.Hold.Warnings);

        await vm.Hotkeys.Stage.RecordCommand.ExecuteAsync(null);
        Assert.Equal("Ctrl+Space", vm.Hotkeys.Stage.Value);
        Assert.Contains(Loc.Instance["Hotkey_Conflict_Duplicate"], vm.Hotkeys.Stage.Warnings);
        Assert.Contains(Loc.Instance["Hotkey_Conflict_Duplicate"], vm.Hotkeys.Toggle.Warnings);

        vm.InsertMode = vm.InsertModes.Single(c => c.Value == "type");
        vm.Hotkeys.Enabled = false;
        await vm.Save();
        var saved = SettingsStore.Desktop(f.Controller.Settings);
        Assert.Equal("Ctrl+Space", saved.HotkeyStage);
        Assert.Equal("type", saved.InsertMode);
        Assert.False(saved.HotkeysEnabled);
        Assert.Equal("Ctrl+Win", saved.HotkeyHold);
    }

    [AvaloniaFact]
    public async Task Cancelled_recording_keeps_the_value()
    {
        using var f = new ControllerFixture();
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker(),
            recordHotkey: () => Task.FromResult<string?>(null));
        await vm.Hotkeys.Hold.RecordCommand.ExecuteAsync(null);
        Assert.Equal("Ctrl+Win", vm.Hotkeys.Hold.Value);
        Assert.False(vm.Hotkeys.Hold.IsRecording);
    }

    [AvaloniaFact]
    public void Without_hook_there_is_no_record_button()
    {
        using var f = new ControllerFixture();
        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
        Assert.False(vm.Hotkeys.Hold.CanRecord);
    }

    [AvaloniaFact]
    public async Task Tray_offers_prompt_stage_after_enabling_it_in_settings()
    {
        // Fund aus dem Handtest (NACHTRAG): Stufe Prompt erst nach Häkchen „Stufe Prompt anbieten“.
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var tray = new TrayMenu(f.Controller, _ => { });
        var stages = (NativeMenuItem)tray.Menu.Items[1];
        Assert.DoesNotContain(stages.Menu!.Items, i => ((NativeMenuItem)i).Header == Loc.Instance["Stage_prompt"]);

        var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()) { PromptEnabled = true };
        await vm.Save();
        Assert.Contains(stages.Menu!.Items, i => ((NativeMenuItem)i).Header == Loc.Instance["Stage_prompt"]);
        Assert.True(new WhisperLoomSettings().Refine.PromptEnabled == false);
    }
}
