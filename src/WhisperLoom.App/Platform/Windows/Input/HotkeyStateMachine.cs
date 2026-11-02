namespace WhisperLoom.App.Platform.Windows.Input;

public enum HotkeyAction
{
    None,
    HoldStart,
    HoldEnd,
    HoldCancel,
    Toggle,
    Stage,
    Reinsert,

    /// <summary>Markierung verarbeiten (W2-SEL); während einer Aufnahme der Anweisung sendet der zweite Druck.</summary>
    Selection,

    /// <summary>Taste für den Stufen-Wähler am Cursor (<see cref="HookDecision.PickerKey"/>).</summary>
    PickerKey,

    /// <summary>Andere Taste, während Wähler oder schwebender Knopf sichtbar sind: schließen (die Taste läuft durch).</summary>
    Dismiss,
    OverlaySend,
    OverlayDiscard,

    /// <summary>Escape während einer Textverbesserung (W3-NEXT): ohne KI weiter, Escape wird geschluckt.</summary>
    SkipRefine,
    Recorded,
    RecordCancelled,
}

/// <summary>Ein Tastenereignis aus dem Hook. <paramref name="Own"/> = von uns selbst injiziert (dwExtraInfo-Kennung).</summary>
public readonly record struct KeyInput(int Vk, bool Down, bool Own = false);

/// <summary>
/// Antwort auf ein Ereignis. <see cref="Swallow"/>: Ereignis nicht weiterreichen. <see cref="ReplayWinUpMasked"/>:
/// das (geschluckte) Loslassen von Win als eigene Eingabe nachspielen, davor die Maskentaste — so öffnet sich kein Startmenü.
/// </summary>
/// <param name="PickerKey">Bei <see cref="HotkeyAction.PickerKey"/>: die Taste mit Modifiern (z. B. Umschalt+Tab).</param>
public sealed record HookDecision(bool Swallow, HotkeyAction Action = HotkeyAction.None, bool ReplayWinUpMasked = false, Hotkey? Recorded = null,
    Hotkey? PickerKey = null)
{
    public static readonly HookDecision Pass = new(false);
}

/// <summary>
/// Zustandsmaschine der globalen Tastenkürzel, ohne Win32: Halten (Aufnahme solange gedrückt), Umschalten, Stufe;
/// solange das Overlay Eingaben annimmt zusätzlich Enter, Strg+Enter und Rücktaste; dazu der Rekorder-Modus.
/// Geschluckt werden nur die Nicht-Modifier-Tasten eines Treffers (Druck, Wiederholungen und Loslassen), Modifier laufen
/// immer durch. Eigene injizierte Eingaben ändern nichts. Nicht threadsicher: nur vom Hook-Thread aufrufen.
/// </summary>
public sealed class HotkeyStateMachine
{
    private readonly HashSet<int> _down = [];
    private readonly HashSet<int> _swallowed = [];
    private bool _holding;
    private bool _winMaskPending;
    private HotkeyModifiers _recordModifiers;
    private int _recordKey;

    public HotkeyStateMachine(Hotkey? hold, Hotkey? toggle, Hotkey? stage)
    {
        Hold = hold;
        Toggle = toggle;
        Stage = stage;
    }

    public Hotkey? Hold { get; set; }

    public Hotkey? Toggle { get; set; }

    public Hotkey? Stage { get; set; }

    /// <summary>Letztes Diktat erneut einfügen (WLD-P5b).</summary>
    public Hotkey? Reinsert { get; set; }

    /// <summary>Markierung verarbeiten (W2-SEL); zweiter Druck sendet eine gesprochene Anweisung.</summary>
    public Hotkey? Selection { get; set; }

    /// <summary>
    /// Stufen-Wähler am Cursor offen (W2-SEL): ↑/↓, Tab/Umschalt+Tab, 1–9 (auch Ziffernblock), Enter und Esc gehören dem Wähler
    /// (geschluckt, <see cref="HotkeyAction.PickerKey"/>); jede andere Taste schließt ihn und läuft durch.
    /// </summary>
    public bool PickerKeysActive { get; set; }

    /// <summary>Schwebender Knopf sichtbar (W2-SEL): jede Taste lässt ihn verschwinden (<see cref="HotkeyAction.Dismiss"/>, läuft durch).</summary>
    public bool DismissOnKey { get; set; }

    /// <summary>Overlay offen und in Aufnahme/Pause: Enter, Strg+Enter und Rücktaste gehören dem Overlay.</summary>
    public bool OverlayKeysActive { get; set; }

