using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Output;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.Pill;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Features;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>
/// Overlay-Familie im neuen Stil (UX-4, k3.html und overlay.html): Zustände mit eigenen Knöpfen und Tastenwegen, „Verwerfen“
/// nach einem Fehler, „Ohne KI einfügen“, reduzierte Bewegung, Pille ohne Wort, Hinweise mit Art und Tastenkappen.
/// </summary>
public sealed class OverlayFamilyTest
{
    private static WhisperLoomSettings Polish() => new()
    {
        Engine = "online",
        Refine = new RefineSettings { Stage = "polish" },
        Polish = new PolishSettings { TrailingSpace = false },
    };

    // --- Overlay: Zustände ------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Aufnahme_zeigt_Pause_Verwerfen_Senden_und_Enter_Ruecktaste()
    {
        Loc.Instance.SetLanguage("de");
        var vm = new OverlayViewModel(() => { }, () => { }, () => { }) { Stage = "Glätten", State = OverlayState.Recording };
        Assert.True(vm.CanAct);
        Assert.True(vm.ShowDiscard);
        Assert.False(vm.ShowRetry);
        Assert.False(vm.ShowSkip);
        Assert.Equal("Stufe: Glätten", vm.StageText);
        Assert.Equal("WhisperLoom – Diktat: Aufnahme läuft", vm.AccessibleName);
        Assert.Equal(Loc.Instance["Overlay_KeysNoStage"], vm.KeysTemplate);
        Assert.Equal(["Enter", "Rücktaste"], vm.Keys);
        Assert.Null(vm.Detail);
    }

    [AvaloniaFact]
    public void Fehler_mit_Audio_bietet_Verwerfen_und_Erneut_senden_ohne_Tastenzeile()
    {
        Loc.Instance.SetLanguage("de");
        var dismissed = 0;
        var vm = new OverlayViewModel(() => { }, () => throw new InvalidOperationException("nicht das Diktat"), () => { })
        {
            Retry = () => { },
            DismissError = () => dismissed++,
            State = OverlayState.Error,
            ErrorKey = "Error_Network",
            CanRetry = true,
        };
        Assert.True(vm.ShowDiscard);
        Assert.True(vm.ShowRetry);
        Assert.False(vm.ShowLevel);
        Assert.Null(vm.KeysTemplate); // Overlay-Tasten sind nach dem Senden frei
        Assert.Equal(Loc.Instance["Overlay_ErrorKept"], vm.Detail);

        vm.DiscardCommand.Execute(null);
        Assert.Equal(1, dismissed);
    }

    [AvaloniaFact]
    public void Verarbeiten_bietet_Ohne_KI_mit_Esc_und_ruht_ohne_Animationen()
    {
        Loc.Instance.SetLanguage("de");
        var skipped = 0;
        var vm = new OverlayViewModel(() => { }, () => { }, () => { }) { SkipRefine = () => skipped++, State = OverlayState.Processing };
        Assert.True(vm.ShowSkip);
        Assert.False(vm.ShowDiscard);
        Assert.Equal(["Esc"], vm.Keys);
        Assert.True(vm.Moving);
        Assert.True(vm.ShowLevel);

        vm.Motion = false; // Systemeinstellung „Animationen anzeigen“ aus
        Assert.False(vm.Moving);
        Assert.False(vm.ShowLevel);

        vm.SkipCommand.Execute(null);
        Assert.Equal(1, skipped);
    }

    [AvaloniaFact]
    public void Anweisung_diktieren_und_Wird_umgeschrieben()
    {
        Loc.Instance.SetLanguage("de");
        var vm = new OverlayViewModel(() => { }, () => { }, () => { }) { IsRewrite = true, State = OverlayState.Recording };
        Assert.Equal("Anweisung diktieren", vm.StateText);
        vm.State = OverlayState.Processing;
        Assert.Equal("Wird umgeschrieben …", vm.StateText);
        // Prüfer-Fund UX-4: Umschreiben ist nicht überspringbar – kein „Ohne KI einfügen“, keine Esc-Zeile.
        vm.SkipRefine = () => { };
        Assert.False(vm.ShowSkip);
        Assert.Null(vm.KeysTemplate);
    }

