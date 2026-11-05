using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Setup;
using WhisperLoom.App.Tests.Ux;
using static WhisperLoom.App.Tests.Setup.SetupUiTest;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>
/// Fertig-Seite „So geht's weiter“ (Goal 2026-11-05-wld-tray-hinweis): Windows mit Taskleisten-Skizze, drei Punkte (Tray-Ort mit ^,
/// Links-/Rechtsklick, Autostart je nach Schalter bzw. Verfügbarkeit), Linux neutral ohne Skizze, Sprachwechsel, 200 % Textgröße.
/// </summary>
public sealed class DoneGuideTest
{
    private static SetupWindow AtDone(SetupFixture s, bool? autostart = null)
    {
        if (autostart is { } on)
        {
            s.Vm.AutostartEnabled = on;
        }
        var window = Show(s.Vm);
        while (s.Vm.Current != SetupStep.Done)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer) =>
        peer.GetChildren().SelectMany(c => Descendants(c).Prepend(c));

    private static string Text(Window window, string name) => Named<TextBlock>(window, name).Text ?? "";

    private static void AssertPoints(Window window, string tray, string autostart)
    {
        Assert.True(Shown(Named<Control>(window, "DoneGuide")));
        Assert.Equal(Loc.Instance["Setup_DoneGuideTitle"], Text(window, "DoneGuideTitle"));
        Assert.Equal(Loc.Instance[tray], Text(window, "DoneGuideTray"));
        var clicks = tray.EndsWith("Linux", StringComparison.Ordinal) ? "Setup_DoneGuideClicksLinux" : "Setup_DoneGuideClicksWindows";
        Assert.Equal(Loc.Instance[clicks], Text(window, "DoneGuideClicks"));
        Assert.Equal(Loc.Instance[autostart], Text(window, "DoneGuideAutostart"));
        foreach (var name in new[] { "DoneGuideTitle", "DoneGuideTray", "DoneGuideClicks", "DoneGuideAutostart" })
        {
            Assert.True(Shown(Named<TextBlock>(window, name)), name);
            Assert.False(Text(window, name).StartsWith('!'), $"{name}: Schlüssel fehlt ({Text(window, name)})"); // Loc: „!Schlüssel!“
        }
    }

    [AvaloniaFact]
    public void DoneGuide_Windows_Autostart_an_zeigt_Skizze_und_die_drei_Punkte()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = AtDone(s, autostart: true);

        AssertPoints(window, "Setup_DoneGuideTrayWindows", "Setup_DoneGuideAutostartOnWindows");
        Assert.Contains("^", Text(window, "DoneGuideTray"));
        Assert.Contains("Linksklick", Text(window, "DoneGuideClicks"));
        Assert.Contains("Rechtsklick", Text(window, "DoneGuideClicks"));
        Assert.Contains("Einstellungen", Text(window, "DoneGuideClicks"));
        Assert.Contains("automatisch", Text(window, "DoneGuideAutostart"));

        var sketch = Named<Control>(window, "TaskbarSketch");
        Assert.True(Shown(sketch));
        Assert.Equal(Loc.Instance["Setup_DoneGuideSketch"], AutomationProperties.GetName(sketch));
        Assert.True(Shown(Named<Control>(window, "SketchTrayIcon")));
        Assert.True(Shown(Named<Control>(window, "SketchChevron")));

