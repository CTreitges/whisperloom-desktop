using Avalonia.Headless.XUnit;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// App-Profile nach K2 (UX-2): Reihenfolge mit Pfeilen, Löschen mit Rückgängig, Zusammenfassung nur mit Abweichungen,
/// Vorschläge nur für fehlende Programme, Kennzeichen „Vorrang“, „Wie allgemein (zurzeit …)“ folgt den allgemeinen Werten.
/// </summary>
public sealed class ProfilesListTest
{
    private ProfileDefaults _defaults = new("polish", "paste", PressEnter: false, FloatingButton: true);

    private ProfilesViewModel Make(params AppProfile[] profiles)
    {
        Loc.Instance.SetLanguage("de");
        var desktop = new DesktopSettings();
        desktop.Profiles.AddRange(profiles);
        return new ProfilesViewModel(desktop, () => _defaults, () => ["notepad.exe", "WINWORD.EXE"]);
    }

    private static AppProfile Terminal() => new() { Name = "Terminal", Process = "WindowsTerminal.exe", Stage = "prompt" };

    private static AppProfile Outlook() => new() { Name = "Outlook", Process = "OUTLOOK.EXE", Stage = "beautify" };

    [AvaloniaFact]
    public void Arrows_move_a_profile_renumber_and_save_the_new_order()
    {
        var desktop = new DesktopSettings();
        desktop.Profiles.AddRange([Terminal(), Outlook(), new AppProfile { Name = "Word", Process = "WINWORD.EXE" }]);
        var vm = new ProfilesViewModel(desktop, () => _defaults);
        var word = vm.Rows[2];
        Assert.False(word.CanMoveDown);
        Assert.False(vm.Rows[0].CanMoveUp);

        word.MoveUpCommand.Execute(null);
        word.MoveUpCommand.Execute(null);
        word.MoveUpCommand.Execute(null); // schon oben: nichts passiert

        Assert.Equal(["Word", "Terminal", "Outlook"], vm.Rows.Select(r => r.Name));
        Assert.Equal([1, 2, 3], vm.Rows.Select(r => r.Number));
        Assert.True(word is { CanMoveUp: false, CanMoveDown: true });
        Assert.True(vm.Rows[2] is { CanMoveUp: true, CanMoveDown: false });
        vm.Rows[1].MoveDownCommand.Execute(null);
        Assert.Equal(["Word", "Outlook", "Terminal"], vm.Rows.Select(r => r.Name));

        vm.Apply();
        Assert.Equal(["WINWORD.EXE", "OUTLOOK.EXE", "WindowsTerminal.exe"], desktop.Profiles.Select(p => p.Process));
    }

    [AvaloniaFact]
    public void Moving_asks_the_view_to_keep_focus_on_the_moved_profile()
    {
        var vm = Make(Terminal(), Outlook());
        var requests = new List<(ProfileRowViewModel?, ProfileFocus)>();
        vm.FocusRequested += (row, target) => requests.Add((row, target));
        var outlook = vm.Rows[1];

        outlook.MoveUpCommand.Execute(null);

        // Oben angekommen gibt es keinen Pfeil „nach oben“ mehr: der Fokus geht auf „nach unten“ desselben Profils.
        Assert.Equal((outlook, ProfileFocus.MoveDown), Assert.Single(requests));
    }

    [AvaloniaFact]
    public void Delete_offers_undo_which_restores_the_profile_at_its_place()
    {
        var vm = Make(Terminal(), Outlook(), new AppProfile { Name = "Word", Process = "WINWORD.EXE" });
        var outlook = vm.Rows[1];

        outlook.DeleteCommand.Execute(null);

        Assert.Equal(["Terminal", "Word"], vm.Rows.Select(r => r.Name));
        Assert.Equal("Profil „Outlook“ gelöscht.", vm.Status);
        Assert.True(vm.CanUndo);
        Assert.Contains(vm.Suggestions, s => s.Process == "OUTLOOK.EXE"); // gelöscht → Vorschlag wieder da

        vm.UndoCommand.Execute(null);

        Assert.Equal(["Terminal", "Outlook", "Word"], vm.Rows.Select(r => r.Name));
        Assert.Same(outlook, vm.Rows[1]);
        Assert.Equal("Profil „Outlook“ wiederhergestellt.", vm.Status);
        Assert.False(vm.CanUndo);
        Assert.DoesNotContain(vm.Suggestions, s => s.Process == "OUTLOOK.EXE");
    }

