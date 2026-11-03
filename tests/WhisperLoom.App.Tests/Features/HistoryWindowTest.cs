using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Features.History;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Verlauf neu (W2-OUT): Liste nach Tagen, Suche, Tastatur, Rückgängig, Bearbeiten, Neu verarbeiten als Fassung.</summary>
public sealed class HistoryWindowTest : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 15, 0, 0, TimeSpan.FromHours(1));
    private readonly ControllerFixture _f = new(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "polish" } });
    private readonly List<string> _copied = [];
    private readonly List<string> _inserted = [];

    public HistoryWindowTest()
    {
        _f.Now = Now;
    }

    public void Dispose() => _f.Dispose();

    private HistoryActions Actions(bool reprocess = true) => new(
        text =>
        {
            _copied.Add(text);
            return Task.FromResult(true);
        },
        text =>
        {
            _inserted.Add(text);
            return Task.FromResult("Pasted");
        },
        reprocess ? (id, stage) => _f.Controller.ReprocessVersionAsync(id, stage) : null,
        _f.Controller.EditHistoryAsync,
        [("off", "Aus"), ("polish", "Glätten"), ("summarize", "Zusammenfassen")]);

    private async Task<HistoryEntry> Add(string raw, string text, DateTimeOffset at, string stage = "polish")
    {
        var e = new HistoryEntry(FileHistoryStore.NewId(at), at, "de", 3000, raw, text, new Refinement(RefineModes.FromKey(stage)), "OpenAI", "gpt");
        await _f.History.AddAsync(e, CancellationToken.None);
        return e;
    }

    private async Task<HistoryViewModel> Model(bool reprocess = true)
    {
        await Add("roh eins", "Eins vorgestern.", Now.AddDays(-2));
        await Add("roh zwei", "Zwei gestern.", Now.AddDays(-1));
        await Add("roh drei", "Drei heute früh.", Now.AddHours(-3));
        await Add("roh vier", "Vier heute.", Now.AddMinutes(-5));
        var vm = new HistoryViewModel(_f.History, Actions(reprocess), () => Now);
        await vm.LoadAsync();
        return vm;
    }

    private static async Task<HistoryWindow> Show(HistoryViewModel vm)
    {
        var w = new HistoryWindow(vm);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.FindControl<ListBox>("Entries")!.Focus();
        await Settle();
        return w;
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None) =>
        w.KeyPress(key, mods, physical, null);

    [AvaloniaFact]
    public async Task Liste_nach_Tagen_neueste_zuerst()
    {
        Loc.Instance.SetLanguage("de");
        var vm = await Model();
        Assert.Equal(["Vier heute.", "Drei heute früh.", "Zwei gestern.", "Eins vorgestern."], vm.Rows.Select(r => r.Entry.Text));
        Assert.Equal([true, false, true, true], vm.Rows.Select(r => r.FirstOfDay));
        Assert.Equal(Loc.Instance["History_Today"], vm.Rows[0].DayLabel);
        Assert.Equal(Loc.Instance["History_Yesterday"], vm.Rows[2].DayLabel);
        Assert.Equal(Loc.Instance["Stage_polish"], vm.Rows[0].StageLabel);
        Assert.Equal(vm.Rows[0], vm.Selected);
    }

    [AvaloniaFact]
    public async Task Suche_findet_auch_im_Ursprung()
    {
        var vm = await Model();
        vm.Search = "ROH ZWEI";
        Assert.Equal(["Zwei gestern."], vm.Rows.Select(r => r.Entry.Text));
        vm.Search = "gibtsnicht";
        Assert.True(vm.NoMatches);
        vm.Search = "";
        Assert.Equal(4, vm.Rows.Count);
    }

    [AvaloniaFact]
    public async Task Pfeile_Enter_und_StrgC_ohne_Maus()
    {
        var vm = await Model();
        var w = await Show(vm);
        Press(w, Key.Down, physical: PhysicalKey.ArrowDown);
        await Settle();
        Assert.Equal("Drei heute früh.", vm.Selected!.Entry.Text);
        Press(w, Key.Enter, physical: PhysicalKey.Enter);
        await Settle();
        Assert.Equal(["Drei heute früh."], _inserted);
        Press(w, Key.C, RawInputModifiers.Control, PhysicalKey.C);
        await Settle();
        Assert.Equal(["Drei heute früh."], _copied);
        Assert.Equal(Loc.Instance["History_Status_copied"], vm.Status);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Entf_loescht_und_StrgZ_holt_zurueck()
    {
        var vm = await Model();
        var w = await Show(vm);
        Press(w, Key.Delete, physical: PhysicalKey.Delete);
        await Settle();
        Assert.Equal(3, vm.Rows.Count);
        Assert.True(vm.CanUndo);
        Assert.Equal("Drei heute früh.", vm.Selected!.Entry.Text);
        Assert.Equal(3, (await _f.History.ListAsync(CancellationToken.None)).Count);

        Press(w, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
        await Settle();
        Assert.Equal(4, vm.Rows.Count);
        Assert.False(vm.CanUndo);
        Assert.Equal("Vier heute.", vm.Selected!.Entry.Text);
        Assert.Equal(4, (await _f.History.ListAsync(CancellationToken.None)).Count);
        w.Close();
    }

    [AvaloniaFact]
    public async Task F2_bearbeitet_StrgEnter_speichert_Ursprung_bleibt()
    {
        var vm = await Model();
        var w = await Show(vm);
        Press(w, Key.F2, physical: PhysicalKey.F2);
        await Settle();
        Assert.True(vm.Detail!.IsEditing);
        var box = w.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EditBox");
        Assert.True(box.IsFocused);
        vm.Detail.EditText = "Vier, von Hand.";
        Press(w, Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter);
        await Settle();
        Assert.False(vm.Detail.IsEditing);
        var stored = (await _f.History.GetAsync(vm.Selected!.Entry.Id, CancellationToken.None))!;
        Assert.Equal("Vier, von Hand.", stored.Text);
        Assert.Equal("roh vier", stored.Raw);
        Assert.True(HistoryVersions.Find(stored, "polish")!.Edited);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Esc_ohne_Aenderung_beendet_die_Bearbeitung_sofort()
    {
        var vm = await Model();
        var w = await Show(vm);
        Press(w, Key.F2, physical: PhysicalKey.F2);
        await Settle();
        Press(w, Key.Escape, physical: PhysicalKey.Escape);
        await Settle();
        Assert.False(vm.Detail!.IsEditing);
        Assert.False(vm.Detail.DiscardPending);
        w.Close();
    }

    /// <summary>UX-6 N14: Esc mit geändertem Text fragt erst; „Weiter bearbeiten“ ist der Standard, Verwerfen nur ausdrücklich.</summary>
    [AvaloniaFact]
    public async Task Esc_mit_Aenderung_fragt_vor_dem_Verwerfen()
    {
        var vm = await Model();
        var w = await Show(vm);
        Press(w, Key.F2, physical: PhysicalKey.F2);
        await Settle();
        vm.Detail!.EditText = "verworfen?";
        Press(w, Key.Escape, physical: PhysicalKey.Escape);
        await Settle();
        Assert.True(vm.Detail.IsEditing); // nichts still verloren
        Assert.True(vm.Detail.DiscardPending);
        Assert.True(w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "KeepEditButton").IsFocused);

        vm.Detail.KeepEditingCommand.Execute(null);
        Assert.False(vm.Detail.DiscardPending);
        Assert.Equal("verworfen?", vm.Detail.EditText);

        vm.Detail.CancelEditCommand.Execute(null); // „Abbrechen“ fragt genauso
        Assert.True(vm.Detail.DiscardPending);
        vm.Detail.DiscardEditCommand.Execute(null);
        Assert.False(vm.Detail.IsEditing);
        Assert.False(vm.Detail.DiscardPending);
        Assert.Equal("Vier heute.", (await _f.History.GetAsync(vm.Selected!.Entry.Id, CancellationToken.None))!.Text);
        w.Close();
    }

    /// <summary>UX-6 N14/N22 (Nachprüfung): auch Fassungs- und Eintragswechsel fragen; während der Rückfrage kein „Speichern“.</summary>
    [AvaloniaFact]
    public async Task Wechsel_waehrend_geaenderter_Bearbeitung_fragt_erst()
    {
        var vm = await Model();
        var w = await Show(vm);
        var detail = vm.Detail!;
        var entry = vm.Selected!;
        detail.BeginEdit();
        detail.EditText = "Entwurf";
        Assert.True(detail.ShowEditActions);

        var origin = detail.Chips.Single(c => c.Key == VersionChip.Origin);
        detail.SelectCommand.Execute(origin);
        Assert.True(detail.DiscardPending);
        Assert.True(detail.IsEditing);
        Assert.NotEqual(origin, detail.SelectedChip); // Fassung bleibt, bis verworfen wird
        Assert.False(detail.ShowEditActions);

        vm.Selected = vm.Rows.First(r => r != entry);
        await Settle();
        Assert.Same(entry, vm.Selected); // Auswahl springt zurück
        Assert.Same(detail, vm.Detail);
        Assert.Equal("Entwurf", detail.EditText);

        detail.DiscardEditCommand.Execute(null);
        detail.SelectCommand.Execute(origin);
        Assert.Equal(origin, detail.SelectedChip);
        w.Close();
    }

    /// <summary>Prüfer Runde 2: ↓ in der Liste während einer geänderten Bearbeitung – Auswahl zurück, Fokus auf „Weiter bearbeiten“.</summary>
    [AvaloniaFact]
    public async Task Pfeil_in_der_Liste_fragt_und_fokussiert_die_Rueckfrage()
    {
        var vm = await Model();
        var w = await Show(vm);
        var entry = vm.Selected!;
        Press(w, Key.F2, physical: PhysicalKey.F2);
        await Settle();
        vm.Detail!.EditText = "Entwurf";
        var list = w.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "Entries");
        (list.ContainerFromItem(entry) as Control)!.Focus();
        Press(w, Key.Down, physical: PhysicalKey.ArrowDown);
        await Settle();
        Assert.Equal(entry.Entry.Id, vm.Selected!.Entry.Id);
        Assert.True(vm.Detail!.DiscardPending);
        Assert.Equal("Entwurf", vm.Detail.EditText);
        Assert.True(w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "KeepEditButton").IsFocused);
        w.Close();
    }

    /// <summary>Prüfer Runde 2: ein abgelehnter Klick auf einen Fassungs-Chip lässt ihn nicht gedrückt stehen.</summary>
    [AvaloniaFact]
    public async Task Abgelehnter_Chip_bleibt_nicht_gedrueckt()
    {
        var vm = await Model();
        var w = await Show(vm);
        vm.Detail!.BeginEdit();
        vm.Detail.EditText = "Entwurf";
        await Settle();
        var origin = w.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .Single(t => t.DataContext is VersionChip { Key: VersionChip.Origin });
        origin.SetCurrentValue(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, true); // wie ToggleButton.OnClick
        origin.Command!.Execute(origin.CommandParameter);
        await Settle();
        Assert.True(vm.Detail.DiscardPending);
        Assert.False(origin.IsChecked);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Suche_filtert_einen_geaenderten_Entwurf_nicht_weg()
    {
        var vm = await Model();
        var detail = vm.Detail!;
        var id = detail.Entry.Id;
        detail.BeginEdit();
        detail.EditText = "Entwurf";
        vm.Search = "zzz-kein-treffer";
        await Settle();
        Assert.Contains(vm.Rows, r => r.Entry.Id == id);
        Assert.Same(detail, vm.Detail);
        Assert.Equal("Entwurf", vm.Detail!.EditText);

        detail.DiscardEdit();
        vm.Search = "zzz-kein-treffer-2";
        await Settle();
        Assert.DoesNotContain(vm.Rows, r => r.Entry.Id == id); // ohne Entwurf filtert die Suche wieder normal
    }

    /// <summary>Prüfer Runde 3: Trefferzahl zählt den Entwurf nicht; Speichern während der Suche lässt ihn gewählt.</summary>
    [AvaloniaFact]
    public async Task Entwurf_in_der_Suche_zaehlt_nicht_und_bleibt_nach_dem_Speichern()
    {
        var vm = await Model();
        var detail = vm.Detail!;
        var id = detail.Entry.Id;
        detail.BeginEdit();
        detail.EditText = "Entwurf";
        vm.Search = "zzz";
        await Settle();
        Assert.Equal(Loc.Instance.Format("History_NoMatchesFor", "zzz"), vm.SearchSummary);

        await detail.SaveEdit();
        await Settle();
        Assert.Equal(id, vm.Selected!.Entry.Id);
        Assert.Equal("Entwurf", vm.Detail!.Text);
    }

    /// <summary>Nachprüfung 3 (N14): während „Neu verarbeiten“ läuft, beginnt keine Bearbeitung; mit Entwurf startet keine Rechnung.</summary>
    [AvaloniaFact]
    public async Task Bearbeiten_und_Neu_verarbeiten_schliessen_sich_aus()
    {
        var vm = await Model();
        var detail = vm.Detail!;
        detail.BeginEdit();
        detail.EditText = "Entwurf";
        await detail.RequestReprocessAsync("summarize");
        Assert.True(detail.DiscardPending);
        Assert.Null(detail.RunningStage);
        Assert.Equal("Entwurf", detail.EditText);

        detail.DiscardEdit();
        detail.RunningStage = "summarize"; // Rechnung läuft
        detail.BeginEdit();
        Assert.False(detail.IsEditing);
    }

    [Theory]
    [InlineData(false, WindowCloseReason.WindowClosing, true, true)]
    [InlineData(false, WindowCloseReason.WindowClosing, false, false)]
    [InlineData(true, WindowCloseReason.WindowClosing, true, false)]
    [InlineData(false, WindowCloseReason.ApplicationShutdown, true, false)]
    [InlineData(false, WindowCloseReason.OSShutdown, true, false)]
    public void Schliessen_fragt_nur_den_Nutzer_mit_Entwurf(bool programmatic, WindowCloseReason reason, bool changed, bool asks) =>
        Assert.Equal(asks, HistoryWindow.ShouldAskOnClose(programmatic, reason, changed));

    [AvaloniaFact]
    public async Task Neuaufbau_schliesst_eine_offene_Rueckfrage()
    {
        var vm = await Model();
        var detail = vm.Detail!;
        detail.BeginEdit();
        detail.EditText = "Entwurf";
        detail.CancelEdit();
        Assert.True(detail.DiscardPending);
        detail.SelectedChip = detail.Chips.Single(c => c.Key == VersionChip.Origin); // wie Rebuild nach „Neu verarbeiten“
        Assert.False(detail.DiscardPending);
        Assert.False(detail.IsEditing);
    }

    /// <summary>UX-6 N15/N16: „Löschen“ ist ein eigener Knopf (kein Ein-Eintrag-Menü), der Stufen-Wähler zeigt seine Tasten.</summary>
    [AvaloniaFact]
    public async Task Loeschen_als_Knopf_und_Tastenzeile_am_Waehler()
    {
        var vm = await Model();
        var w = await Show(vm);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), b => b.Name == "MoreButton");
        var delete = w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeleteButton");
        Assert.True(delete.IsEffectivelyVisible);
        delete.Command!.Execute(null);
        await Settle();
        Assert.Equal(3, vm.Rows.Count);
        Assert.True(vm.CanUndo);

        w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReprocessButton").Command!.Execute(null);
        await Settle();
        var keys = w.GetVisualDescendants().OfType<WhisperLoom.App.Design.KeycapText>().Single(k => k.Name == "ReprocessKeys");
        Assert.True(keys.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Ursprung_bearbeiten_ueberschreibt_den_Rohtext_nie()
    {
        var vm = await Model();
        var detail = vm.Detail!;
        detail.SelectCommand.Execute(detail.Chips.Single(c => c.Key == VersionChip.Origin));
        Assert.Equal("roh vier", detail.Text);
        detail.BeginEdit();
        detail.EditText = "Rohtext korrigiert";
        await detail.SaveEdit();
        var stored = (await _f.History.GetAsync(detail.Entry.Id, CancellationToken.None))!;
        Assert.Equal("roh vier", stored.Raw);
        Assert.Equal("Rohtext korrigiert", HistoryVersions.Find(stored, HistoryVersions.Edited)!.Text);
        Assert.Equal("Rohtext korrigiert", detail.Text);
        Assert.Contains(Loc.Instance["History_Edited"], detail.Chips[0].Label);
    }

    [AvaloniaFact]
    public async Task Neu_verarbeiten_legt_eine_Fassung_an_ohne_Ausgabe()
    {
        var vm = await Model();
        var detail = vm.Detail!;
        await detail.RequestReprocessAsync("summarize");
        await detail.Running;
        Assert.Equal(Loc.Instance["History_Status_saved"], detail.Status);
        Assert.Equal([VersionChip.Origin, "polish", "summarize"], detail.Chips.Select(c => c.Key));
        Assert.Equal("summarize", detail.SelectedChip!.Key);
        var stored = (await _f.History.GetAsync(detail.Entry.Id, CancellationToken.None))!;
        Assert.Equal("roh vier", stored.Raw);
        Assert.Equal("Vier heute.", stored.Text);
        Assert.Equal(4, (await _f.History.ListAsync(CancellationToken.None)).Count); // kein neuer Eintrag
        Assert.Empty(_f.Sink.Written); // keine Ausgabe
    }

    [AvaloniaFact]
    public async Task Bearbeitete_Fassung_ersetzen_fragt_erst()
    {
        var vm = await Model();
        var detail = vm.Detail!;
        detail.BeginEdit();
        detail.EditText = "Handarbeit";
        await detail.SaveEdit();

        await detail.RequestReprocessAsync("polish");
        Assert.True(detail.ConfirmReplacePending);
        detail.CancelReplace();
        Assert.False(detail.ConfirmReplacePending);
        Assert.Equal("Handarbeit", (await _f.History.GetAsync(detail.Entry.Id, CancellationToken.None))!.Text);

        await detail.RequestReprocessAsync("polish");
        await detail.ConfirmReplace();
        await detail.Running;
        var stored = (await _f.History.GetAsync(detail.Entry.Id, CancellationToken.None))!;
        Assert.NotEqual("Handarbeit", stored.Text);
        Assert.False(HistoryVersions.Find(stored, "polish")!.Edited);
    }

    [AvaloniaFact]
    public async Task Stufen_waehler_per_Tastatur_im_Verlauf()
    {
        var vm = await Model();
        var w = await Show(vm);
        w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReprocessButton").Command!.Execute(null);
        var picker = w.GetVisualDescendants().OfType<WhisperLoom.App.Design.StagePicker>().Single();
        await Settle();
        Assert.True(vm.Detail!.IsStagePickerOpen);
        picker.Focus();
        Press(w, Key.Down, physical: PhysicalKey.ArrowDown);
        Press(w, Key.Enter, physical: PhysicalKey.Enter);
        await Settle();
        await vm.Detail.Running;
        Assert.False(vm.Detail.IsStagePickerOpen);
        Assert.Contains(vm.Detail.Chips, c => c.Key == "summarize");
        w.Close();
    }

    [AvaloniaFact]
    public async Task Ohne_Freigabe_kein_Neu_verarbeiten()
    {
        var vm = await Model(reprocess: false);
        Assert.False(vm.Detail!.CanReprocess);
    }

    [AvaloniaFact]
    public async Task Leeren_fragt_erst()
    {
        var vm = await Model();
        vm.AskClear();
        Assert.True(vm.ClearPending);
        vm.CancelClear();
        Assert.Equal(4, (await _f.History.ListAsync(CancellationToken.None)).Count);
        vm.AskClear();
        await vm.Clear();
        Assert.Empty(await _f.History.ListAsync(CancellationToken.None));
        Assert.True(vm.IsEmpty);
    }
}
