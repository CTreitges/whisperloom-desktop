using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Setup;
using WhisperLoom.App.Tests.Settings;
using WhisperLoom.App.Tests.Setup;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.App.Views.Help;

namespace WhisperLoom.App.Tests.Help;

/// <summary>Einstiege in die Hilfe (W2-HELP Nr. 4): „?“ je Einstellungsseite, F1 nach Kontext, Assistent.</summary>
public sealed class HelpEntryTest : IDisposable
{
    private readonly List<string> _opened = [];
    private readonly Action<string>? _previous = HelpLauncher.Handler;

    public HelpEntryTest()
    {
        Loc.Instance.SetLanguage("de");
        HelpLauncher.Install();
        HelpLauncher.Handler = _opened.Add;
    }

    public void Dispose() => HelpLauncher.Handler = _previous;

    private static SettingsWindow Settings(ControllerFixture f)
    {
        SettingsShellViewModel.LastPage = "dictation";
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Question_mark_on_every_settings_page_opens_its_section()
    {
        using var f = new ControllerFixture();
        var window = Settings(f);
        foreach (var page in SettingsUi.PageIds)
        {
            var view = SettingsUi.Show(window, page);
            var help = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Help");
            Assert.True(help.IsEffectivelyVisible, page);
            Assert.Equal(Loc.Instance.Format("Help_PageButton_Name", Loc.Instance["Page_" + page]), AutomationProperties.GetName(help));
            Assert.Equal(Loc.Instance["Help_PageButton_Tip"], ToolTip.GetTip(help));
            help.Command!.Execute(null);
            Assert.Equal(HelpCatalog.PageTopics[page], _opened[^1]);
        }
        Assert.Equal(SettingsUi.PageIds.Count, _opened.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void F1_in_settings_opens_the_section_of_the_current_page()
    {
        using var f = new ControllerFixture();
        var window = Settings(f);
        SettingsUi.Show(window, "connections");
        window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Assert.Equal(["providers"], _opened);
        SettingsUi.Show(window, "text");
        window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Assert.Equal("stages", _opened[^1]);
        // Nur F1 ohne Modifier.
        window.KeyPress(Key.F1, RawInputModifiers.Shift, PhysicalKey.F1, null);
        Assert.Equal(2, _opened.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void F1_in_the_wizard_follows_the_step_and_the_help_link_too()
    {
        using var s = new SetupFixture();
        var window = new SetupWindow(s.Vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Assert.Equal(HelpCatalog.TopicForSetup(s.Vm.Current), _opened[^1]);

        while (s.Vm.Current != SetupStep.Recognition)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();
        var link = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "HelpButton");
        Assert.True(link.IsEffectivelyVisible);
        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("recognition", _opened[^1]);
        window.Close();
    }

    [AvaloniaFact]
    public void F1_on_the_stage_opens_dictating_and_does_nothing_inside_the_help()
    {
        using var f = new ControllerFixture();
        var stage = new StageWindow(new StageViewModel(f.Controller));
        stage.Show();
        stage.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Assert.Equal(["dictation"], _opened);
        stage.Close();

        var help = new HelpWindow(new HelpViewModel());
        help.Show();
        help.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Assert.Single(_opened);
        Assert.Equal(HelpCatalog.Overview, HelpLauncher.TopicFor(new Window()));
        help.Close();
    }

    [AvaloniaFact]
    public void Without_handler_nothing_happens()
    {
        HelpLauncher.Handler = null;
        HelpLauncher.Open("start");
        HelpLauncher.OpenForPage(null);
        Assert.Empty(_opened);
    }
}
