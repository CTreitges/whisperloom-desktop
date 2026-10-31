namespace WhisperLoom.App.Platform.Windows.Input;

public enum HotkeyAction
{
    None,
    HoldStart,
    HoldEnd,
    HoldCancel,
    Toggle,
    Stage,
    OverlaySend,
    OverlaySendEnter,
    OverlayDiscard,
    Recorded,
    RecordCancelled,
}

/// <summary>Ein Tastenereignis aus dem Hook. <paramref name="Own"/> = von uns selbst injiziert (dwExtraInfo-Kennung).</summary>
public readonly record struct KeyInput(int Vk, bool Down, bool Own = false);

/// <summary>
/// Antwort auf ein Ereignis. <see cref="Swallow"/>: Ereignis nicht weiterreichen. <see cref="ReplayWinUpMasked"/>:
/// das (geschluckte) Loslassen von Win als eigene Eingabe nachspielen, davor die Maskentaste — so öffnet sich kein Startmenü.
/// </summary>
public sealed record HookDecision(bool Swallow, HotkeyAction Action = HotkeyAction.None, bool ReplayWinUpMasked = false, Hotkey? Recorded = null)
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

    /// <summary>Overlay offen und in Aufnahme/Pause: Enter, Strg+Enter und Rücktaste gehören dem Overlay.</summary>
    public bool OverlayKeysActive { get; set; }

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
            return HookDecision.Pass;
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
        if (OverlayKeysActive)
        {
            if (vk == Hotkey.VkReturn && mods == HotkeyModifiers.None)
            {
                return HotkeyAction.OverlaySend;
            }
            if (vk == Hotkey.VkReturn && mods == HotkeyModifiers.Ctrl)
            {
                return HotkeyAction.OverlaySendEnter;
            }
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
        return HotkeyAction.None;
    }

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
