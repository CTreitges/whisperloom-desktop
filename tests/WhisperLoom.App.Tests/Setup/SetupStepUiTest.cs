using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Linux;
using WhisperLoom.App.Setup;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>Einzelne Schritte des Assistenten headless: Pegel-Test, Hotkey-Test, Linux-Anleitung, Autostart.</summary>
public sealed class SetupStepUiTest
{
    [AvaloniaFact]
    public void Pegel_Test_zeigt_den_Pegel_und_verwirft_die_Aufnahme()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        s.Vm.Next();
        Assert.Equal(SetupStep.Microphone, s.Vm.Current);
        s.Settings.Microphone = s.Settings.Microphones.Single(m => m.Value == "Headset-Mikrofon");

        s.Mic.StartCommand.Execute(null);
        Assert.True(s.Recorder.Running);
        s.Mic.Tick();
        Assert.Equal(0.42, s.Mic.Level, 3);
        Assert.Equal(Loc.Instance["Setup_MicHeard"], s.Mic.Status);
        Assert.Equal(0.42, SetupUiTest.Named<ProgressBar>(window, "MicLevelBar").Value, 3);

        s.Vm.Next(); // Verlassen des Schritts beendet den Test
        Assert.False(s.Recorder.Running);
        Assert.False(s.Mic.Running);
        Assert.Equal(0, s.Mic.Level);
        window.Close();
    }

    [AvaloniaFact]
    public void Pegel_Test_ohne_Mikrofon_meldet_den_Fehler()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        s.Recorder.Unavailable = true;
        s.Mic.Start();
        Assert.False(s.Mic.Running);
        Assert.Equal(Loc.Instance["Setup_MicError"], s.Mic.Status);
    }

    [AvaloniaFact]
    public async Task Hotkey_Test_erkennt_die_Kombi()
    {
        Loc.Instance.SetLanguage("de");
        var answers = new Queue<string?>(["Ctrl+Win", "Ctrl+Alt+K", null]);
        using var s = new SetupFixture(recordHotkey: () => Task.FromResult(answers.Dequeue()));
        var window = SetupUiTest.Show(s.Vm);
        while (s.Vm.Current != SetupStep.Hotkey)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(SetupUiTest.Named<Button>(window, "HotkeyTestButton").IsVisible);
        Assert.Equal("Ctrl+Win", SetupUiTest.Named<TextBlock>(window, "HoldCombo").Text);

        await s.Vm.Hotkey.TestCommand.ExecuteAsync(null);
        Assert.True(s.Vm.Hotkey.Recognized);
        Assert.Equal(Loc.Instance.Format("Setup_HotkeyOk", Loc.Instance["Hotkey_Hold"], "Ctrl+Win"), s.Vm.Hotkey.Status);

        await s.Vm.Hotkey.TestCommand.ExecuteAsync(null);
        Assert.False(s.Vm.Hotkey.Recognized);
        Assert.Equal(Loc.Instance.Format("Setup_HotkeyOther", "Ctrl+Alt+K"), s.Vm.Hotkey.Status);

        await s.Vm.Hotkey.TestCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["Setup_HotkeyNone"], s.Vm.Hotkey.Status);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Hotkey_Test_kennt_auch_Erneut_einfuegen_und_Markierung()
    {
        Loc.Instance.SetLanguage("de");
        var answers = new Queue<string?>(["Ctrl+Alt+Shift+V", "Ctrl+Alt+Shift+S"]);
        using var s = new SetupFixture(recordHotkey: () => Task.FromResult(answers.Dequeue()));

        await s.Vm.Hotkey.TestCommand.ExecuteAsync(null);
        Assert.True(s.Vm.Hotkey.Recognized);
        Assert.Equal(Loc.Instance.Format("Setup_HotkeyOk", Loc.Instance["Hotkey_Reinsert"], "Ctrl+Alt+Shift+V"), s.Vm.Hotkey.Status);

        await s.Vm.Hotkey.TestCommand.ExecuteAsync(null);
        Assert.True(s.Vm.Hotkey.Recognized);
        Assert.Equal(Loc.Instance.Format("Setup_HotkeyOk", Loc.Instance["Hotkey_Selection"], "Ctrl+Alt+Shift+S"), s.Vm.Hotkey.Status);
    }

    [AvaloniaFact]
    public void Linux_zeigt_statt_Hotkey_Test_die_Kuerzel_Anleitung()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture(windows: false);
        var window = SetupUiTest.Show(s.Vm);
        while (s.Vm.Current != SetupStep.Hotkey)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("\"/opt/WhisperLoom/WhisperLoom\" toggle", SetupUiTest.Named<SelectableTextBlock>(window, "ToggleCommandText").Text);
        Assert.False(SetupUiTest.Named<Button>(window, "HotkeyTestButton").IsEffectivelyVisible);
        var texts = UiTest.TextsOf(window).ToList();
        Assert.Contains(texts, t => t.Contains("KDE", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("GNOME", StringComparison.Ordinal));
        while (s.Vm.Current != SetupStep.Done)
        {
            s.Vm.Next();
        }
        Assert.Contains("toggle", s.Vm.DoneHint, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Autostart_Schalter_schreibt_und_entfernt_den_Eintrag()
    {
        Loc.Instance.SetLanguage("de");
        var dir = Path.Combine(Path.GetTempPath(), "wl-autostart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var autostart = new LinuxAutostart(dir, "/opt/WhisperLoom/WhisperLoom");
            using (var s = new SetupFixture(windows: false, autostart: autostart))
            {
                var window = SetupUiTest.Show(s.Vm);
                while (s.Vm.Current != SetupStep.Autostart)
                {
                    s.Vm.Next();
                }
                Dispatcher.UIThread.RunJobs();
                var toggle = SetupUiTest.Named<ToggleSwitch>(window, "AutostartSwitch");
                Assert.False(toggle.IsChecked);
                toggle.IsChecked = true;
                Assert.True(s.Vm.AutostartEnabled);
                s.Vm.Next();
                await s.Vm.Finish();
                Assert.True(File.Exists(autostart.FilePath));
            }
            using (var s = new SetupFixture(windows: false, autostart: autostart))
            {
                Assert.True(s.Vm.AutostartEnabled); // zeigt den echten Stand
                s.Vm.AutostartEnabled = false;
                await s.Vm.Finish();
                Assert.False(File.Exists(autostart.FilePath));
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
