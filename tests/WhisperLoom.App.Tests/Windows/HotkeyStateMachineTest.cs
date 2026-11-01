using WhisperLoom.App.Platform.Windows.Input;
using static WhisperLoom.App.Platform.Windows.Input.Hotkey;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Zustandsmaschine der Tastenkürzel aus synthetischen Ereignisfolgen (Standard-Kombis).</summary>
public class HotkeyStateMachineTest
{
    private const int VkRight = 0x27;

    private readonly HotkeyStateMachine _m = new(DefaultHold, DefaultToggle, DefaultStage);

    private HookDecision Down(int vk, bool own = false) => _m.OnKey(new KeyInput(vk, true, own));

    private HookDecision Up(int vk, bool own = false) => _m.OnKey(new KeyInput(vk, false, own));

    [Fact]
    public void Hold_ctrl_then_win_starts_and_win_release_ends_with_start_menu_mask()
    {
        Assert.Equal(HookDecision.Pass, Down(VkLControl));
        var start = Down(VkLWin);
        Assert.Equal(HotkeyAction.HoldStart, start.Action);
        Assert.False(start.Swallow);
        Assert.True(_m.Holding);

        var end = Up(VkLWin);
        Assert.Equal(HotkeyAction.HoldEnd, end.Action);
        Assert.True(end.Swallow);
        Assert.True(end.ReplayWinUpMasked);
        Assert.Equal(HookDecision.Pass, Up(VkLControl));
    }

    [Fact]
    public void Hold_win_then_ctrl_release_ctrl_first_still_masks_win()
    {
        Down(VkRWin);
        Assert.Equal(HotkeyAction.HoldStart, Down(VkRControl).Action);
        var ctrlUp = Up(VkRControl);
        Assert.Equal(HotkeyAction.HoldEnd, ctrlUp.Action);
        Assert.False(ctrlUp.Swallow);
        var winUp = Up(VkRWin);
        Assert.Equal(HotkeyAction.None, winUp.Action);
        Assert.True(winUp.ReplayWinUpMasked);
        Assert.True(winUp.Swallow);
    }

    [Fact]
    public void Hold_is_cancelled_by_another_key_which_passes()
    {
        Down(VkLControl);
        Down(VkLWin);
        var arrow = Down(VkRight);
        Assert.Equal(HotkeyAction.HoldCancel, arrow.Action);
        Assert.False(arrow.Swallow);
        Assert.False(_m.Holding);
        Assert.Equal(HotkeyAction.None, Up(VkRight).Action);
        Assert.Equal(HotkeyAction.None, Up(VkLWin).Action);
    }

    [Fact]
    public void Hold_needs_exact_modifiers()
    {
        Down(VkLControl);
        Down(VkLShift);
        Assert.Equal(HotkeyAction.None, Down(VkLWin).Action);
        Assert.False(_m.Holding);
    }

    [Fact]
    public void Plain_win_press_is_not_masked()
    {
        Down(VkLWin);
        var up = Up(VkLWin);
        Assert.False(up.ReplayWinUpMasked);
        Assert.False(up.Swallow);
    }

    [Fact]
    public void Toggle_swallows_space_down_repeat_and_up_in_any_release_order()
    {
        Down(VkLControl);
        var press = Down(VkSpace);
        Assert.Equal(HotkeyAction.Toggle, press.Action);
        Assert.True(press.Swallow);
        var repeat = Down(VkSpace);
        Assert.True(repeat.Swallow);
        Assert.Equal(HotkeyAction.None, repeat.Action);
        Assert.Equal(HookDecision.Pass, Up(VkLControl));
        Assert.True(Up(VkSpace).Swallow);
        // Danach ist Leertaste wieder normal.
        Assert.Equal(HookDecision.Pass, Down(VkSpace));
    }

    [Fact]
    public void Stage_combo_does_not_trigger_toggle()
    {
        Down(VkLControl);
        Down(VkLMenu);
        var press = Down(VkSpace);
        Assert.Equal(HotkeyAction.Stage, press.Action);
        Assert.True(press.Swallow);
    }

    [Fact]
    public void Space_without_ctrl_passes()
    {
        Assert.Equal(HookDecision.Pass, Down(VkSpace));
        Assert.Equal(HookDecision.Pass, Up(VkSpace));
    }

