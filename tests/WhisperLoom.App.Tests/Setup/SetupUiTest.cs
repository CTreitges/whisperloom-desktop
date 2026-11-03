using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Linux;
using WhisperLoom.App.Setup;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>Einrichtungs-Assistent headless: Schritte, Überspringen, erneuter Aufruf, Richtlinie, Pegel-, Hotkey-Test, Autostart.</summary>
public sealed class SetupUiTest
{
    private static readonly SetupStep[] AllSteps =
        [SetupStep.Language, SetupStep.Microphone, SetupStep.Recognition, SetupStep.Stage, SetupStep.Hotkey, SetupStep.Autostart, SetupStep.Done];

    internal static SetupWindow Show(SetupViewModel vm)
    {
        var window = new SetupWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    internal static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>
    /// Wirklich sichtbar: im visuellen Baum und kein Vorfahr unsichtbar. Inhalt unter einem unsichtbaren Vorfahren wird nie
    /// gemessen, hängt dann gar nicht im visuellen Baum und meldet trotzdem <c>IsEffectivelyVisible</c>.
    /// </summary>
    internal static bool Shown(Control c) => c.IsEffectivelyVisible && TopLevel.GetTopLevel(c) is not null;

    [AvaloniaFact]
    public void Alle_Schritte_der_Reihe_nach_mit_Zurueck()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = Show(s.Vm);
        Assert.Equal(AllSteps, s.Vm.Steps);
        Assert.Equal(SetupStep.Language, s.Vm.Current);
        Assert.Equal("Schritt 1 von 7", Named<TextBlock>(window, "ProgressText").Text);
        Assert.False(s.Vm.BackCommand.CanExecute(null));
        Assert.True(Named<Control>(window, "LanguagePanel").IsVisible);

        var titles = new List<string>();
        foreach (var step in AllSteps)
        {
            Assert.Equal(step, s.Vm.Current);
            Dispatcher.UIThread.RunJobs();
            titles.Add(Named<TextBlock>(window, "StepTitle").Text ?? "");
            if (step != SetupStep.Done)
            {
                Assert.True(Named<Button>(window, "NextButton").IsVisible);
                s.Vm.Next();
            }
        }
        Assert.Equal(titles.Count, titles.Distinct().Count());
        Assert.DoesNotContain(titles, t => t.StartsWith('!'));
        Assert.True(Named<Button>(window, "FinishButton").IsVisible);
        Assert.False(Named<Button>(window, "NextButton").IsVisible);
        Assert.Equal("Schritt 7 von 7", Named<TextBlock>(window, "ProgressText").Text);

        s.Vm.Next(); // am Ende bleibt es bei „Fertig“
        Assert.Equal(SetupStep.Done, s.Vm.Current);
        s.Vm.Back();
        Assert.Equal(SetupStep.Autostart, s.Vm.Current);
        Assert.True(Named<Control>(window, "AutostartPanel").IsVisible);
        Assert.False(Named<Control>(window, "DonePanel").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Ueberspringen_speichert_nichts_und_markiert_erledigt()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        var window = Show(s.Vm);
        Assert.False(s.State.Done);
        s.Settings.Language = s.Settings.Languages.Single(l => l.Value == "en");
        s.Settings.UiLanguage = s.Settings.UiLanguages.Single(l => l.Value == "en");
        Assert.Equal("en", Loc.Instance.Language); // Vorschau sofort
        s.Vm.AutostartEnabled = true;

        s.Vm.SkipCommand.Execute(null);

        Assert.Equal(1, s.Closed);
        Assert.True(s.State.Done);
        Assert.Equal("de", s.F.Controller.Settings.Language);
        Assert.False(File.Exists(s.F.Store.FilePath));
        Assert.False(s.Autostart.IsEnabled);
        Assert.Equal(Loc.OsLanguage(), Loc.Instance.Language); // Vorschau zurück auf die gespeicherte Wahl „wie System“
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Fertig_speichert_Auswahl_Autostart_und_markiert_erledigt()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        Show(s.Vm);
        s.Settings.Language = s.Settings.Languages.Single(l => l.Value == "en");
        s.Settings.Microphone = s.Settings.Microphones.Single(m => m.Value == "USB-Mikrofon");
        s.Settings.Stage = s.Settings.Stages.Single(m => m.Value == "prompt");
        s.Vm.AutostartEnabled = true;
        while (s.Vm.Current != SetupStep.Done)
        {
            s.Vm.Next();
        }

        await s.Vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal("", s.Vm.Status);
        Assert.Equal(1, s.Closed);
        Assert.True(s.State.Done);
        var saved = s.F.Store.Load();
        Assert.Equal("en", saved.Language);
        Assert.Equal("prompt", saved.Refine.Stage);
        Assert.True(saved.Refine.PromptEnabled); // sonst fiele die Stufe Prompt auf Verschönern zurück
        Assert.Equal("USB-Mikrofon", Services.SettingsStore.Desktop(saved).Microphone);
        Assert.True(s.Autostart.IsEnabled);
    }

    [AvaloniaFact]
    public async Task Erneuter_Aufruf_nach_Abschluss_beginnt_vorne_und_zeigt_den_Stand()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        s.Settings.Language = s.Settings.Languages.Single(l => l.Value == "fr");
        await s.Vm.Finish();
        var options = new AppOptions(s.F.DataDir);
        Assert.False(SetupState.ShowOnStart(options, new SetupState(s.F.DataDir)));

        // Erneut aufrufen (Tray, „WhisperLoom setup“): neuer Assistent auf dem gespeicherten Stand.
        var again = new SetupViewModel(new ViewModels.SettingsViewModel(s.F.Controller, s.Secrets, new FakeAccessChecker()),
            new SetupState(s.F.DataDir), s.Autostart, null, null, windows: true, "WhisperLoom toggle");
        var window = Show(again);
        Assert.Equal(SetupStep.Language, again.Current);
        Assert.Equal("fr", again.Settings.Language.Value);
        Assert.True(Named<Control>(window, "LanguagePanel").IsVisible);
        window.Close();
    }