    /// <summary>
    /// Läuft gerade eine Textverbesserung (W3-NEXT)? Dann gehört Escape ohne Modifier WhisperLoom (<see cref="HotkeyAction.SkipRefine"/>,
    /// geschluckt), sonst läuft es unverändert an die Vordergrund-App. Wird im Hook-Thread abgefragt.
    /// </summary>
    public Func<bool>? SkipKeyActive { get; set; }

    /// <summary>Rekorder aktiv: alle Tasten werden geschluckt, bis alles losgelassen ist (Esc bricht ab).</summary>
    public bool Recording { get; private set; }

    public bool Holding => _holding;

    public void StartRecording()
    {
        Recording = true;
        _recordModifiers = HotkeyModifiers.None;
        _recordKey = 0;
    }

    /// <summary>Bricht den Rekorder ab (Zeitlimit). Ein noch gehaltenes Loslassen läuft danach normal durch.</summary>
    public void StopRecording()
    {
        Recording = false;
        _swallowed.Clear();
    }

    /// <summary>
    /// Gleicht gemerkte Modifier mit dem echten Tastenzustand ab (verlorenes Loslassen, z. B. nach Sperrbildschirm).
    /// Nicht im Rekorder-Modus und nicht für geschluckte Tasten, deren Zustand das System nicht kennt.
    /// </summary>
    /// <returns><see cref="HotkeyAction.HoldCancel"/>, wenn ein Halten dadurch hinfällig ist.</returns>
    public HotkeyAction Resync(Func<int, bool> isDown, int currentVk)
    {
        if (Recording)
        {
            return HotkeyAction.None;
        }
        var lost = _down.Where(vk => vk != currentVk && !_swallowed.Contains(vk) && Hotkey.ModifierOf(vk) != HotkeyModifiers.None && !isDown(vk)).ToList();
        foreach (var vk in lost)
        {
            _down.Remove(vk);
        }
        if (lost.Count > 0 && _holding && Hold is not null && (Modifiers() & Hold.Modifiers) != Hold.Modifiers)
        {
            _holding = false;
            return HotkeyAction.HoldCancel;
        }
        return HotkeyAction.None;
    }

    public HookDecision OnKey(KeyInput e)
    {
        if (e.Own)
        {
            return HookDecision.Pass;
        }
        if (Recording)
        {
            return OnRecordKey(e);
        }
        return e.Down ? OnDown(e.Vk) : OnUp(e.Vk);
    }

    private HookDecision OnDown(int vk)
    {
        var repeat = !_down.Add(vk);
        if (_swallowed.Contains(vk))
        {
            return new HookDecision(true);
        }
        var mods = Modifiers();
        if (Hotkey.ModifierOf(vk) != HotkeyModifiers.None)
        {
            if (repeat)
            {
                return HookDecision.Pass;
            }
            if (_holding)
            {
                // Weiterer Modifier zur Halten-Kombi: der Nutzer meint ein anderes Kürzel.
                _holding = false;
                return new HookDecision(false, HotkeyAction.HoldCancel);
            }
            if (Hold is { IsModifierOnly: true } && mods == Hold.Modifiers && !AnyKeyDown())
            {
                _holding = true;
                _winMaskPending |= mods.HasFlag(HotkeyModifiers.Win);
                return new HookDecision(false, HotkeyAction.HoldStart);
            }
            return HookDecision.Pass;
        }
        if (PickerKeysActive && !repeat && IsPickerKey(vk, mods))
        {
            // Gehaltene Wiederholungen schluckt _swallowed oben; eine schon vor dem Öffnen gedrückte Taste bleibt der App.
            _swallowed.Add(vk);
            return new HookDecision(true, HotkeyAction.PickerKey, PickerKey: new Hotkey(mods, vk));
        }
        if (repeat)
        {
            return HookDecision.Pass;
        }
        if (_holding)
        {
            // Andere Taste während des Haltens (z. B. Strg+Win+Pfeil): Aufnahme verwerfen, Taste durchlassen.
            _holding = false;
            return new HookDecision(false, HotkeyAction.HoldCancel);
        }
        var action = Match(vk, mods);
        if (action == HotkeyAction.None)
        {
            return PickerKeysActive || DismissOnKey ? new HookDecision(false, HotkeyAction.Dismiss) : HookDecision.Pass;
        }
        _swallowed.Add(vk);
        if (action == HotkeyAction.HoldStart)
        {
            _holding = true;
        }
        _winMaskPending |= mods.HasFlag(HotkeyModifiers.Win);
        return new HookDecision(true, action);
    }

