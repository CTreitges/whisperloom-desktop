using Avalonia;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Output;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>
/// „Markierung verarbeiten“ (W2-SEL) ohne echte Eingabe: Kombi → Wähler am Cursor → Stufe per Pfeil/Ziffer/Enter → Markierung
/// ersetzt, Zwischenablage wie vorher, Verlaufseintrag mit Quelle „Markierung“; dazu die Starter Knopf, Strg+Rechtsklick, Tray, CLI.
/// </summary>
public sealed class SelectionFlowTest : IDisposable
{
    private readonly InsertRig _rig = new();
    private readonly FakePresenter _ui = new();
    private readonly FakeSelectionPresenter _picker = new();
    private readonly FakeSelectionSource _uia = new();
    private readonly List<string> _log = [];
    private readonly ControllerFixture _f;
    private readonly InputFlow _flow;
    private readonly Dictionary<IntPtr, WindowInfo> _windows = new()
    {
        [42] = new WindowInfo("WINWORD", "Bericht.docx - Word"),
        [7] = new WindowInfo("notepad", "Unbenannt - Editor"),
    };

    public SelectionFlowTest()
    {
        var settings = new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" }, Polish = new PolishSettings { TrailingSpace = true } };
        settings.Selection.FloatingButton = true;
        Sink = new InsertOutputSink(new CaptureSink(), _rig.Inserter, () => InsertMode.Paste);
        _f = new ControllerFixture(settings, Sink);
        _rig.Windows.Alive.Add(7);
        _flow = new InputFlow(_f.Controller, Sink, _ui, () => _rig.Windows.Current, _ => { },
            w => _windows.GetValueOrDefault(w, new WindowInfo("", "")))
        {
            Selection = Wiring(),
        };
    }

    private InsertOutputSink Sink { get; }

    public void Dispose() => _f.Dispose();

    private SelectionWiring Wiring(Func<IntPtr>? lastExternal = null, Func<PixelPoint, IntPtr>? windowAt = null)
    {
        var w = SelectionTesting.Wiring(_rig, _uia, _picker, log: _log.Add);
        return new SelectionWiring
        {
            Reader = w.Reader, Ui = w.Ui, PickerKeys = w.PickerKeys, DismissOnKey = w.DismissOnKey, ReplayRightClick = w.ReplayRightClick,
            Anchor = w.Anchor, LastExternal = lastExternal ?? (() => IntPtr.Zero), WindowAt = windowAt ?? (_ => IntPtr.Zero),
            Activate = _rig.Windows.Activate, Now = () => _f.Now,
        };
    }

    private static Hotkey Key(int vk, HotkeyModifiers mods = HotkeyModifiers.None) => new(mods, vk);

    // --- Tastenkombi → Wähler → Stufe → ersetzt ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Kombi_Waehler_Stufe_zwei_per_Pfeil_Enter_ersetzt_die_Markierung()
    {
        _rig.Keys.Selection = "das ist ein test";

        await _flow.HandleAsync(HotkeyAction.Selection);

        var picker = Assert.IsType<Design.StagePickerViewModel>(_picker.Picker);
        Assert.Equal(["polish", "beautify", "summarize", InputFlow.InstructionKey], picker.Options.Select(o => o.Key));
        Assert.Equal(new PixelPoint(300, 200), _picker.PickerAt); // an der Schreibmarke
        Assert.Contains("picker-keys-on", _log);
        Assert.Equal("alt", _rig.Clipboard.Text); // Erfassung hat die Zwischenablage zurückgestellt

        _flow.PickerKey(Key(0x28)); // ↓
        Assert.Equal("beautify", picker.HighlightedOption?.Key);
        _flow.PickerKey(Key(Hotkey.VkReturn));
        await WaitIdleAsync();

        Assert.Equal("[beautify] das ist ein test", _rig.Keys.Target); // ohne Leerzeichen danach: ersetzt nur die Markierung
        Assert.Equal(0, _rig.Keys.Enters);
        Assert.Equal("alt", _rig.Clipboard.Text); // Zwischenablage danach wie vorher
        Assert.Null(_picker.Picker);
        Assert.Contains("picker-keys-off", _log);
        var entry = Assert.Single(await _f.History.ListAsync(CancellationToken.None));
        Assert.Equal((HistoryEntry.SourceSelection, "das ist ein test", RefineMode.Beautify), (entry.Source, entry.Raw, entry.Refinement.Mode));
    }