    [Fact]
    public void Beim_Start_nur_ohne_Marker_und_nie_im_Testbetrieb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-setup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = new SetupState(dir);
            Assert.True(SetupState.ShowOnStart(new AppOptions(dir), state));
            Assert.False(SetupState.ShowOnStart(new AppOptions(dir, OutputFile: Path.Combine(dir, "out.txt")), state));
            Assert.False(SetupState.ShowOnStart(new AppOptions(dir, TestAudio: Path.Combine(dir, "a.wav")), state));
            state.MarkDone();
            Assert.True(File.Exists(state.FilePath));
            Assert.True(new SetupState(dir).Done);
            Assert.False(SetupState.ShowOnStart(new AppOptions(dir), state));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Verb_setup_oeffnet_den_Assistenten()
    {
        Assert.Equal("setup", CommandLine.Parse(["setup"]).Verb);
        Assert.False(CommandLine.Parse(["setup"]).NeedsRunningInstance);
    }

    [AvaloniaFact]
    public async Task AppHost_setup_ruft_das_Fenster()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-setup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var host = new AppHost(new AppOptions(dir), new MemorySecretStore(), _ => new Core.Testing.FakeDictationPipeline(), new FakeRecorder(), new CaptureSink());
            var opened = new List<string>();
            host.ShowWindow = opened.Add;
            var response = await host.ExecuteAsync("setup", []);
            Assert.True(response.Ok);
            Assert.Equal(["setup"], opened);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void Richtlinie_blendet_gesperrte_Schritte_aus()
    {
        Loc.Instance.SetLanguage("de");
        var policy = new PolicySettings { Locked = ["language", "desktop.uiLanguage", "engine", "stt", "secrets.stt", "refine"] };
        using var s = new SetupFixture(policy: policy);
        var window = Show(s.Vm);
        Assert.Equal([SetupStep.Microphone, SetupStep.Hotkey, SetupStep.Autostart, SetupStep.Done], s.Vm.Steps);
        Assert.Equal(SetupStep.Microphone, s.Vm.Current);
        Assert.Equal("Schritt 1 von 4", Named<TextBlock>(window, "ProgressText").Text);
        Assert.True(Named<WhisperLoom.App.Design.HintRow>(window, "PolicyHint").IsVisible); // W2-ENR: Hinweis-Zeile des Designsystems
        Assert.False(Named<Control>(window, "LanguagePanel").IsVisible);
        s.Vm.Next();
        Assert.Equal(SetupStep.Hotkey, s.Vm.Current);
        window.Close();
    }