    private HotkeyAction Match(int vk, HotkeyModifiers mods)
    {
        if (vk == Hotkey.VkEscape && mods == HotkeyModifiers.None && SkipKeyActive?.Invoke() == true)
        {
            return HotkeyAction.SkipRefine;
        }
        if (OverlayKeysActive)
        {
            if (vk == Hotkey.VkReturn && mods == HotkeyModifiers.None)
            {
                return HotkeyAction.OverlaySend;
            }
            // Strg+Enter gibt es nicht mehr (W2-OUT): Enter danach ist nur noch Einstellung (output.pressEnter, App-Profil).
            // Die Taste geht unverändert an das Zielfenster.
            if (vk == Hotkey.VkBack && mods == HotkeyModifiers.None)
            {
                return HotkeyAction.OverlayDiscard;
            }
        }
        if (Hold is { IsModifierOnly: false } && Hold.Key == vk && Hold.Modifiers == mods)
        {
            return HotkeyAction.HoldStart;
        }
        if (Toggle is not null && Toggle.Key == vk && Toggle.Modifiers == mods)
        {
            return HotkeyAction.Toggle;
        }
        if (Stage is not null && Stage.Key == vk && Stage.Modifiers == mods)
        {
            return HotkeyAction.Stage;
        }
        if (Reinsert is not null && Reinsert.Key == vk && Reinsert.Modifiers == mods)
        {
            return HotkeyAction.Reinsert;
        }
        if (Selection is not null && Selection.Key == vk && Selection.Modifiers == mods)
        {
            return HotkeyAction.Selection;
        }
        return HotkeyAction.None;
    }

    /// <summary>
    /// Wähler-Taste: Strg darf noch gehalten sein (Strg+Rechtsklick), Umschalt nur bei Tab (Umschalt+Tab); Umschalt+Ziffer ist ein
    /// Zeichen für die App, Alt/Win gehören zu anderen Kürzeln.
    /// </summary>
    private static bool IsPickerKey(int vk, HotkeyModifiers mods)
    {
        var rest = mods & ~HotkeyModifiers.Ctrl;
        return PickerKeys.Contains(vk) && (rest == HotkeyModifiers.None || (vk == 0x09 && rest == HotkeyModifiers.Shift));
    }

    /// <summary>Tasten des Stufen-Wählers: Tab, Enter, Esc, ↑, ↓, 1–9 und Ziffernblock 1–9.</summary>
    private static readonly HashSet<int> PickerKeys =
        [0x09, Hotkey.VkReturn, Hotkey.VkEscape, 0x26, 0x28, .. Enumerable.Range('1', 9), .. Enumerable.Range(0x61, 9)];

    private HookDecision OnUp(int vk)
    {
        _down.Remove(vk);
        var swallow = _swallowed.Remove(vk);
        var action = HotkeyAction.None;
        if (_holding && Hold is not null && (vk == Hold.Key || Hold.Modifiers.HasFlag(Hotkey.ModifierOf(vk)) && Hotkey.ModifierOf(vk) != HotkeyModifiers.None))
        {
            _holding = false;
            action = HotkeyAction.HoldEnd;
        }
        if (Hotkey.ModifierOf(vk) == HotkeyModifiers.Win && _winMaskPending)
        {
            _winMaskPending = false;
            return new HookDecision(true, action, ReplayWinUpMasked: true);
        }
        return new HookDecision(swallow, action);
    }

    private HookDecision OnRecordKey(KeyInput e)
    {
        if (e.Down)
        {
            _down.Add(e.Vk);
            _swallowed.Add(e.Vk);
            var mod = Hotkey.ModifierOf(e.Vk);
            if (e.Vk == Hotkey.VkEscape && Modifiers() == HotkeyModifiers.None)
            {
                _recordKey = -1;
            }
            else if (mod != HotkeyModifiers.None)
            {
                _recordModifiers |= mod;
            }
            else if (_recordKey == 0)
            {
                _recordKey = e.Vk;
            }
            return new HookDecision(true);
        }
        _down.Remove(e.Vk);
        var swallow = _swallowed.Remove(e.Vk);
        if (_down.Count > 0 || (_recordModifiers == HotkeyModifiers.None && _recordKey == 0))
        {
            return new HookDecision(swallow);
        }
        Recording = false;
        _swallowed.Clear();
        if (_recordKey == -1)
        {
            return new HookDecision(swallow, HotkeyAction.RecordCancelled);
        }
        return new HookDecision(swallow, HotkeyAction.Recorded, Recorded: new Hotkey(_recordModifiers, _recordKey));
    }

    private HotkeyModifiers Modifiers()
    {
        var mods = HotkeyModifiers.None;
        foreach (var vk in _down)
        {
            mods |= Hotkey.ModifierOf(vk);
        }
        return mods;
    }

    private bool AnyKeyDown() => _down.Any(vk => Hotkey.ModifierOf(vk) == HotkeyModifiers.None);
}
