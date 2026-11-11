using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Reinsert;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tray;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Tray-Menü nach tray.html (UX-4): zehn Einträge in fünf Gruppen, Tooltip mit der geltenden Stufe, letzte Diktate mit Wirkung
/// und Uhrzeit. Die Grundform (Reihenfolge, Befehle) prüft <c>UiTest.Tray_menu_has_all_entries</c>.
/// </summary>
public sealed class TrayMenuTest
{
    private static List<string> Headers(TrayMenu tray) =>
        [.. tray.Menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header ?? "")];

    [AvaloniaFact]
    public async Task Zehn_Eintraege_mit_Markierung_und_Erneut_senden()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture(new WhisperLoomSettings { Engine = "online" });
        var tray = new TrayMenu(f.Controller, _ => { }, onSelection: () => Task.CompletedTask);
        f.Pipeline.Failure = FakeFailure.SttNetwork;
        f.Controller.Start();
        await f.Controller.StopAsync();
        Assert.True(f.Controller.CanRetry);
        tray.Refresh();

        Assert.Equal(
            ["Aufnahme starten", "Letztes Diktat erneut senden", "Stufe: Aus", "Letzte Diktate", "Markierung verarbeiten …", "Verlauf", "Bühne",
                "Einstellungen …", "Hilfe", "WhisperLoom beenden"],
            Headers(tray));
        Assert.DoesNotContain("Einrichtung …", Headers(tray)); // nur unter Einstellungen › System (Entscheidung 20)
    }

    [AvaloniaFact]
    public void Tooltip_nennt_die_geltende_Stufe_auch_mit_Profil()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "polish" } });
        var tray = new TrayMenu(f.Controller, _ => { });
        Assert.Equal("WhisperLoom · Stufe: Glätten", tray.ToolTip);

        f.Controller.ActiveProfile = new ProfileMatch(0, new AppProfile { Name = "Outlook", Process = "OUTLOOK.EXE", Stage = "beautify" });
        Assert.Equal("WhisperLoom · Outlook · Verschönern", tray.ToolTip);
    }

    [AvaloniaFact]
    public void Letzte_Diktate_kopieren_wenn_nicht_eingefuegt_werden_kann()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var entry = Entry(new DateTimeOffset(2026, 11, 3, 14, 21, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 3, 14, 21, 0))));
        var tray = new TrayMenu(f.Controller, _ => { }, pastes: () => false);
        tray.SetRecent([entry]);
        var recent = tray.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Letzte Diktate");
        Assert.Equal("Kopieren – Das Meeting ist am Donnerstag (14:21)", recent.Menu!.Items.OfType<NativeMenuItem>().Single().Header);
        Assert.Equal("14:21", RecentHistory.Time(entry));
    }

    /// <summary>Prüfer-Fund UX-4: Die Windows-Bedienung startet nach dem Tray; ein Refresh danach nennt die echte Wirkung.</summary>
    [AvaloniaFact]
    public void Refresh_fragt_die_Wirkung_neu()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var pastes = false;
        var tray = new TrayMenu(f.Controller, _ => { }, pastes: () => pastes);
        tray.SetRecent([Entry(DateTimeOffset.Now)]);
        var recent = tray.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Letzte Diktate");
        Assert.StartsWith("Kopieren – ", recent.Menu!.Items.OfType<NativeMenuItem>().Single().Header, StringComparison.Ordinal);

        pastes = true;
        tray.Refresh();
        Assert.StartsWith("Einfügen – ", recent.Menu!.Items.OfType<NativeMenuItem>().Single().Header, StringComparison.Ordinal);
    }

    /// <summary>
    /// TS-WTRAY (W3-NEXT-Folge): Der Tray-Eintrag „Aufnahme starten“ sperrte sich, solange sein eigener Aufruf (Stopp bis zur
    /// Ausgabe) lief. Jetzt wirkt er während der Verarbeitung wie die Taste: über das echte Command-Objekt startet er die nächste
    /// Aufnahme (derselbe Einstieg <c>ToggleAsync</c>), beide Diktate kommen in Reihenfolge.
    /// </summary>
    [AvaloniaFact]
    public async Task Diktat_im_Tray_startet_waehrend_der_Verarbeitung_die_naechste_Aufnahme()
    {
        Loc.Instance.SetLanguage("de");
        var pipeline = new GatedRefinePipeline();
        using var f = new ControllerFixture(new WhisperLoomSettings
        {
            Engine = "online",
            Refine = new RefineSettings { Stage = "polish" },
            Polish = new PolishSettings { TrailingSpace = false },
        }, pipeline: pipeline);
        var tray = new TrayMenu(f.Controller, _ => { });
        var toggle = tray.Menu.Items.OfType<NativeMenuItem>().First();
        var command = toggle.Command!;

        command.Execute(null); // Aufnahme 1
        Assert.Equal(DictationState.Recording, f.Controller.State);
        f.Now += TimeSpan.FromSeconds(1);
        command.Execute(null); // Stopp: der Aufruf läuft, bis Diktat 1 ausgegeben ist
        await Waits.For(() => pipeline.Refining(1));
        Assert.Equal(DictationState.Processing, f.Controller.State);
        Assert.Equal("Aufnahme starten", toggle.Header);

        Assert.True(command.CanExecute(null)); // das Menü fragt CanExecute, bevor es den Eintrag auslöst
        command.Execute(null);
        Assert.Equal(DictationState.Recording, f.Controller.State);
        Assert.Equal(1, f.Controller.Pending);
        Assert.Contains("recording.start next=True", f.LogText, StringComparison.Ordinal);

        f.Now += DictationController.DoublePressWindow + TimeSpan.FromMilliseconds(100);
        Assert.True(command.CanExecute(null));
        command.Execute(null); // Stopp von Diktat 2
        await Waits.For(() => pipeline.Refining(2));
        pipeline.Release(1);
        pipeline.Release(2);
        await Waits.For(() => f.Controller.State == DictationState.Idle);
        Assert.Equal(["[KI] Diktat 1", "[KI] Diktat 2"], f.Sink.Written);
    }

    private static HistoryEntry Entry(DateTimeOffset at) =>
        new("1-a", at, "de", 1000, "roh", "Das Meeting ist am Donnerstag", new Refinement(RefineMode.Polish), "Fake-STT");
}