    [AvaloniaFact]
    public async Task Ziffer_waehlt_sofort_und_UIA_Markierung_fasst_die_Zwischenablage_nicht_an()
    {
        _uia.Next = new UiaSelection(true, "aus UIA");

        await _flow.HandleAsync(HotkeyAction.Selection);
        _flow.PickerKey(Key('3'));
        await WaitIdleAsync();

        Assert.DoesNotContain("ctrl-c", _rig.Steps.TakeWhile(s => s != "ctrl-v"));
        Assert.Equal(1, _rig.Steps.Count(s => s == "save")); // nur das Einfügen sichert
        Assert.Equal("[summarize] aus UIA", _rig.Keys.Target);
        Assert.Equal("alt", _rig.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task Tab_und_Umschalt_Tab_bewegen_Esc_schliesst_ohne_Aenderung()
    {
        _rig.Keys.Selection = "x";
        await _flow.HandleAsync(HotkeyAction.Selection);
        var picker = _picker.Picker!;
        _flow.PickerKey(Key(0x09));
        _flow.PickerKey(Key(0x09));
        _flow.PickerKey(Key(0x09, HotkeyModifiers.Shift));
        Assert.Equal("beautify", picker.HighlightedOption?.Key);

        _flow.PickerKey(Key(Hotkey.VkEscape));

        Assert.Null(_picker.Picker);
        Assert.Null(_flow.Picker);
        Assert.Equal("none", _flow.ModeName);
        Assert.Equal("", _rig.Keys.Target);
        Assert.Empty(await _f.History.ListAsync(CancellationToken.None));
    }

    [AvaloniaFact]
    public async Task Klick_auf_eine_Zeile_waehlt()
    {
        _rig.Keys.Selection = "klick";
        await _flow.HandleAsync(HotkeyAction.Selection);
        var picker = _picker.Picker!;

        picker.PickCommand.Execute(picker.Options[0]);
        await WaitIdleAsync();

        Assert.Equal("[polish] klick", _rig.Keys.Target);
    }

    [AvaloniaFact]
    public async Task Andere_Taste_Fensterwechsel_oder_Zeit_schliessen_den_Waehler()
    {
        _rig.Keys.Selection = "x";
        await _flow.HandleAsync(HotkeyAction.Selection);
        await _flow.HandleAsync(HotkeyAction.Dismiss);
        Assert.Null(_flow.Picker);

        await _flow.HandleAsync(HotkeyAction.Selection);
        _rig.Windows.Current = 7;
        _flow.Tick();
        Assert.Null(_flow.Picker);

        _rig.Windows.Current = 42;
        await _flow.HandleAsync(HotkeyAction.Selection);
        _f.Now += InputFlow.PickerTimeout + TimeSpan.FromSeconds(1);
        _flow.Tick();
        Assert.Null(_flow.Picker);
        Assert.Equal("", _rig.Keys.Target);
    }

    [AvaloniaFact]
    public async Task Zweiter_Druck_schliesst_anderes_Kuerzel_schliesst_und_laeuft_weiter()
    {
        _rig.Keys.Selection = "x";
        await _flow.HandleAsync(HotkeyAction.Selection);
        await _flow.HandleAsync(HotkeyAction.Selection);
        Assert.Null(_flow.Picker);
        Assert.Equal(DictationState.Idle, _f.Controller.State);

        await _flow.HandleAsync(HotkeyAction.Selection);
        await _flow.HandleAsync(HotkeyAction.Toggle);
        Assert.Null(_flow.Picker);
        Assert.Equal(DictationState.Recording, _f.Controller.State);
        _f.Controller.Cancel();
    }

    [AvaloniaFact]
    public async Task Ziel_weg_nach_der_Wahl_Ergebnis_in_die_Zwischenablage_mit_Hinweis()
    {
        _rig.Keys.Selection = "x";
        await _flow.HandleAsync(HotkeyAction.Selection);
        _rig.Windows.Alive.Remove(42);

        _flow.PickerKey(Key(Hotkey.VkReturn));
        await WaitIdleAsync();

        Assert.Equal("", _rig.Keys.Target);
        Assert.Equal("[polish] x", _rig.Clipboard.Text);
        Assert.Equal(InsertOutcome.TargetGone, Sink.LastOutcome);
        Assert.Equal(OutputHint.Copied, InsertOutcome.TargetGone.Hint());
    }

    [AvaloniaFact]
    public async Task Ohne_KI_Ergebnis_bleibt_die_Markierung_und_ein_Fehler_wird_gezeigt()
    {
        _rig.Keys.Selection = "Original";
        _f.Pipeline.Failure = Core.Testing.FakeFailure.Refine;
        await _flow.HandleAsync(HotkeyAction.Selection);
        _flow.PickerKey(Key(Hotkey.VkReturn));
        await WaitIdleAsync();

        Assert.Equal("", _rig.Keys.Target);
        Assert.Equal(Loc.Instance["Selection_Failed"], _ui.Hints[^1].Message);
        Assert.Empty(await _f.History.ListAsync(CancellationToken.None));
    }

    [AvaloniaFact]
    public async Task Leere_Markierung_Hinweis_Passwortfeld_nichts()
    {
        await _flow.HandleAsync(HotkeyAction.Selection);
        Assert.Equal(Loc.Instance["Selection_Empty"], _ui.Hints[^1].Message);
        Assert.Null(_picker.Picker);

        _ui.Hints.Clear();
        _rig.Steps.Clear();
        var uiaCalls = _uia.Calls;
        _rig.Focus.Kind = FocusKind.Password;
        _rig.Keys.Selection = "geheim";
        await _flow.HandleAsync(HotkeyAction.Selection);
        Assert.Empty(_ui.Hints);
        Assert.Null(_picker.Picker);
        Assert.DoesNotContain("ctrl-c", _rig.Steps);
        Assert.Equal(uiaCalls, _uia.Calls);
    }

    [AvaloniaFact]
    public async Task IT_Sperre_auf_Aus_verweigert_vor_dem_Kopieren()
    {
        _f.Controller.Policy = new PolicySettings { Locked = ["refine.stage"] };
        _rig.Keys.Selection = "Vertraulich";

        await _flow.HandleAsync(HotkeyAction.Selection);

        Assert.DoesNotContain("ctrl-c", _rig.Steps);
        Assert.Equal(Loc.Instance["Rewrite_Locked"], _ui.Hints[^1].Message);
    }

    [AvaloniaFact]
    public async Task Gesperrte_Stufe_bietet_nur_sie_und_die_Anweisung()
    {
        _f.Controller.Settings.Refine.Stage = "beautify";
        _f.Controller.Policy = new PolicySettings { Locked = ["refine.stage"] };
        _rig.Keys.Selection = "x";

        await _flow.HandleAsync(HotkeyAction.Selection);

        Assert.Equal(["beautify", InputFlow.InstructionKey], _picker.Picker!.Options.Select(o => o.Key));
    }

    // --- Starter -----------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Strg_Rechtsklick_mit_Markierung_Waehler_an_der_Maus_ohne_Rechtsklick_nachspielen()
    {
        _rig.Keys.Selection = "markiert";
        Assert.Equal("picker", await _flow.OnCtrlRightClickAsync(new PixelPoint(640, 480)));
        Assert.Equal(new PixelPoint(640, 480), _picker.PickerAt);
        Assert.DoesNotContain("replay-right-click", _log);
        _flow.CancelPicker();

        _rig.Keys.Selection = null;
        _ui.Hints.Clear();
        Assert.Equal("empty", await _flow.OnCtrlRightClickAsync(new PixelPoint(640, 480)));
        Assert.Contains("replay-right-click", _log); // normales Kontextmenü
        Assert.Empty(_ui.Hints);
        Assert.Null(_picker.Picker);
    }

    [AvaloniaFact]
    public async Task Strg_Rechtsklick_auf_ein_anderes_Fenster_spielt_den_Klick_nach()
    {
        var flow = new InputFlow(_f.Controller, Sink, _ui, () => _rig.Windows.Current, _ => { })
        {
            Selection = Wiring(windowAt: _ => 7),
        };
        _rig.Keys.Selection = "markiert";

        Assert.Equal("other-window", await flow.OnCtrlRightClickAsync(new PixelPoint(1, 1)));
        Assert.Contains("replay-right-click", _log);
        Assert.DoesNotContain("ctrl-c", _rig.Steps);
    }

    [AvaloniaFact]
    public async Task Knopf_erscheint_nach_Markier_Geste_und_startet_den_Waehler()
    {
        _uia.Next = new UiaSelection(true, "Wort");
        await _flow.OnSelectionGestureAsync(new PixelPoint(100, 100));

        Assert.Equal(new PixelPoint(114, 114), _picker.ButtonAt);
        Assert.Contains("dismiss-on", _log);

        _picker.Click!();
        await WaitAsync(() => _flow.Picker is not null);
        Assert.Null(_picker.ButtonAt);
        Assert.Contains("dismiss-off", _log);
        Assert.Equal(new PixelPoint(100, 100), _picker.PickerAt);
    }

    [AvaloniaFact]
    public async Task Knopf_nie_im_Passwortfeld_nie_ohne_Markierung_nie_wenn_aus_oder_im_Profil_aus()
    {
        _rig.Focus.Kind = FocusKind.Password;
        await _flow.OnSelectionGestureAsync(new PixelPoint(1, 1));
        Assert.Null(_picker.ButtonAt);

        _rig.Focus.Kind = FocusKind.Editable;
        _uia.Next = new UiaSelection(true, "");
        await _flow.OnSelectionGestureAsync(new PixelPoint(1, 1));
        Assert.Null(_picker.ButtonAt);

        _uia.Next = new UiaSelection(true, "Text");
        var desktop = SettingsStore.Desktop(_f.Controller.Settings);
        desktop.Profiles.Add(new AppProfile { Process = "WINWORD.EXE", FloatingButton = false });
        SettingsStore.SetDesktop(_f.Controller.Settings, desktop);
        await _flow.OnSelectionGestureAsync(new PixelPoint(1, 1));
        Assert.Null(_picker.ButtonAt);

        _rig.Windows.Current = 7; // anderes Programm, kein Profil
        _rig.Windows.Alive.Add(7);
        _f.Controller.Settings.Selection.FloatingButton = false;
        await _flow.OnSelectionGestureAsync(new PixelPoint(1, 1));
        Assert.Null(_picker.ButtonAt);

        _f.Controller.Settings.Selection.FloatingButton = true;
        await _flow.OnSelectionGestureAsync(new PixelPoint(1, 1));
        Assert.NotNull(_picker.ButtonAt);
    }

    [AvaloniaFact]
    public async Task Knopf_verschwindet_bei_Taste_und_nach_Ablauf()
    {
        _uia.Next = new UiaSelection(false, ""); // keine Auskunft: Knopf erscheint, Klick erfasst per Strg+C
        await _flow.OnSelectionGestureAsync(new PixelPoint(5, 5));
        Assert.NotNull(_picker.ButtonAt);
        await _flow.HandleAsync(HotkeyAction.Dismiss);
        Assert.Null(_picker.ButtonAt);

        await _flow.OnSelectionGestureAsync(new PixelPoint(5, 5));
        _f.Now += InputFlow.ButtonDuration + TimeSpan.FromSeconds(1);
        _flow.Tick();
        Assert.Null(_picker.ButtonAt);
    }

    [AvaloniaFact]
    public async Task Tray_holt_das_Fenster_vor_dem_Tray_Klick_nach_vorn()
    {
        var flow = new InputFlow(_f.Controller, Sink, _ui, () => _rig.Windows.Current, _ => { })
        {
            Selection = Wiring(lastExternal: () => 42),
        };
        _rig.Windows.Current = 7; // Tray-Menü hatte den Fokus
        _rig.Keys.Selection = "aus dem Tray";

        Assert.Equal("picker", await flow.ProcessSelectionFromTrayAsync());
        Assert.Equal((IntPtr)42, _rig.Windows.Current);
        Assert.NotNull(flow.Picker);
    }

    [AvaloniaFact]
    public async Task Tray_ohne_bekanntes_Fenster_zeigt_einen_Hinweis()
    {
        Assert.Equal("no-target", await _flow.ProcessSelectionFromTrayAsync());
        Assert.Equal(Loc.Instance["Rewrite_TargetChanged"], _ui.Hints[^1].Message);
    }

    [AvaloniaFact]
    public async Task CLI_mit_Stufe_verarbeitet_ohne_Waehler()
    {
        _rig.Keys.Selection = "per CLI";

        Assert.Equal("done", await _flow.ProcessSelectionFromCliAsync("summarize"));

        Assert.Equal(0, _picker.PickerShows);
        Assert.Equal("[summarize] per CLI", _rig.Keys.Target);
        Assert.Equal("stage-unavailable", await _flow.ProcessSelectionFromCliAsync("off"));
    }

    [AvaloniaFact]
    public async Task Status_meldet_Waehler_und_markierte_Stufe()
    {
        _rig.Keys.Selection = "x";
        await _flow.HandleAsync(HotkeyAction.Selection);
        _flow.PickerKey(Key(0x28));

        var status = _flow.Status()["selection"]!;
        Assert.True((bool)status["pickerOpen"]!);
        Assert.Equal("beautify", (string?)status["highlighted"]);
        Assert.Equal(4, status["options"]!.AsArray().Count);
    }

    private async Task WaitIdleAsync() => await WaitAsync(() => _f.Controller.State == DictationState.Idle && _flow.Picker is null);

    private static async Task WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(10);
        }
        await Task.Delay(20);
        Assert.True(done());
    }
}
