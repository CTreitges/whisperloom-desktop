using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.History;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Verlauf nach UX-3 (mockups/verlauf.html): Trefferzahl, Rückfrage mit Anzahl, Lösch-Status mit Uhrzeit, Metazeile mit
/// Stufe, „n Fassungen“, Leerzustand mit Tastenkappen, Überschrift ohne Auswahl, Werkzeugleiste nur mit Auswahl.
/// </summary>
public sealed class HistoryTextsTest : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 15, 0, 0, TimeSpan.FromHours(1));
    private readonly ControllerFixture _f = new(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "polish" } });

    public HistoryTextsTest() => _f.Now = Now;

    public void Dispose() => _f.Dispose();

    private HistoryActions Actions(IReadOnlyList<string>? keys = null) => new(
        _ => Task.FromResult(true), _ => Task.FromResult("Pasted"), null, _f.Controller.EditHistoryAsync,
        [("polish", "Glätten")], StartKeys: keys);

    private async Task<HistoryEntry> Add(string text, DateTimeOffset at, string stage = "polish")
    {
        var e = new HistoryEntry(FileHistoryStore.NewId(at), at, "de", 6200, "roh " + text, text, new Refinement(RefineModes.FromKey(stage)), "OpenAI", "gpt");
        await _f.History.AddAsync(e, CancellationToken.None);
        return e;
    }

    [AvaloniaFact]
    public async Task Suche_sagt_die_Trefferzahl_und_ohne_Treffer_den_Begriff()
    {
        Loc.Instance.SetLanguage("de");
        await Add("Einkauf Milch", Now.AddHours(-1));
        await Add("Einkauf Brot", Now.AddHours(-2));
        await Add("Meeting", Now.AddHours(-3));
        var vm = new HistoryViewModel(_f.History, Actions(), () => Now);
        await vm.LoadAsync();
        Assert.Equal("", vm.SearchSummary); // ohne Suche keine Ansage

        vm.Search = "einkauf";
        Assert.Equal("2 Treffer", vm.SearchSummary);
        vm.Search = "meeting";
        Assert.Equal("1 Treffer", vm.SearchSummary);
        vm.Search = "urlaub";
        Assert.Equal("Keine Treffer für „urlaub“.", vm.SearchSummary);
        Assert.Equal("Keine Treffer", vm.HeaderText);
        Assert.False(vm.ShowToolbar);

        vm.ClearSearchCommand.Execute(null);
        Assert.Equal("", vm.Search);
        Assert.True(vm.ShowToolbar);
    }

    [AvaloniaFact]
    public async Task Rueckfrage_nennt_die_Anzahl_und_Loeschen_die_Uhrzeit()
    {
        Loc.Instance.SetLanguage("de");
        await Add("Eins", Now.AddHours(-1));
        var vm = new HistoryViewModel(_f.History, Actions(), () => Now);
        await vm.LoadAsync();
        Assert.Equal("Das eine Diktat löschen? Das lässt sich nicht rückgängig machen.", vm.ClearQuestion);
        await Add("Zwei", Now.AddMinutes(-90));
        await vm.LoadAsync();
        Assert.Equal("Alle 2 Diktate löschen? Das lässt sich nicht rückgängig machen.", vm.ClearQuestion);

        vm.Selected = vm.Rows[0];
        var time = vm.Rows[0].Entry.CreatedAt.ToLocalTime().ToString("t", HistoryRow.Culture);
        await vm.DeleteSelected();
        Assert.Equal($"Diktat von {time} gelöscht.", vm.Status);
        Assert.True(vm.HasStatus);
        Assert.True(vm.CanUndo);
    }

    [AvaloniaFact]
    public async Task Metazeile_mit_Stufe_und_Fassungszahl()
    {
        Loc.Instance.SetLanguage("de");
        var e = await Add("Text", Now.AddHours(-1));
        var row = HistoryRow.Build([e], Now)[0];
        Assert.StartsWith("Glätten · ", row.MetaLine, StringComparison.Ordinal);
        Assert.Equal(row.StageLabel + " · " + row.Meta, row.MetaLine);
        Assert.Equal("", row.VersionsText); // nur die damalige Fassung

        await _f.Controller.EditHistoryAsync(e.Id, "polish", "Text, bearbeitet");
        var edited = (await _f.History.ListAsync(CancellationToken.None)).Single();
        Assert.Equal("bearbeitet", HistoryRow.Build([edited], Now)[0].VersionsText);
    }

    [AvaloniaFact]
    public async Task Leerzustand_zeigt_die_Tastenkuerzel_bzw_den_Linux_Satz()
    {
        Loc.Instance.SetLanguage("de");
        var windows = new HistoryViewModel(_f.History, Actions(["Ctrl+Win", "Ctrl+Space"]), () => Now);
        await windows.LoadAsync();
        Assert.True(windows.IsEmpty);
        Assert.Equal("Noch keine Diktate", windows.HeaderText);
        Assert.Equal(["Ctrl+Win", "Ctrl+Space"], windows.EmptyKeys);
        Assert.Contains("{0}", windows.EmptyTemplate, StringComparison.Ordinal);
        Assert.False(windows.ShowFoot); // keine Werkzeugleiste, kein Status

        var linux = new HistoryViewModel(_f.History, Actions(), () => Now);
        await linux.LoadAsync();
        Assert.Empty(linux.EmptyKeys);
        Assert.DoesNotContain("{0}", linux.EmptyTemplate, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Fenster_zeigt_Leerzustand_ohne_Werkzeugleiste_und_mit_Auswahl_die_Leiste()
    {
        Loc.Instance.SetLanguage("de");
        var vm = new HistoryViewModel(_f.History, Actions(["Ctrl+Win", "Ctrl+Space"]), () => Now);
        await vm.LoadAsync();
        var w = new HistoryWindow(vm);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FindControl<StackPanel>("EmptyState")!.IsVisible);
        Assert.False(w.FindControl<WrapPanel>("Toolbar")!.IsVisible);
        Assert.False(w.FindControl<Button>("ClearButton")!.IsVisible); // leerer Verlauf: nichts zu löschen

        await Add("Eins", Now.AddHours(-1));
        await vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.FindControl<StackPanel>("EmptyState")!.IsVisible);
        Assert.True(w.FindControl<WrapPanel>("Toolbar")!.IsVisible);
        Assert.True(w.FindControl<Button>("ClearButton")!.IsVisible);
        Assert.Equal(vm.Detail!.Header, w.FindControl<TextBlock>("HeaderText")!.Text);

        vm.AskClear();
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.FindControl<Button>("ClearButton")!.IsVisible);
        Assert.True(w.FindControl<Border>("ClearConfirm")!.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Tab_fuehrt_nach_der_Hilfe_erst_ins_Detail_dann_in_die_Werkzeugleiste()
    {
        // Prüfer-Fund PR #42: Der Fuß stand im Markup vor dem Inhalt; Tab sprang von „Hilfe“ direkt in die Werkzeugleiste.
        Loc.Instance.SetLanguage("de");
        await Add("Eins", Now.AddHours(-1));
        var vm = new HistoryViewModel(_f.History, Actions(), () => Now);
        await vm.LoadAsync();
        var w = new HistoryWindow(vm);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.FindControl<Button>("HelpButton")!.Focus(Avalonia.Input.NavigationMethod.Tab);

        w.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Tab, null);
        var focused = w.FocusManager?.GetFocusedElement() as Avalonia.Visual;
        Assert.NotNull(focused);
        Assert.False(w.FindControl<WrapPanel>("Toolbar")!.IsVisualAncestorOf(focused));
        Assert.IsType<ToggleButton>(focused); // erste Fassung („Ursprung“)
        w.Close();
    }

    [AvaloniaFact]
    public async Task Sprachwechsel_erneuert_Ueberschrift_und_Leerzustand_bis_zum_Schliessen()
    {
        // Prüfer-Fund PR #42: Überschrift und Leerzustand sind berechnet und folgten der Sprache nicht mehr.
        Loc.Instance.SetLanguage("de");
        var vm = new HistoryViewModel(_f.History, Actions(["Ctrl+Win", "Ctrl+Space"]), () => Now);
        await vm.LoadAsync();
        var w = new HistoryWindow(vm);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Noch keine Diktate", w.FindControl<TextBlock>("HeaderText")!.Text);

        Loc.Instance.SetLanguage("en");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Instance["History_Empty"], w.FindControl<TextBlock>("HeaderText")!.Text);
        Assert.Equal(Loc.Instance["History_EmptyHint"], w.FindControl<KeycapText>("EmptyHint")!.Template);

        w.Close();
        var raised = 0;
        vm.PropertyChanged += (_, _) => raised++;
        Loc.Instance.SetLanguage("de");
        Assert.Equal(0, raised); // geschlossen: kein Abo mehr auf Loc.Instance
    }

    [AvaloniaFact]
    public async Task Starttasten_nur_unter_Windows_mit_eingeschalteten_Tastenkuerzeln()
    {
        // Prüfer-Fund PR #42: Der Leerzustand nannte die Kürzel auch bei desktop.hotkeysEnabled = false; die Nachprüfung fand,
        // dass Windows ohne Kürzel danach den Linux-Satz („Desktop-Kürzel“) bekam.
        Loc.Instance.SetLanguage("de");
        var on = new Services.DesktopSettings { HotkeyHold = "Ctrl+Win", HotkeyToggle = "Ctrl+Space" };
        Assert.Equal(["Ctrl+Win", "Ctrl+Space"], HistoryActions.StartKeysFor(on, windows: true));
        Assert.Null(HistoryActions.StartKeysFor(on, windows: false));
        on.HotkeysEnabled = false;
        var noKeys = HistoryActions.StartKeysFor(on, windows: true);
        Assert.NotNull(noKeys);
        Assert.Empty(noKeys);

        var vm = new HistoryViewModel(_f.History, Actions(noKeys), () => Now);
        await vm.LoadAsync();
        Assert.Equal(Loc.Instance["History_EmptyHintNoKeys"], vm.EmptyTemplate);
        Assert.Contains("Tray", vm.EmptyTemplate, StringComparison.Ordinal);
    }
}
