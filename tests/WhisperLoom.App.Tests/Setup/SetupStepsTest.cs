using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Setup;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>
/// Einrichtung (UX-3, mockups/einrichtung.html): Schrittliste, „Später fortsetzen“ per Esc, ein Knopf für den Pegel-Test,
/// Hinweis auf entfallene Schritte, Tastenkappen auf „Fertig“ und Abmelden vom Sprachwechsel beim Schließen.
/// </summary>
public sealed class SetupStepsTest
{
    [AvaloniaFact]
    public void Geschlossener_Assistent_haengt_nicht_mehr_am_Sprachwechsel()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        var titles = 0;
        s.Vm.PropertyChanged += (_, e) => titles += e.PropertyName == nameof(SetupViewModel.Title) ? 1 : 0;
        Loc.Instance.SetLanguage("en");
        Assert.True(titles > 0); // offen: Titel folgt der Sprache

        window.Close();
        Dispatcher.UIThread.RunJobs();
        titles = 0;
        Loc.Instance.SetLanguage("de");
        Assert.Equal(0, titles); // geschlossen: kein Abo mehr auf Loc.Instance (sonst hält es Fenster und Assistent im Speicher)
    }

    [AvaloniaFact]
    public void Schrittliste_zeigt_erledigt_aktuell_kommend_und_springt_nur_zurueck()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        SetupUiTest.Show(s.Vm);
        s.Vm.Next();
        s.Vm.Next(); // Erkennung

        var items = s.Vm.StepItems;
        Assert.Equal(s.Vm.Steps, items.Select(i => i.Step));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Current, StepState.Upcoming], items.Take(4).Select(i => i.State));
        Assert.Equal("Zurück zu Schritt 1: Sprache", items[0].GoName);
        Assert.Equal("Schritt 3: Erkennung (aktueller Schritt)", items[2].Name);
        Assert.Equal("7 Schritte", s.Vm.StepsCaption); // ohne Firmen-Pfad nur die Zahl

        items[^1].GoCommand.Execute(SetupStep.Done); // kommender Schritt: kein Sprung nach vorn
        Assert.Equal(SetupStep.Recognition, s.Vm.Current);
        items[0].GoCommand.Execute(SetupStep.Language);
        Assert.Equal(SetupStep.Language, s.Vm.Current);
        Assert.Equal(StepState.Current, s.Vm.StepItems[0].State);
    }

    [AvaloniaFact]
    public void Schrittliste_im_Fenster_hat_Knoepfe_nur_fuer_erledigte_Schritte()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        s.Vm.Next();
        Dispatcher.UIThread.RunJobs();
        var list = SetupUiTest.Named<ItemsControl>(window, "StepList");
        var buttons = list.GetVisualDescendants().OfType<Button>().Where(SetupUiTest.Shown).ToList();
        var back = Assert.Single(buttons);
        Assert.Equal("Zurück zu Schritt 1: Sprache", Avalonia.Automation.AutomationProperties.GetName(back));
        back.Command!.Execute(back.CommandParameter);
        Assert.Equal(SetupStep.Language, s.Vm.Current);
        window.Close();
    }

    [AvaloniaFact]
    public void Zuruecksprung_per_Tastatur_setzt_den_Fokus_in_den_Schritt()
    {
        // Prüfer-Fund PR #42: Der fokussierte „Zurück zu Schritt 1“-Knopf verschwindet beim Sprung; der Fokus stand danach nirgends.
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        s.Vm.Next();
        Dispatcher.UIThread.RunJobs();
        var back = SetupUiTest.Named<ItemsControl>(window, "StepList").GetVisualDescendants().OfType<Button>().Single(SetupUiTest.Shown);
        back.Focus(NavigationMethod.Tab);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SetupStep.Language, s.Vm.Current);
        var focused = window.FocusManager?.GetFocusedElement() as Avalonia.Visual;
        Assert.NotNull(focused);
        Assert.True(SetupUiTest.Named<ScrollViewer>(window, "StepContent").IsVisualAncestorOf(focused));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Kopiert_folgt_der_Sprache_und_geht_wieder_auf_Kopieren()
    {
        // Prüfer-Fund PR #42: Der Kopier-Knopf überschrieb seinen Text dauerhaft mit „Kopiert“ (ohne Sprachbindung).
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture(windows: false);
        var window = SetupUiTest.Show(s.Vm);
        while (s.Vm.Current != SetupStep.Hotkey)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();
        var button = SetupUiTest.Named<Button>(window, "CopyCommandButton");
        string Label() => button.GetVisualDescendants().OfType<TextBlock>().Single(SetupUiTest.Shown).Text!;
        Assert.Equal("Kopieren", Label());

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Kopiert", Label());
        Loc.Instance.SetLanguage("en");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Copied", Label());

        await Task.Delay(CopyFeedback.Duration + TimeSpan.FromMilliseconds(300));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Copy", Label());
        window.Close();
        Loc.Instance.SetLanguage("de");
    }

    [AvaloniaFact]
    public void Sprachwechsel_erneuert_Nutzungswahl_und_Satz_auf_Fertig()
    {
        // Prüfer-Fund PR #42: Vorlage auf „Fertig“ und die Nutzungswahl blieben nach dem Sprachwechsel in der alten Sprache.
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        while (s.Vm.Current != SetupStep.Done)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();

        Loc.Instance.SetLanguage("en");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(s.Vm.DoneTemplate, new[] { Loc.Instance["Setup_DoneWindows"], Loc.Instance["Setup_DoneLinux"] });
        Assert.Equal(s.Vm.DoneTemplate, SetupUiTest.Named<KeycapText>(window, "DoneHintText").Template);
        Assert.Equal(Loc.Instance["Setup_UsagePersonal"], s.Vm.UsageChoices[0].Label);
        Assert.Same(s.Vm.UsageChoices[0], s.Vm.Usage);
        window.Close();
        Loc.Instance.SetLanguage("de");
    }

    [AvaloniaFact]
    public void Sprachwechsel_behaelt_die_Wahl_mit_Firma()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        s.Vm.Usage = s.Vm.UsageChoices[1];
        Dispatcher.UIThread.RunJobs();
        Loc.Instance.SetLanguage("en");
        Dispatcher.UIThread.RunJobs();
        Assert.True(s.Vm.UseCompany); // die neue Liste setzt die Wahl nicht zurück
        Assert.Same(s.Vm.UsageChoices[1], s.Vm.Usage);
        window.Close();
        Loc.Instance.SetLanguage("de");
    }

    [AvaloniaFact]
    public async Task Esc_fragt_spaeter_fortsetzen_und_speichert_ohne_erledigt()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        s.Settings.Language = s.Settings.Languages.Single(l => l.Value == "en");
        s.Vm.AutostartEnabled = true;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(s.Vm.LaterPending);
        Assert.True(SetupUiTest.Named<Border>(window, "LaterConfirm").IsVisible);
        Assert.False(SetupUiTest.Shown(SetupUiTest.Named<Button>(window, "NextButton"))); // Rückfrage statt Fuß
        Assert.True(SetupUiTest.Named<Button>(window, "LaterButton").IsFocused);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); // zweites Esc nimmt die Frage zurück
        Assert.False(s.Vm.LaterPending);
        Assert.Equal(0, s.Closed);

        s.Vm.AskLater();
        await s.Vm.LaterCommand.ExecuteAsync(null);
        Assert.Equal(1, s.Closed);
        Assert.False(s.State.Done); // erscheint beim nächsten Start wieder
        Assert.Equal("en", s.F.Store.Load().Language); // Angaben bleiben
        Assert.False(s.Autostart.IsEnabled); // Autostart erst mit „Fertig“
    }

    [AvaloniaFact]
    public void Schrittwechsel_nimmt_die_Rueckfrage_zurueck()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        s.Vm.AskLater();
        s.Vm.Next();
        Assert.False(s.Vm.LaterPending);
        Assert.True(s.Vm.ShowFoot);
    }

    [AvaloniaFact]
    public void Ein_Knopf_startet_und_beendet_den_Pegel_Test()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        s.Vm.Next();
        Dispatcher.UIThread.RunJobs();
        var button = SetupUiTest.Named<Button>(window, "MicTestButton");
        Assert.Equal("Pegel testen", s.Vm.MicButtonText);

        button.Command!.Execute(null);
        Assert.True(s.Recorder.Running);
        Assert.Equal("Test beenden", s.Vm.MicButtonText);
        Assert.Equal("Test beenden", Avalonia.Automation.AutomationProperties.GetName(button));

        button.Command.Execute(null);
        Assert.False(s.Recorder.Running);
        Assert.Equal("Pegel testen", s.Vm.MicButtonText);
        window.Close();
    }

    [AvaloniaFact]
    public void Ohne_Mikrofon_kein_Knopf_und_eine_Warnung()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new SetupFixture();
        var vm = new SetupViewModel(f.Settings, f.State, f.Autostart, null, null, true, "toggle");
        var window = SetupUiTest.Show(vm);
        vm.Next();
        Dispatcher.UIThread.RunJobs();
        Assert.True(SetupUiTest.Shown(SetupUiTest.Named<HintRow>(window, "MicUnavailable")));
        Assert.False(SetupUiTest.Shown(SetupUiTest.Named<Button>(window, "MicTestButton")));
        vm.ToggleMic(); // ohne Pegel-Test: nichts
        window.Close();
    }

    [AvaloniaFact]
    public void Hinweis_nennt_die_von_der_IT_festgelegten_Schritte()
    {
        Loc.Instance.SetLanguage("de");
        using (var free = new SetupFixture())
        {
            Assert.Equal("", free.Vm.SkippedStepsText); // ohne Richtlinie kein Hinweis
        }
        using (var some = new SetupFixture(policy: new PolicySettings { Locked = ["stt.providerId"] }))
        {
            Assert.Equal("", some.Vm.SkippedStepsText); // Richtlinie, aber kein Schritt entfällt: kein Hinweis
        }
        var policy = new PolicySettings { Locked = ["engine", "stt", "secrets.stt", "refine"] };
        using var s = new SetupFixture(policy: policy);
        Assert.Equal("Einige Schritte hat deine IT bereits festgelegt; sie entfallen: Erkennung, Stufe.", s.Vm.SkippedStepsText);
        var window = SetupUiTest.Show(s.Vm);
        Assert.True(SetupUiTest.Named<HintRow>(window, "PolicyHint").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Hilfe_Knopf_nennt_den_Schritt()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        Assert.Equal("Hilfe zu „Sprache“ (F1)", Avalonia.Automation.AutomationProperties.GetName(SetupUiTest.Named<Button>(window, "HelpButton")));
        s.Vm.Next();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Hilfe zu „Mikrofon“ (F1)", Avalonia.Automation.AutomationProperties.GetName(SetupUiTest.Named<Button>(window, "HelpButton")));
        window.Close();
    }

    [Fact]
    public void Tastenkappen_ersetzen_die_Platzhalter()
    {
        var parts = KeycapText.Parts("Halte {0} zum Diktieren oder drück {1}.", ["Strg+Win", "Strg+Leertaste"]).ToList();
        Assert.Equal([("Halte ", false), ("Strg+Win", true), (" zum Diktieren oder drück ", false), ("Strg+Leertaste", true), (".", false)], parts);
        // Fehlt eine Taste, bleibt der Platzhalter als Text stehen statt zu werfen.
        Assert.Equal([("{0}", false), (" am Anfang", false)], KeycapText.Parts("{0} am Anfang", []).ToList());
    }

    [AvaloniaFact]
    public void Fertig_zeigt_die_Tastenkuerzel_als_Tastenkappen()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = SetupUiTest.Show(s.Vm);
        while (s.Vm.Current != SetupStep.Done)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();
        var text = SetupUiTest.Named<KeycapText>(window, "DoneHintText");
        var runs = text.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
        Assert.Contains(runs, r => r.Text!.Contains(s.Settings.Hotkeys.Hold.Value, StringComparison.Ordinal) && r.FontWeight == Avalonia.Media.FontWeight.SemiBold);
        Assert.Equal("Alles bereit", s.Vm.Title);
        Assert.False(SetupUiTest.Named<Button>(window, "SkipButton").IsVisible); // am Ende kein Überspringen
        Assert.True(SetupUiTest.Named<Button>(window, "FinishButton").IsVisible);
        window.Close();
    }
}
