using System.Text.Json.Nodes;
using Avalonia;
using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Bausteine von „Markierung verarbeiten“ (W2-SEL) ohne Fenster: Erfassung, Tastatur- und Maus-Zustandsmaschine, Migration.</summary>
public sealed class SelectionPartsTest
{
    // --- Erfassung: UIA zuerst, Rückfall Strg+C mit Sicherung -----------------------------------------------------------

    private static (InsertRig Rig, FakeSelectionSource Uia, SelectionReader Reader) Reader(TimeSpan? timeout = null)
    {
        var rig = new InsertRig();
        var uia = new FakeSelectionSource();
        var reader = new SelectionReader(rig.Focus, uia, new SelectionCapture(rig.Clipboard, rig.Keys, rig.Windows, TimeSpan.Zero), rig.Windows,
            timeout ?? TimeSpan.FromSeconds(5));
        return (rig, uia, reader);
    }

    [Fact]
    public async Task UIA_Markierung_ohne_Zwischenablage()
    {
        var (rig, uia, reader) = Reader();
        uia.Next = new UiaSelection(true, "Hallo");

        var read = await reader.ReadAsync(42, false, CancellationToken.None);

        Assert.Equal(new SelectionRead(SelectionReadKind.Text, "Hallo"), read);
        Assert.Empty(rig.Steps);
    }

    [Fact]
    public async Task UIA_leere_Markierung_ist_leer_ohne_Strg_C()
    {
        var (rig, uia, reader) = Reader();
        uia.Next = new UiaSelection(true, "  ");
        rig.Keys.Selection = "ganze Zeile"; // VS Code o. Ä. kopierten ohne Markierung die Zeile

        Assert.Equal(SelectionRead.Empty, await reader.ReadAsync(42, false, CancellationToken.None));
        Assert.DoesNotContain("ctrl-c", rig.Steps);
    }

    [Fact]
    public async Task Ohne_TextPattern_Strg_C_und_Zwischenablage_danach_wie_vorher()
    {
        var (rig, uia, reader) = Reader();
        uia.Next = new UiaSelection(false, "");
        rig.Keys.Selection = "markiert";
        rig.Clipboard.Text = "vorher";

        var read = await reader.ReadAsync(42, false, CancellationToken.None);

        Assert.Equal(new SelectionRead(SelectionReadKind.Text, "markiert", ViaClipboard: true), read);
        Assert.Equal(["wait-modifiers", "save", "ctrl-c", "get-text", "restore"], rig.Steps);
        Assert.Equal("vorher", rig.Clipboard.Text);
    }

    [Fact]
    public async Task Konsole_kopiert_mit_Strg_Einfg_und_nichts_markiert_ist_leer()
    {
        var (rig, _, reader) = Reader();
        Assert.Equal(SelectionRead.Empty, await reader.ReadAsync(42, true, CancellationToken.None));
        Assert.Contains("ctrl-insert", rig.Steps);
        Assert.Equal("alt", rig.Clipboard.Text);
    }

