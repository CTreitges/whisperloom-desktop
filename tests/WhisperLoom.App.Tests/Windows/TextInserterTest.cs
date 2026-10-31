using WhisperLoom.App.Platform.Windows.TextOutput;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Einfüge-Ablauf mit Fake-Adaptern: Sicherung/Wiederherstellung, Strg+Enter, Ziel weg, UIPI, Tippen.</summary>
public class TextInserterTest
{
    private readonly InsertRig _rig = new();

    private Task<InsertOutcome> Insert(string text = "Hallo Welt", InsertMode mode = InsertMode.Paste, bool enter = false, IntPtr? target = null) =>
        _rig.Inserter.InsertAsync(target ?? 42, text, mode, enter, CancellationToken.None);

    [Fact]
    public async Task Paste_saves_sets_private_pastes_and_restores_the_clipboard()
    {
        Assert.Equal(InsertOutcome.Pasted, await Insert());
        Assert.Equal(["wait-modifiers", "activate", "save", "set-private", "ctrl-v", "restore"], _rig.Steps);
        Assert.Equal("Hallo Welt", _rig.Keys.Target);
        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.Equal(0, _rig.Keys.Enters);
    }

    [Fact]
    public async Task Report_names_the_window_that_got_the_paste()
    {
        Assert.Null(_rig.Inserter.LastReport);
        await Insert();
        var report = _rig.Inserter.LastReport!;
        Assert.Equal((IntPtr)42, report.Window);
        Assert.True(report.SameTarget);
        Assert.Equal("proc-42", report.Process);
        Assert.Equal("Edit", report.FocusClass);
    }

    [Fact]
    public async Task Report_flags_a_different_foreground_window()
    {
        // Activate meldet Erfolg, beim Senden ist aber ein anderes Fenster vorn (Wettlauf mit dem Nutzer).
        _rig.Windows.Alive.Add(7);
        var inserter = new TextInserter(_rig.Clipboard, _rig.Keys, new SwitchingWindows(_rig.Windows, 7), TimeSpan.Zero);
        await inserter.InsertAsync(42, "x", InsertMode.Paste, false, CancellationToken.None);
        Assert.False(inserter.LastReport!.SameTarget);
        Assert.Equal("proc-7", inserter.LastReport.Process);
    }

    [Fact]
    public async Task Report_is_empty_when_nothing_was_sent()
    {
        await Insert();
        _rig.Windows.Alive.Clear();
        await Insert();
        Assert.Null(_rig.Inserter.LastReport);
    }

    [Fact]
    public async Task Paste_restores_an_empty_clipboard_as_empty()
    {
        _rig.Clipboard.Text = null;
        await Insert();
        Assert.Null(_rig.Clipboard.Text);
    }

    [Fact]
    public async Task Ctrl_enter_sends_enter_after_the_paste_and_before_the_restore()
    {
        Assert.Equal(InsertOutcome.Pasted, await Insert(enter: true));
        Assert.Equal(["wait-modifiers", "activate", "save", "set-private", "ctrl-v", "enter", "restore"], _rig.Steps);
        Assert.Equal(1, _rig.Keys.Enters);
    }

    [Fact]
    public async Task Enter_is_not_sent_when_the_user_switched_windows_after_the_paste()
    {
        _rig.Windows.Alive.Add(7);
        _rig.Keys.AfterPaste = () => _rig.Windows.Current = 7;
        Assert.Equal(InsertOutcome.Pasted, await Insert(enter: true));
        Assert.Equal(0, _rig.Keys.Enters);
        Assert.Contains("restore", _rig.Steps);
    }

    [Fact]
    public async Task Enter_after_typing_also_needs_the_target_in_front()
    {
        _rig.Windows.Alive.Add(7);
        _rig.Keys.AfterPaste = () => _rig.Windows.Current = 7;
        Assert.Equal(InsertOutcome.Typed, await Insert("x", InsertMode.Type, enter: true));
        Assert.Equal(0, _rig.Keys.Enters);
    }

    [Fact]
    public async Task Clipboard_is_restored_when_setting_the_private_text_fails()
    {
        _rig.Clipboard.FailPrivateSet = true;
        await Assert.ThrowsAsync<IOException>(() => Insert());
        Assert.Equal(["wait-modifiers", "activate", "save", "set-private", "restore"], _rig.Steps);
        Assert.Equal("alt", _rig.Clipboard.Text);
    }