    /// <summary>Prüfer-Fund UX-4: Textknöpfe und „Verwerfen“ nach Fehler stehen auf der Innenkante wie das Zustandssymbol.</summary>
    [AvaloniaFact]
    public void Eine_Textkante_auch_nach_Fehler_und_beim_Verarbeiten()
    {
        var vm = new OverlayViewModel(() => { }, () => { }, () => { })
        {
            DismissError = () => { },
            Retry = () => { },
            SkipRefine = () => { },
            CanRetry = true,
            ErrorKey = "Error_Network",
            State = OverlayState.Error,
        };
        var window = new OverlayWindow(vm);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        double Left(string name) => window.FindControl<Control>(name)!.TranslatePoint(default, window)!.Value.X;
        Assert.Equal(Left("ErrorIcon"), Left("DiscardButton"), 1);

        vm.State = OverlayState.Processing;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var skipIcon = window.FindControl<Button>("SkipButton")!.GetLogicalDescendants().OfType<Design.LoomIcon>().First();
        Assert.Equal(Left("ProcessingIcon"), skipIcon.TranslatePoint(default, window)!.Value.X, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void Texte_folgen_der_Sprache()
    {
        Loc.Instance.SetLanguage("de");
        var vm = new OverlayViewModel(() => { }, () => { }, () => { }) { Stage = "Glätten" };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Loc.Instance.SetLanguage("en");
        Assert.Equal("Stage: Glätten", vm.StageText);
        Assert.Contains(nameof(OverlayViewModel.StateText), changed);
        Assert.Contains(nameof(OverlayViewModel.Keys), changed);
        Loc.Instance.SetLanguage("de");
    }

    // --- Ablauf: Verwerfen nach Fehler, Ohne KI, Anweisung ----------------------------------------------------------

    [AvaloniaFact]
    public async Task Verwerfen_nach_Fehler_schliesst_und_vergisst_das_Audio()
    {
        var (f, _, flow, ui) = Flow(Polish());
        using var _ = f;
        f.Pipeline.Failure = FakeFailure.SttNetwork;
        await flow.HandleAsync(HotkeyAction.Toggle);
        var send = flow.HandleAsync(HotkeyAction.OverlaySend);
        await WaitFor(() => flow.Overlay.State == OverlayState.Error);
        Assert.True(f.Controller.CanRetry);
        Assert.True(flow.Overlay.ShowDiscard);
        var retryChanged = false;
        f.Controller.PropertyChanged += (_, e) => retryChanged |= e.PropertyName == nameof(DictationController.CanRetry);

        flow.Overlay.DiscardCommand.Execute(null);

        Assert.False(ui.Overlay);
        Assert.False(f.Controller.CanRetry);
        Assert.False(flow.Overlay.CanRetry);
        Assert.True(retryChanged); // Tray-Eintrag „Erneut senden“ verschwindet
        Assert.Equal("nothing", await f.Controller.RetryAsync());
        GC.KeepAlive(send);
    }

    /// <summary>Prüfer-Fund UX-4: Ein neues Diktat (hier über das Tray) verwirft das Audio – das Fehler-Overlay schließt.</summary>
    [AvaloniaFact]
    public async Task Neues_Diktat_schliesst_das_Fehler_Overlay()
    {
        var (f, _, flow, ui) = Flow(Polish());
        using var _ = f;
        f.Pipeline.Failure = FakeFailure.SttNetwork;
        await flow.HandleAsync(HotkeyAction.Toggle);
        var send = flow.HandleAsync(HotkeyAction.OverlaySend);
        await WaitFor(() => flow.Overlay.State == OverlayState.Error);
        Assert.True(ui.Overlay);

        f.Controller.Start();

        Assert.False(ui.Overlay);
        Assert.False(flow.Overlay.CanRetry);
        f.Controller.Cancel();
        GC.KeepAlive(send);
    }

    [Fact]
    public void DropRetry_ohne_Audio_meldet_nichts()
    {
        using var f = new ControllerFixture(Polish());
        var changed = 0;
        f.Controller.PropertyChanged += (_, _) => changed++;
        Assert.False(f.Controller.DropRetry());
        Assert.Equal(0, changed);
    }

    [AvaloniaFact]
    public async Task Ohne_KI_einfuegen_im_Overlay_ueberspringt_die_Verbesserung()
    {
        var (f, rig, flow, _) = Flow(Polish(), new ComfortTest.SlowRefinePipeline());
        using var _ = f;
        await flow.HandleAsync(HotkeyAction.Toggle);
        var send = flow.HandleAsync(HotkeyAction.OverlaySend);
        await WaitFor(() => f.Controller.IsRefining);
        flow.Tick();
        Assert.True(flow.Overlay.CanSkip);
        Assert.True(flow.Overlay.ShowSkip);

        flow.Overlay.SkipCommand.Execute(null);

        await send.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal("Hallo Welt", rig.Keys.Target);
    }

    // --- Hinweise ---------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Kopiert_mit_Tastenkappe_Passwortfeld_als_Warnung()
    {
        Loc.Instance.SetLanguage("de");
        var (f, _, flow, ui) = Flow(Polish());
        using var _ = f;

        flow.ShowHint(Core.Output.OutputHint.Copied);
        Assert.Equal("Kopiert – einfügen mit Strg+V", ui.Hints[^1].Message);
        Assert.Equal(HintTone.Copied, ui.HintStyles[^1].Tone);
        Assert.Equal(["Strg+V"], ui.HintStyles[^1].Keys!);

        flow.ShowHint(Core.Output.OutputHint.PasswordField);
        Assert.Equal(Loc.Instance["Output_Hint_Password"], ui.Hints[^1].Message);
        Assert.Equal(HintTone.Warning, ui.HintStyles[^1].Tone);
    }

    [Theory]
    [InlineData("Recording_Capped", HintTone.Warning)]
    [InlineData("Error_Network", HintTone.Error)]
    [InlineData("Overlay_Error", HintTone.Error)]
    [InlineData("Rewrite_TargetChanged", HintTone.Warning)]
    [InlineData("Rewrite_Locked", HintTone.Warning)]
    [InlineData("Recording_Long", HintTone.Info)]
    [InlineData("Output_Hint_RawOnly", HintTone.Info)]
    public void Art_des_Hinweises_aus_dem_Schluessel(string key, HintTone tone) =>
        Assert.Equal(tone, InputFlow.ToneOf(key));

    [AvaloniaFact]
    public void Hinweisfenster_setzt_Tasten_ein_und_nennt_den_Inhalt()
    {
        Loc.Instance.SetLanguage("de");
        var hint = new HintWindow();
        hint.SetContent("toast", "WhisperLoom", "Kopiert – einfügen mit {0}", HintTone.Copied, ["Strg+V"]);
        Assert.Equal("Kopiert – einfügen mit Strg+V", hint.Text);
        Assert.Equal("WhisperLoom: Kopiert – einfügen mit Strg+V", AutomationProperties.GetName(hint.FindControl<Border>("Panel")!));

        hint.SetContent("toast", "WhisperLoom", "Passwortfeld – nicht eingefügt", HintTone.Warning);
        Assert.Equal("Passwortfeld – nicht eingefügt", hint.Text);
        Assert.Contains("warning", hint.FindControl<Border>("Bar")!.Classes);
        Assert.Equal("Loom.Icon.Warning", HintWindow.IconKey(HintTone.Warning));
        Assert.Equal("Loom.Icon.TextStage", HintWindow.IconKey(HintTone.Stage));
    }

    /// <summary>UX-6 N21: Warnung und Fehler tragen Wesentliches und laufen nicht per Zeit ab; „Schließen“ nimmt sie weg.</summary>
    [AvaloniaTheory]
    [InlineData(HintTone.Warning, true)]
    [InlineData(HintTone.Error, true)]
    [InlineData(HintTone.Copied, false)]
    [InlineData(HintTone.Info, false)]
    [InlineData(HintTone.Stage, false)]
    public void Warnung_und_Fehler_bleiben_stehen(HintTone tone, bool sticks) =>
        Assert.Equal(sticks, InputPresenter.Sticks(tone));

    [AvaloniaFact]
    public void Stehender_Hinweis_hat_Schliessen_mit_Namen()
    {
        Loc.Instance.SetLanguage("de");
        var hint = new HintWindow();
        hint.SetContent("toast", "WhisperLoom", "Passwortfeld – nicht eingefügt", HintTone.Warning);
        var close = hint.FindControl<Button>("CloseButton")!;
        Assert.False(close.IsVisible);
        hint.Sticky = true;
        hint.Show();
        Assert.True(close.IsEffectivelyVisible);
        Assert.Equal(Loc.Instance["Hint_Close"], AutomationProperties.GetName(close));
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(hint.IsVisible);
    }

    // --- Pille --------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Pille_ohne_Wort_aber_mit_Namen_und_ruhend_ohne_Animationen()
    {
        Loc.Instance.SetLanguage("de");
        var pill = new PillViewModel();
        Assert.Equal("WhisperLoom – Aufnahme läuft", pill.AccessibleName);
        Assert.True(pill.ShowBar);
        pill.Processing = true;
        Assert.Equal("WhisperLoom – Verarbeitung läuft", pill.AccessibleName);
        Assert.True(pill.Moving);
        pill.Motion = false;
        Assert.False(pill.Moving);
        Assert.False(pill.ShowBar);

        var window = new PillWindow(pill);
        Assert.Empty(window.GetLogicalDescendants().OfType<TextBlock>()); // nur Symbol und Pegel (Entscheidung 14)
    }

    // --- Markierungs-Knopf: Kürzel als Tastenkappe ------------------------------------------------------------------

    [AvaloniaFact]
    public void Knopf_zeigt_das_Kuerzel_nur_wenn_es_wirkt()
    {
        var (f, _, flow, _) = Flow(Polish());
        using var _ = f;
        Assert.Equal(SelectionSettings.DefaultHotkey, flow.SelectionShortcut());

        f.Controller.Settings.Selection.HotkeyCombo = "";
        Assert.Null(flow.SelectionShortcut());

        f.Controller.Settings.Selection.HotkeyCombo = "Ctrl+Alt+P";
        var desktop = SettingsStore.Desktop(f.Controller.Settings);
        desktop.HotkeysEnabled = false;
        SettingsStore.SetDesktop(f.Controller.Settings, desktop);
        Assert.Null(flow.SelectionShortcut());

        var window = new WhisperLoom.App.Features.Selection.SelectionButtonWindow();
        window.SetShortcut("Ctrl+Alt+P");
        Assert.Equal("Ctrl+Alt+P", window.Shortcut);
        window.SetShortcut(null);
        Assert.Null(window.Shortcut);
    }

    // --- Helfer --------------------------------------------------------------------------------------------------------

    private static (ControllerFixture F, InsertRig Rig, InputFlow Flow, FakePresenter Ui) Flow(WhisperLoomSettings s, IDictationPipeline? pipeline = null)
    {
        var rig = new InsertRig();
        var ui = new FakePresenter();
        ControllerFixture? f = null;
        var sink = new InsertOutputSink(new CaptureSink(), rig.Inserter, () => InsertMode.Paste, () => f!.Controller.Settings.Output);
        f = new ControllerFixture(s, sink, pipeline: pipeline);
        f.Pipeline.RawText = "Hallo Welt";
        var flow = new InputFlow(f.Controller, sink, ui, () => rig.Windows.Current, _ => { });
        return (f, rig, flow, ui);
    }

    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 1000 && !done(); i++)
        {
            await Task.Delay(10);
        }
        Assert.True(done());
    }
}