    [Fact]
    public async Task Passwortfeld_liest_gar_nichts()
    {
        var (rig, uia, reader) = Reader();
        rig.Focus.Kind = FocusKind.Password;
        rig.Keys.Selection = "geheim";

        Assert.Equal(SelectionRead.Password, await reader.ReadAsync(42, false, CancellationToken.None));
        Assert.Equal(0, uia.Calls);
        Assert.Empty(rig.Steps);
        Assert.False(await reader.MaySelectAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Haengendes_UIA_faellt_nach_der_Zeitgrenze_auf_Strg_C_zurueck()
    {
        var rig = new InsertRig();
        var reader = new SelectionReader(rig.Focus, new HangingSource(), new SelectionCapture(rig.Clipboard, rig.Keys, rig.Windows, TimeSpan.Zero),
            rig.Windows, TimeSpan.FromMilliseconds(50));
        rig.Keys.Selection = "trotzdem";

        Assert.Equal("trotzdem", (await reader.ReadAsync(42, false, CancellationToken.None)).Text);
    }

    [Fact]
    public async Task Anderes_Fenster_vorn_oder_Admin_Fenster_keine_Erfassung()
    {
        var (rig, _, reader) = Reader();
        rig.Windows.Current = 7;
        Assert.Equal("Rewrite_TargetChanged", (await reader.ReadAsync(42, false, CancellationToken.None)).ToastKey);
        rig.Windows.Current = 42;
        rig.Windows.Elevated.Add(42);
        Assert.Equal("Rewrite_TargetProtected", (await reader.ReadAsync(42, false, CancellationToken.None)).ToastKey);
        Assert.Empty(rig.Steps);
    }

    [Fact]
    public async Task Knopf_Vorpruefung_ohne_Zwischenablage()
    {
        var (rig, uia, reader) = Reader();
        uia.Next = new UiaSelection(true, "Wort");
        Assert.True(await reader.MaySelectAsync(CancellationToken.None));
        uia.Next = new UiaSelection(true, "");
        Assert.False(await reader.MaySelectAsync(CancellationToken.None));
        uia.Next = null; // keine Auskunft
        Assert.True(await reader.MaySelectAsync(CancellationToken.None));
        Assert.Empty(rig.Steps);
    }

    private sealed class HangingSource : ISelectionTextSource
    {
        public UiaSelection? Selection()
        {
            Thread.Sleep(2000);
            return new UiaSelection(true, "zu spät");
        }
    }

    // --- Tastatur-Hook: Wähler-Tasten ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0x28, HotkeyModifiers.None)] // ↓
    [InlineData(0x26, HotkeyModifiers.None)] // ↑
    [InlineData(0x09, HotkeyModifiers.Shift)] // Umschalt+Tab
    [InlineData('3', HotkeyModifiers.None)]
    [InlineData(0x63, HotkeyModifiers.None)] // Ziffernblock 3
    [InlineData(Hotkey.VkReturn, HotkeyModifiers.None)]
    [InlineData(Hotkey.VkEscape, HotkeyModifiers.None)]
    public void Waehler_Tasten_werden_geschluckt_und_weitergereicht(int vk, HotkeyModifiers mods)
    {
        var m = new HotkeyStateMachine(null, null, null) { PickerKeysActive = true };
        if (mods.HasFlag(HotkeyModifiers.Shift))
        {
            m.OnKey(new KeyInput(Hotkey.VkLShift, true));
        }

        var decision = m.OnKey(new KeyInput(vk, true));

        Assert.Equal(new HookDecision(true, HotkeyAction.PickerKey, PickerKey: new Hotkey(mods, vk)), decision);
        Assert.True(m.OnKey(new KeyInput(vk, false)).Swallow);
    }

    [Fact]
    public void Andere_Taste_schliesst_den_Waehler_und_laeuft_durch_Modifier_allein_nicht()
    {
        var m = new HotkeyStateMachine(null, null, null) { PickerKeysActive = true };
        Assert.Equal(HookDecision.Pass, m.OnKey(new KeyInput(Hotkey.VkLControl, true)));
        Assert.Equal(new HookDecision(false, HotkeyAction.Dismiss), m.OnKey(new KeyInput('C', true))); // Strg+C: nicht seine Taste
        m.OnKey(new KeyInput('C', false));
        m.OnKey(new KeyInput(Hotkey.VkLControl, false));
        Assert.Equal(new HookDecision(false, HotkeyAction.Dismiss), m.OnKey(new KeyInput('A', true)));

        m.PickerKeysActive = false;
        Assert.Equal(HookDecision.Pass, m.OnKey(new KeyInput(0x28, true)));
        m.DismissOnKey = true; // Knopf sichtbar: jede Taste lässt ihn verschwinden
        Assert.Equal(new HookDecision(false, HotkeyAction.Dismiss), m.OnKey(new KeyInput('B', true)));
    }

    [Fact]
    public void Gehaltenes_Strg_nach_Strg_Rechtsklick_bedient_den_Waehler_statt_der_Ziel_App()
    {
        var m = new HotkeyStateMachine(null, null, null) { PickerKeysActive = true };
        m.OnKey(new KeyInput(Hotkey.VkLControl, true));
        Assert.Equal(new HookDecision(true, HotkeyAction.PickerKey, PickerKey: new Hotkey(HotkeyModifiers.Ctrl, '2')), m.OnKey(new KeyInput('2', true)));
        Assert.Equal(HotkeyAction.PickerKey, m.OnKey(new KeyInput(Hotkey.VkReturn, true)).Action); // nicht Strg+Enter in Word
    }

    [Fact]
    public void Umschalt_Ziffer_ist_keine_Waehler_Taste_und_schliesst()
    {
        var m = new HotkeyStateMachine(null, null, null) { PickerKeysActive = true };
        m.OnKey(new KeyInput(Hotkey.VkLShift, true));
        Assert.Equal(new HookDecision(false, HotkeyAction.Dismiss), m.OnKey(new KeyInput('1', true))); // „!“ geht an die App
    }

    [Fact]
    public void Schon_gehaltene_Taste_bleibt_der_App()
    {
        var m = new HotkeyStateMachine(null, null, null);
        m.OnKey(new KeyInput(0x28, true)); // ↓ gedrückt, bevor der Wähler aufgeht
        m.PickerKeysActive = true;
        Assert.False(m.OnKey(new KeyInput(0x28, true)).Swallow); // Wiederholung
        Assert.False(m.OnKey(new KeyInput(0x28, false)).Swallow); // Loslassen erreicht die App
    }

    [Fact]
    public void Kombi_bleibt_auch_bei_offenem_Waehler_die_Kombi()
    {
        var m = new HotkeyStateMachine(null, null, null) { Selection = Hotkey.DefaultSelection, PickerKeysActive = true };
        foreach (var vk in new[] { Hotkey.VkLControl, Hotkey.VkLMenu, Hotkey.VkLShift })
        {
            m.OnKey(new KeyInput(vk, true));
        }
        Assert.Equal(new HookDecision(true, HotkeyAction.Selection), m.OnKey(new KeyInput('S', true)));
    }

    // --- Maus-Hook: Zustandsmaschine (Callback ohne blockierende Arbeit) ----------------------------------------------

    private static MouseInput M(MouseKind kind, int x = 10, int y = 10, bool ctrl = false, long t = 0, bool own = false) => new(kind, x, y, ctrl, t, own);

    [Fact]
    public void Strg_Rechtsklick_schluckt_Druecken_und_Loslassen()
    {
        var g = new MouseGestures { CtrlRightClick = true };
        Assert.Equal(new MouseDecision(true, MouseAction.CtrlRightClick, 10, 10), g.OnMouse(M(MouseKind.RightDown, ctrl: true)));
        Assert.Equal(new MouseDecision(true), g.OnMouse(M(MouseKind.RightUp, ctrl: true)));
        // Ohne Strg, ausgeschaltet oder eigener nachgespielter Klick: normal.
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.RightDown)));
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.RightUp)));
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.RightDown, ctrl: true, own: true)));
        g.CtrlRightClick = false;
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.RightDown, ctrl: true)));
    }

    [Fact]
    public void Ziehen_oder_Doppelklick_ist_eine_Markier_Geste_einfacher_Klick_nicht()
    {
        var g = new MouseGestures { FloatingButton = true };
        g.OnMouse(M(MouseKind.LeftDown, 10, 10, t: 0));
        Assert.Equal(new MouseDecision(false, MouseAction.SelectionGesture, 60, 12), g.OnMouse(M(MouseKind.LeftUp, 60, 12, t: 300)));

        g.OnMouse(M(MouseKind.LeftDown, 100, 100, t: 1000));
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.LeftUp, 101, 100, t: 1050)));
        g.OnMouse(M(MouseKind.LeftDown, 101, 100, t: 1200));
        Assert.Equal(MouseAction.SelectionGesture, g.OnMouse(M(MouseKind.LeftUp, 101, 101, t: 1250)).Action);

        g.OnMouse(M(MouseKind.LeftDown, 300, 300, t: 5000));
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.LeftUp, 300, 300, t: 5050)));
        g.OnMouse(M(MouseKind.LeftDown, 300, 300, t: 6000)); // zu spät für einen Doppelklick
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.LeftUp, 300, 300, t: 6050)));

        g.FloatingButton = false;
        g.OnMouse(M(MouseKind.LeftDown, 0, 0));
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.LeftUp, 200, 0)));
    }

    [Fact]
    public void Klick_daneben_oder_Mausrad_schliesst_Klick_auf_den_Knopf_nicht()
    {
        var g = new MouseGestures { FloatingButton = true, Transient = [new ScreenRect(100, 100, 150, 120)] };
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.LeftDown, 120, 110)));
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.LeftUp, 120, 110)));
        Assert.Equal(new MouseDecision(false, MouseAction.Dismiss), g.OnMouse(M(MouseKind.LeftDown, 10, 10)));
        Assert.Equal(new MouseDecision(false, MouseAction.Dismiss), g.OnMouse(M(MouseKind.Wheel)));
        Assert.Equal(new MouseDecision(false, MouseAction.Dismiss), g.OnMouse(M(MouseKind.RightDown, 10, 10)));
        g.Transient = [];
        Assert.Equal(MouseDecision.Pass, g.OnMouse(M(MouseKind.Wheel)));
    }

    [Fact]
    public void Entscheidung_des_Callbacks_dauert_unter_einer_Millisekunde()
    {
        var g = new MouseGestures { FloatingButton = true, CtrlRightClick = true, Transient = [new ScreenRect(0, 0, 10, 10)] };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10_000; i++)
        {
            g.OnMouse(M((MouseKind)(i % 5), i % 500, i % 300, ctrl: i % 7 == 0, t: i));
        }
        Assert.True(watch.Elapsed.TotalMilliseconds / 10_000 < 1, $"{watch.Elapsed.TotalMilliseconds / 10_000:F4} ms je Ereignis");
    }

    // --- Maus-Hook nur bei eingeschaltetem Starter -------------------------------------------------------------------------

    private sealed class FakeMouseHook : IMouseHook
    {
        public MouseGestures Gestures { get; } = new();

        public bool Disposed { get; private set; }

        public bool Start() => true;

        public void Update(Action<MouseGestures> change) => change(Gestures);

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Hook_nur_solange_ein_Maus_Starter_an_ist()
    {
        var hooks = new List<FakeMouseHook>();
        using var sw = new MouseHookSwitch(() =>
        {
            var h = new FakeMouseHook();
            hooks.Add(h);
            return h;
        });

        sw.Apply(new SelectionSettings());
        Assert.False(sw.Installed);
        Assert.Empty(hooks);

        sw.Apply(new SelectionSettings { CtrlRightClick = true });
        sw.Apply(new SelectionSettings { CtrlRightClick = true, FloatingButton = true });
        var hook = Assert.Single(hooks);
        Assert.True(hook.Gestures.FloatingButton && hook.Gestures.CtrlRightClick);

        sw.Apply(new SelectionSettings());
        Assert.True(hook.Disposed);
        Assert.False(sw.Installed);
    }

    [Fact]
    public void Verweigerter_Hook_wird_nicht_behalten()
    {
        using var sw = new MouseHookSwitch(() => new RefusingHook());
        Assert.False(sw.Apply(new SelectionSettings { FloatingButton = true }));
        Assert.False(sw.Installed);
    }

    private sealed class RefusingHook : IMouseHook
    {
        public bool Start() => false;

        public void Update(Action<MouseGestures> change)
        {
        }

        public void Dispose()
        {
        }
    }

    // --- Migration „Umschreiben“ → selection.hotkeyCombo ---------------------------------------------------------------

    private static Core.Contracts.Settings.WhisperLoomSettings WithOld(string? old, string? selection = null)
    {
        var json = new JsonObject { ["desktop"] = new JsonObject { ["hotkeyHold"] = "Ctrl+Win" } };
        if (old is not null)
        {
            json["desktop"]!["hotkeyRewrite"] = old;
        }
        if (selection is not null)
        {
            json["selection"] = new JsonObject { ["hotkeyCombo"] = selection };
        }
        return SettingsJson.Deserialize(json.ToJsonString());
    }

    private static bool Valid(string combo) => Hotkey.Parse(combo) is not null;

    [Theory]
    [InlineData("Ctrl+Alt+Shift+R", "Ctrl+Alt+Shift+R")] // alter Standard: Muskelgedächtnis bleibt
    [InlineData("Ctrl+Alt+Shift+U", "Ctrl+Alt+Shift+U")]
    [InlineData("", SelectionSettings.DefaultHotkey)] // leer galt als Standard
    [InlineData("Quatsch+", SelectionSettings.DefaultHotkey)]
    public void Gespeicherte_Umschreiben_Kombi_wandert_nach_selection(string old, string expected)
    {
        var s = WithOld(old);

        Assert.True(SelectionMigration.Apply(s, Valid));

        Assert.Equal(expected, s.Selection.HotkeyCombo);
        var desktop = SettingsJson.Serialize(s);
        Assert.DoesNotContain("hotkeyRewrite", desktop);
        Assert.Contains("\"hotkeyHold\"", desktop);
        Assert.False(SelectionMigration.Apply(s, Valid)); // nur einmal
    }

    [Fact]
    public void Eigene_Selection_Kombi_gewinnt_ohne_alten_Schluessel_nichts_zu_tun()
    {
        var s = WithOld("Ctrl+Alt+Shift+U", selection: "Ctrl+Shift+F9");
        Assert.True(SelectionMigration.Apply(s, Valid));
        Assert.Equal("Ctrl+Shift+F9", s.Selection.HotkeyCombo);

        Assert.False(SelectionMigration.Apply(WithOld(null), Valid));
    }

    [Fact]
    public void SettingsStore_migriert_beim_Laden()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-sel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"desktop\":{\"hotkeyRewrite\":\"Ctrl+Alt+Shift+U\"}}");
            var store = new SettingsStore(dir);
            var loaded = store.Load();
            Assert.Equal("Ctrl+Alt+Shift+U", loaded.Selection.HotkeyCombo);
            store.Save(loaded);
            Assert.DoesNotContain("hotkeyRewrite", File.ReadAllText(store.FilePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // --- Lage am Cursor -----------------------------------------------------------------------------------------------

    [Fact]
    public void Waehler_unter_dem_Punkt_sonst_darueber_immer_im_Arbeitsbereich()
    {
        var work = new PixelRect(1920, 0, 2560, 1400); // zweiter Monitor, 150 %
        Assert.Equal(new PixelPoint(2000, 109), SelectionPlacement.Near(work, 1.5, new Size(260, 200), new PixelPoint(2000, 100)));
        // Unten kein Platz: darüber.
        Assert.Equal(new PixelPoint(2000, 1391 - 300), SelectionPlacement.Near(work, 1.5, new Size(260, 200), new PixelPoint(2000, 1400)));
        // Rechts kein Platz: nach links geschoben.
        Assert.Equal(4480 - 390, SelectionPlacement.Near(work, 1.5, new Size(260, 200), new PixelPoint(4470, 100)).X);
    }
}