    [AvaloniaFact]
    public void Richtlinie_teilweise_Schritt_bleibt_mit_gesperrten_Feldern()
    {
        Loc.Instance.SetLanguage("de");
        // Nur der Anbieter ist fest: Schritt Erkennung bleibt, Anbieter ausgeblendet, Key und Weg änderbar.
        using var s = new SetupFixture(policy: new PolicySettings { Locked = ["stt.providerId"] });
        var window = Show(s.Vm);
        Assert.Equal(AllSteps, s.Vm.Steps);
        while (s.Vm.Current != SetupStep.Recognition)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();
        Assert.False(Shown(Named<ComboBox>(window, "ProviderBox"))); // W2-ENR: Fest ist auch im Assistenten ausgeblendet
        Assert.True(Named<WhisperLoom.App.Design.OptionGroup>(window, "EngineBox").IsVisible); // UX-3: Optionsfelder statt Auswahlliste
        window.Close();

        // Lokal fest gewählt, Modell fest: Schritt entfällt.
        var offline = new WhisperLoomSettings { Engine = "offline" };
        using var s2 = new SetupFixture(policy: new PolicySettings { Locked = ["engine", "offline.model"] }, settings: offline);
        Assert.DoesNotContain(SetupStep.Recognition, s2.Vm.Steps);
    }

    [AvaloniaFact]
    public async Task Erkennung_Cloud_mit_Zugang_pruefen_und_Lokal_mit_Download()
    {
        Loc.Instance.SetLanguage("de");
        using var s = new SetupFixture();
        s.Secrets.Values[SecretNames.Stt] = "test-key-not-real-61";
        await s.Settings.LoadKeysAsync();
        var window = Show(s.Vm);
        while (s.Vm.Current != SetupStep.Recognition)
        {
            s.Vm.Next();
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<StackPanel>(window, "OnlinePanel").IsVisible);
        Assert.False(Named<StackPanel>(window, "OfflinePanel").IsVisible);
        await s.Settings.SttKey.CheckCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["Access_Ok"] + " (HTTP 200)", s.Settings.SttKey.Status);
        Assert.DoesNotContain(UiTest.TextsOf(window), t => t.Contains("test-key-not-real-61", StringComparison.Ordinal));

        s.Settings.Local.Engine = s.Settings.Local.Engines.Single(e => e.Value == "offline");
        s.Settings.Local.SelectedModel = s.Settings.Local.ModelChoices.Single(m => m.Value == "small");
        Dispatcher.UIThread.RunJobs();
        Assert.False(Named<StackPanel>(window, "OnlinePanel").IsVisible);
        Assert.True(Named<StackPanel>(window, "OfflinePanel").IsVisible);
        Assert.Equal("small", s.Vm.SelectedModel.Model.Id);
        // UX-3: dieselbe Modell-Liste wie unter Erkennung (Herunterladen je Zeile).
        Assert.True(Shown(Named<Views.Settings.ModelList>(window, "ModelList")));
        window.Close();
    }
}