        // Screenreader: die Skizze ist ein Bild mit Beschreibung, ihre Einzelteile („^“, „12:30“) werden nicht vorgelesen.
        var peer = ControlAutomationPeer.CreatePeerForElement(sketch);
        Assert.True(peer.IsContentElement());
        Assert.Equal(AutomationControlType.Image, peer.GetAutomationControlType());
        Assert.Equal(Loc.Instance["Setup_DoneGuideSketch"], peer.GetName());
        Assert.DoesNotContain(Descendants(peer), p => p.IsContentElement() || p.IsControlElement());
        window.Close();
    }

    [AvaloniaFact]
    public void DoneGuide_Windows_Autostart_aus_nennt_Startmenue_und_Einstellungen()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = AtDone(s, autostart: false);

        AssertPoints(window, "Setup_DoneGuideTrayWindows", "Setup_DoneGuideAutostartOffWindows");
        Assert.Contains("Startmenü", Text(window, "DoneGuideAutostart"));
        Assert.Contains("Einstellungen", Text(window, "DoneGuideAutostart"));
        window.Close();
    }

    [AvaloniaFact]
    public void DoneGuide_Autostart_folgt_dem_Schalter_bis_zur_Fertig_Seite()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = AtDone(s, autostart: true);
        s.Vm.Back();
        s.Vm.AutostartEnabled = false;
        s.Vm.Next();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Instance["Setup_DoneGuideAutostartOffWindows"], Text(window, "DoneGuideAutostart"));
        window.Close();
    }

    [AvaloniaFact]
    public void DoneGuide_Autostart_nicht_verfuegbar_verweist_nicht_auf_einen_Schalter()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture(autostartAvailable: false);
        var window = AtDone(s);

        Assert.False(s.Vm.AutostartAvailable);
        AssertPoints(window, "Setup_DoneGuideTrayWindows", "Setup_DoneGuideAutostartUnavailableWindows");
        Assert.Contains("Startmenü", Text(window, "DoneGuideAutostart"));
        window.Close();
    }

    [AvaloniaFact]
    public void DoneGuide_Linux_neutral_ohne_Skizze()
    {
        Loc.Instance.SetLanguage("de");
        using (var s = new SetupFixture(windows: false))
        {
            var window = AtDone(s, autostart: true);
            AssertPoints(window, "Setup_DoneGuideTrayLinux", "Setup_DoneGuideAutostartOnLinux");
            Assert.False(Shown(Named<Control>(window, "TaskbarSketch")));
            Assert.DoesNotContain("Windows", Text(window, "DoneGuideTray") + Text(window, "DoneGuideAutostart"));
            Assert.DoesNotContain("Taskleiste", Text(window, "DoneGuideTray"));
            Assert.Contains("Anmeldung", Text(window, "DoneGuideAutostart"));
            window.Close();
        }
        using (var s = new SetupFixture(windows: false))
        {
            var window = AtDone(s, autostart: false);
            AssertPoints(window, "Setup_DoneGuideTrayLinux", "Setup_DoneGuideAutostartOffLinux");
            window.Close();
        }
        using (var s = new SetupFixture(windows: false, autostartAvailable: false))
        {
            var window = AtDone(s);
            AssertPoints(window, "Setup_DoneGuideTrayLinux", "Setup_DoneGuideAutostartUnavailableLinux");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DoneGuide_folgt_dem_Sprachwechsel()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = AtDone(s, autostart: true);
        try
        {
            Loc.Instance.SetLanguage("en");
            Dispatcher.UIThread.RunJobs();
            AssertPoints(window, "Setup_DoneGuideTrayWindows", "Setup_DoneGuideAutostartOnWindows");
            Assert.Contains("right-click", Text(window, "DoneGuideClicks"));
            Assert.Equal(Loc.Instance["Setup_DoneGuideSketch"], AutomationProperties.GetName(Named<Control>(window, "TaskbarSketch")));
        }
        finally
        {
            Loc.Instance.SetLanguage("de");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DoneGuide_bei_200_Prozent_Textgroesse_nichts_abgeschnitten()
    {
        Loc.Instance.SetLanguage("de");
        Design.TextScale.Apply(Avalonia.Application.Current!, 2.0);
        try
        {
            foreach (var windows in new[] { true, false })
            {
                using var s = new SetupFixture(windows: windows);
                var window = AtDone(s, autostart: false);
                UxStage.Settle(window);
                var guide = Named<Control>(window, "DoneGuide");
                var violations = UxRules.Clipped(window).ToList();
                Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
                Assert.True(guide.Bounds.Width > 0 && guide.Bounds.Height > 0);
                if (windows)
                {
                    // Die Skizze wächst mit: der Pfeil ^ hat die Größe von Loom.Font.Title (16 px bei 100 %).
                    Assert.Equal(32, Named<Control>(window, "SketchChevron").Bounds.Width, 1);
                }
                window.Close();
            }
        }
        finally
        {
            Design.TextScale.Apply(Avalonia.Application.Current!, 1.0);
        }
    }
}
