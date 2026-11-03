using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Reinsert;
using WhisperLoom.App.Localization;
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

    private static HistoryEntry Entry(DateTimeOffset at) =>
        new("1-a", at, "de", 1000, "roh", "Das Meeting ist am Donnerstag", new Refinement(RefineMode.Polish), "Fake-STT");
}