    [Fact]
    public void Overlay_keys_only_while_active()
    {
        Assert.Equal(HookDecision.Pass, Down(VkReturn));
        Up(VkReturn);
        _m.OverlayKeysActive = true;
        var enter = Down(VkReturn);
        Assert.Equal(HotkeyAction.OverlaySend, enter.Action);
        Assert.True(enter.Swallow);
        Assert.True(Up(VkReturn).Swallow);

        Down(VkLControl);
        Assert.Equal(HotkeyAction.OverlaySendEnter, Down(VkReturn).Action);
        Up(VkReturn);
        Up(VkLControl);

        var back = Down(VkBack);
        Assert.Equal(HotkeyAction.OverlayDiscard, back.Action);
        Assert.True(back.Swallow);
        Assert.True(Up(VkBack).Swallow);

        // Shift+Enter gehört nicht zum Overlay.
        Down(VkLShift);
        Assert.Equal(HookDecision.Pass, Down(VkReturn));
    }

    [Fact]
    public void Second_toggle_press_while_overlay_open_is_toggle()
    {
        _m.OverlayKeysActive = true;
        Down(VkLControl);
        Assert.Equal(HotkeyAction.Toggle, Down(VkSpace).Action);
    }

    [Fact]
    public void Own_injected_events_pass_and_change_nothing()
    {
        Down(VkLControl);
        // Eigenes Strg+V samt Loslassen von Strg: der Nutzer hält Strg aber weiter.
        Assert.Equal(HookDecision.Pass, Down(VkControl, own: true));
        Assert.Equal(HookDecision.Pass, Down('V', own: true));
        Assert.Equal(HookDecision.Pass, Up('V', own: true));
        Assert.Equal(HookDecision.Pass, Up(VkLControl, own: true));
        Assert.Equal(HotkeyAction.Toggle, Down(VkSpace).Action);

        _m.OverlayKeysActive = true;
        Assert.Equal(HookDecision.Pass, Down(VkReturn, own: true));
        Assert.Equal(HookDecision.Pass, Down(VkMaskKey, own: true));
        Assert.Equal(HookDecision.Pass, Up(VkLWin, own: true));
    }

    [Fact]
    public void Resync_drops_lost_modifier_so_space_alone_does_not_toggle()
    {
        Down(VkLControl);
        // Loslassen ging verloren (z. B. Sperrbildschirm).
        Assert.Equal(HotkeyAction.None, _m.Resync(_ => false, VkSpace));
        Assert.Equal(HookDecision.Pass, Down(VkSpace));
    }

    [Fact]
    public void Resync_cancels_hold_when_its_modifier_is_lost()
    {
        Down(VkLControl);
        Down(VkLWin);
        Assert.Equal(HotkeyAction.HoldCancel, _m.Resync(vk => vk == VkLWin, 'A'));
        Assert.False(_m.Holding);
    }

    [Fact]
    public void Resync_keeps_modifiers_that_are_down()
    {
        Down(VkLControl);
        _m.Resync(_ => true, VkSpace);
        Assert.Equal(HotkeyAction.Toggle, Down(VkSpace).Action);
    }

    [Fact]
    public void Hold_with_key_swallows_key_and_ends_on_key_release()
    {
        _m.Hold = Parse("Ctrl+Shift+H");
        Down(VkLControl);
        Down(VkLShift);
        var start = Down('H');
        Assert.Equal(HotkeyAction.HoldStart, start.Action);
        Assert.True(start.Swallow);
        Assert.True(Down('H').Swallow);
        var end = Up('H');
        Assert.Equal(HotkeyAction.HoldEnd, end.Action);
        Assert.True(end.Swallow);
    }

    [Fact]
    public void Recorder_records_modifiers_and_key_and_swallows_everything()
    {
        _m.StartRecording();
        Assert.True(Down(VkLControl).Swallow);
        Assert.True(Down(VkLMenu).Swallow);
        Assert.True(Down('K').Swallow);
        Assert.True(Up('K').Swallow);
        Assert.True(Up(VkLMenu).Swallow);
        var done = Up(VkLControl);
        Assert.True(done.Swallow);
        Assert.Equal(HotkeyAction.Recorded, done.Action);
        Assert.Equal("Ctrl+Alt+K", done.Recorded!.ToString());
        Assert.False(_m.Recording);
    }

    [Fact]
    public void Recorder_records_modifier_only_combo_and_esc_cancels()
    {
        _m.StartRecording();
        Down(VkLControl);
        Down(VkLWin);
        Up(VkLControl);
        Assert.Equal(DefaultHold, Up(VkLWin).Recorded);
        Assert.False(Up(VkLWin).ReplayWinUpMasked);

        _m.StartRecording();
        Down(VkEscape);
        Assert.Equal(HotkeyAction.RecordCancelled, Up(VkEscape).Action);
    }

    [Fact]
    public void Disabled_combos_do_nothing()
    {
        var off = new HotkeyStateMachine(null, null, null);
        off.OnKey(new KeyInput(VkLControl, true));
        Assert.Equal(HookDecision.Pass, off.OnKey(new KeyInput(VkLWin, true)));
        Assert.Equal(HookDecision.Pass, off.OnKey(new KeyInput(VkSpace, true)));
    }
}