    [Fact]
    public async Task Clipboard_is_restored_when_cancelled_after_the_paste()
    {
        using var cts = new CancellationTokenSource();
        _rig.Keys.AfterPaste = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _rig.Inserter.InsertAsync(42, "Hallo", InsertMode.Paste, true, cts.Token));
        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.Equal(0, _rig.Keys.Enters);
    }

    [Fact]
    public async Task Restore_is_skipped_when_someone_else_copied_meanwhile()
    {
        _rig.Clipboard.ChangedByOthers = true;
        await Insert();
        Assert.DoesNotContain("restore", _rig.Steps);
        Assert.Equal("fremd", _rig.Clipboard.Text);
    }

    [Fact]
    public async Task Gone_target_puts_text_on_the_clipboard_without_keys()
    {
        _rig.Windows.Alive.Clear();
        Assert.Equal(InsertOutcome.TargetGone, await Insert(enter: true));
        Assert.Equal(["set"], _rig.Steps);
        Assert.Equal("Hallo Welt", _rig.Clipboard.Text);
        Assert.False(_rig.Clipboard.Private);
        Assert.Equal("", _rig.Keys.Target);
        Assert.True(InsertOutcome.TargetGone.IsFallback());
    }

    [Fact]
    public async Task Missing_target_handle_counts_as_gone()
    {
        Assert.Equal(InsertOutcome.TargetGone, await Insert(target: IntPtr.Zero));
    }

    [Fact]
    public async Task Admin_target_is_protected_by_uipi_and_gets_the_clipboard()
    {
        _rig.Windows.Elevated.Add(42);
        Assert.Equal(InsertOutcome.TargetProtected, await Insert());
        Assert.Equal(["set"], _rig.Steps);
        Assert.Equal("Hallo Welt", _rig.Clipboard.Text);
    }

    [Fact]
    public async Task Target_that_cannot_be_activated_gets_the_clipboard()
    {
        _rig.Windows.Current = 7;
        _rig.Windows.RefuseActivate = true;
        Assert.Equal(InsertOutcome.NotForeground, await Insert());
        Assert.DoesNotContain("ctrl-v", _rig.Steps);
        Assert.Equal("Hallo Welt", _rig.Clipboard.Text);
    }

    [Fact]
    public async Task Rejected_input_leaves_the_text_on_the_clipboard()
    {
        _rig.Keys.Reject = true;
        Assert.Equal(InsertOutcome.InputRejected, await Insert());
        Assert.Equal("Hallo Welt", _rig.Clipboard.Text);
        Assert.False(_rig.Clipboard.Private);
    }

    [Fact]
    public async Task Type_mode_types_unicode_and_leaves_the_clipboard_alone()
    {
        Assert.Equal(InsertOutcome.Typed, await Insert("Grüße aus Köln ß", InsertMode.Type, enter: true));
        Assert.Equal(["wait-modifiers", "activate", "type", "enter"], _rig.Steps);
        Assert.Equal("Grüße aus Köln ß", _rig.Keys.Target);
        Assert.Equal("alt", _rig.Clipboard.Text);
    }

    [Fact]
    public async Task Clipboard_only_mode_sets_a_normal_clipboard_entry()
    {
        Assert.Equal(InsertOutcome.ClipboardOnly, await Insert(mode: InsertMode.ClipboardOnly));
        Assert.Equal(["set"], _rig.Steps);
        Assert.False(_rig.Clipboard.Private);
    }

    [Fact]
    public async Task Empty_text_does_nothing()
    {
        Assert.Equal(InsertOutcome.Empty, await Insert(""));
        Assert.Empty(_rig.Steps);
    }

    [Theory]
    [InlineData("paste", InsertMode.Paste)]
    [InlineData("type", InsertMode.Type)]
    [InlineData("clipboard", InsertMode.ClipboardOnly)]
    [InlineData("unbekannt", InsertMode.Paste)]
    public void Insert_mode_keys(string key, InsertMode mode)
    {
        Assert.Equal(mode, InsertModes.FromKey(key));
        if (key != "unbekannt")
        {
            Assert.Equal(key, mode.Key());
        }
    }
}

public class InsertOutputSinkTest
{
    [Fact]
    public async Task Unarmed_writes_go_to_the_inner_sink_armed_ones_are_inserted_once()
    {
        var rig = new InsertRig();
        var inner = new CaptureSink();
        var sink = new InsertOutputSink(inner, rig.Inserter, () => InsertMode.Paste);
        var outcomes = new List<InsertOutcome>();
        sink.Inserted += outcomes.Add;

        await sink.WriteAsync("cli", CancellationToken.None);
        Assert.Equal(["cli"], inner.Written);

        sink.Arm(42);
        sink.SetPressEnter(true);
        await sink.WriteAsync("hotkey", CancellationToken.None);
        Assert.Equal("hotkey", rig.Keys.Target);
        Assert.Equal(1, rig.Keys.Enters);
        Assert.Equal([InsertOutcome.Pasted], outcomes);

        await sink.WriteAsync("wieder cli", CancellationToken.None);
        Assert.Equal(["cli", "wieder cli"], inner.Written);
    }

    [Fact]
    public async Task Disarm_sends_the_next_write_to_the_inner_sink()
    {
        var rig = new InsertRig();
        var inner = new CaptureSink();
        var sink = new InsertOutputSink(inner, rig.Inserter, () => InsertMode.Paste);
        sink.Arm(42);
        sink.Disarm();
        await sink.WriteAsync("x", CancellationToken.None);
        Assert.Equal(["x"], inner.Written);
        Assert.Empty(rig.Steps);
    }
}