    [AvaloniaFact]
    public void Undo_after_further_deletes_restores_only_the_last_and_clamps_the_index()
    {
        var vm = Make(Terminal(), Outlook());
        vm.Rows[1].DeleteCommand.Execute(null); // Outlook an Stelle 1
        vm.Rows[0].DeleteCommand.Execute(null); // Terminal an Stelle 0, ersetzt das Rückgängig von Outlook

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null); // nichts mehr rückgängig zu machen

        Assert.Equal(["Terminal"], vm.Rows.Select(r => r.Name));
        Assert.True(vm.IsEmpty is false);
    }

    [AvaloniaFact]
    public void Summary_names_only_deviations()
    {
        var vm = Make(new AppProfile());
        var row = vm.Rows[0];
        Assert.Equal("Neues Profil", row.DisplayName);
        Assert.Equal("Kein Programm gewählt, greift noch nicht", row.Summary);

        row.Process = "WindowsTerminal.exe";
        Assert.Equal("WindowsTerminal.exe · übernimmt alles von allgemein", row.Summary);

        row.Stage = vm.Stages.Single(c => c.Value == "prompt");
        Assert.Equal("WindowsTerminal.exe · Stufe Prompt", row.Summary);

        row.InsertMode = vm.InsertModes.Single(c => c.Value == "type");
        row.PressEnter = true;
        row.ShowFloatingButton = false;
        row.Enabled = false;
        Assert.Equal("Inaktiv · WindowsTerminal.exe · Stufe Prompt · Ausgabe Direkt tippen · Enter drücken · ohne schwebenden Knopf", row.Summary);

        row.Enabled = true;
        row.Stage = vm.Stages[0];
        row.InsertMode = vm.InsertModes[0];
        row.PressEnter = null;
        row.ShowFloatingButton = true;
        row.TitlePattern = "Claude";
        Assert.Equal("WindowsTerminal.exe · nur Fenster mit „Claude“", row.Summary);
        row.TitlePattern = "Claude(";
        Assert.Equal("WindowsTerminal.exe · Fenstertitel nicht lesbar, greift so nicht", row.Summary);
    }

    [AvaloniaFact]
    public void Floating_button_counts_only_while_it_is_on_in_general()
    {
        var vm = Make(new AppProfile { Process = "notepad.exe", FloatingButton = false });
        Assert.True(vm.FloatingButtonOn);
        Assert.Contains("ohne schwebenden Knopf", vm.Rows[0].Summary);

        _defaults = _defaults with { FloatingButton = false };
        vm.RefreshDefaults();

        Assert.False(vm.FloatingButtonOn);
        Assert.Equal("notepad.exe · übernimmt alles von allgemein", vm.Rows[0].Summary);
        Assert.False(vm.Rows[0].ToProfile().FloatingButton); // die Einstellung bleibt erhalten
    }

    [AvaloniaFact]
    public void Suggestions_appear_only_for_programs_without_a_profile()
    {
        var vm = Make(new AppProfile { Process = "outlook" });
        var only = Assert.Single(vm.Suggestions);
        Assert.Equal("WindowsTerminal.exe", only.Process);
        Assert.Equal("Vorschlag für Terminal: WindowsTerminal.exe, Stufe Prompt", only.Text);
        Assert.Equal("Terminal-Profil anlegen", only.ActionText);

        only.AddCommand.Execute(null);

        Assert.Empty(vm.Suggestions);
        var added = vm.Rows[^1];
        Assert.Equal(("Terminal", "WindowsTerminal.exe", "prompt", false), (added.Name, added.Process, added.Stage.Value, added.IsOpen));
        Assert.Equal("Profil „Terminal“ angelegt.", vm.Status);

        added.Process = "notepad.exe"; // Programm geändert → Terminal fehlt wieder
        Assert.Single(vm.Suggestions);
    }

    [AvaloniaFact]
    public void Add_opens_an_empty_profile_and_asks_for_focus_on_its_name()
    {
        var vm = Make();
        Assert.True(vm.IsEmpty);
        var requests = new List<(ProfileRowViewModel?, ProfileFocus)>();
        vm.FocusRequested += (row, target) => requests.Add((row, target));

        vm.AddCommand.Execute(null);

        var row = Assert.Single(vm.Rows);
        Assert.True(row.IsOpen);
        Assert.False(vm.IsEmpty);
        Assert.Equal((row, ProfileFocus.Name), Assert.Single(requests));
    }

    /// <summary>Prüfer-Befund UX-2/4: „Profil hinzufügen“ ist die nächste Aktion – das Rückgängig-Angebot verfällt.</summary>
    [AvaloniaFact]
    public void Adding_a_profile_ends_the_undo_offer()
    {
        var vm = Make(Terminal(), Outlook());
        vm.Rows[1].DeleteCommand.Execute(null);

        vm.AddCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        Assert.False(vm.CanUndo);
        Assert.Equal(["Terminal", "Neues Profil"], vm.Rows.Select(r => r.DisplayName));
    }

    [AvaloniaFact]
    public void Priority_badge_only_for_a_readable_window_title()
    {
        var vm = Make(Terminal());
        var row = vm.Rows[0];
        Assert.False(row.HasPriority);
        Assert.False(row.ShowAdvanced);

        row.TitlePattern = "Posteingang|Entwurf";
        Assert.True(row.HasPriority);
        Assert.Equal("", row.TitleWarning);

        row.TitlePattern = "Entwurf(";
        Assert.False(row.HasPriority);
        Assert.Equal(Loc.Instance["Profile_InvalidTitle"], row.TitleWarning);
        Assert.DoesNotContain("ungültig", row.TitleWarning, StringComparison.OrdinalIgnoreCase);

        // Mit Titel ist „Nur bestimmte Fenster“ offen.
        Assert.True(Make(new AppProfile { Process = "a.exe", TitlePattern = "x" }).Rows[0].ShowAdvanced);
    }

    [AvaloniaFact]
    public void Inherit_options_name_the_current_general_value_and_follow_it()
    {
        var vm = Make(new AppProfile { Process = "a.exe" });
        var row = vm.Rows[0];
        Assert.Equal("Wie allgemein (zurzeit Glätten)", vm.Stages[0].Label);
        Assert.Equal("Wie allgemein (zurzeit Einfügen)", vm.InsertModes[0].Label);
        Assert.Equal("Wie allgemein (zurzeit kein Enter)", vm.EnterChoices[0].Label);
        Assert.Equal(["Enter drücken", "Kein Enter"], vm.EnterChoices.Skip(1).Select(c => c.Label));

        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        _defaults = new ProfileDefaults("beautify", "clipboard", PressEnter: true, FloatingButton: true);
        vm.RefreshDefaults();

        Assert.Equal("Wie allgemein (zurzeit Verschönern)", vm.Stages[0].Label);
        Assert.Equal("Wie allgemein (zurzeit Nur Zwischenablage)", vm.InsertModes[0].Label);
        Assert.Equal("Wie allgemein (zurzeit Enter drücken)", vm.EnterChoices[0].Label);
        Assert.Same(vm.Stages[0], row.Stage); // die Zeile zeigt weiter „Wie allgemein“ aus der neuen Liste
        Assert.Contains(nameof(ProfileRowViewModel.Stage), changed);
        Assert.Equal("", row.ToProfile().Stage);
    }

    [AvaloniaFact]
    public void Picking_a_running_program_fills_the_field_and_closes_the_list()
    {
        var vm = Make(new AppProfile());
        var row = vm.Rows[0];

        row.PickCommand.Execute(null);
        Assert.True(row.PickOpen);
        Assert.Equal(["notepad.exe", "WINWORD.EXE"], row.RunningPrograms);

        row.PickOneCommand.Execute("WINWORD.EXE");

        Assert.Equal("WINWORD.EXE", row.Process);
        Assert.False(row.PickOpen);
    }

    [AvaloniaFact]
    public void Names_for_screen_readers_carry_the_profile_name()
    {
        var vm = Make(Terminal());
        var row = vm.Rows[0];
        Assert.Equal("Terminal aktiv", row.ActiveLabel);
        Assert.Equal("Terminal nach oben", row.MoveUpLabel);
        Assert.Equal("Terminal nach unten", row.MoveDownLabel);
        Assert.Equal("Profil „Terminal“ löschen", row.DeleteLabel);
        row.Name = "";
        Assert.Equal("Profil „windowsterminal“ löschen", row.DeleteLabel);
    }
}
