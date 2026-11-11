using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Tests.Windows;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Markierung per Strg+C holen (WLD-P5b), Zwischenablage gesichert und wiederhergestellt; neue Kombis im Hook.</summary>
public sealed class SelectionCaptureTest
{
    private readonly InsertRig _rig = new();

    private SelectionCapture Capture() => new(_rig.Clipboard, _rig.Keys, _rig.Windows, TimeSpan.Zero);

    [Fact]
    public async Task Copies_the_selection_and_restores_the_clipboard()
    {
        _rig.Keys.Selection = "Markierter Text";

        Assert.Equal("Markierter Text", (await Capture().CaptureAsync(42, false, CancellationToken.None)).Text);

        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.Equal(["wait-modifiers", "save", "ctrl-c", "get-text", "restore"], _rig.Steps);
    }

    [Fact]
    public async Task Nothing_selected_gives_null_and_leaves_the_clipboard_alone()
    {
        Assert.Null((await Capture().CaptureAsync(42, false, CancellationToken.None)).Text);

        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.DoesNotContain("restore", _rig.Steps);
        Assert.DoesNotContain("get-text", _rig.Steps);
    }

    [Fact]
    public async Task Whitespace_only_selection_counts_as_nothing_but_is_restored()
    {
        _rig.Keys.Selection = " \r\n ";

        Assert.Null((await Capture().CaptureAsync(42, false, CancellationToken.None)).Text);

        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.Contains("restore", _rig.Steps);
    }

    [Fact]
    public async Task Empty_clipboard_before_stays_empty_after()
    {
        _rig.Clipboard.Text = null;
        _rig.Keys.Selection = "x";

        Assert.Equal("x", (await Capture().CaptureAsync(42, false, CancellationToken.None)).Text);

        Assert.Null(_rig.Clipboard.Text);
    }

    [Fact]
    public async Task A_late_copy_after_the_wait_is_undone_and_the_clipboard_restored()
    {
        // Review N-7: Kopiert das Ziel erst nach der Wartezeit, blieb die Markierung in der Zwischenablage, die alte war weg.
        // Nachfrist = SelectionCapture.DefaultLateCopyGrace (500 ms) nach den 50 ms Wartezeit.
        var capture = new SelectionCapture(_rig.Clipboard, _rig.Keys, _rig.Windows, TimeSpan.FromMilliseconds(50));
        var late = Task.Run(async () =>
        {
            await Task.Delay(200);
            _rig.Clipboard.Text = "Markierter Text";
            _rig.Clipboard.SequenceNumber++;
        });

        Assert.Null((await capture.CaptureAsync(42, false, CancellationToken.None)).Text);
        await late;

        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.Contains("restore", _rig.Steps);
    }

    [Fact]
    public async Task Consoles_get_ctrl_insert_instead_of_ctrl_c()
    {
        _rig.Keys.Selection = "ls -la";

        Assert.Equal("ls -la", (await Capture().CaptureAsync(42, true, CancellationToken.None)).Text);

        Assert.Contains("ctrl-insert", _rig.Steps);
        Assert.DoesNotContain("ctrl-c", _rig.Steps);
        Assert.True(SelectionCapture.IsConsole("WindowsTerminal"));
        Assert.True(SelectionCapture.IsConsole("pwsh.exe"));
        Assert.False(SelectionCapture.IsConsole("WINWORD"));
        Assert.False(SelectionCapture.IsConsole("WhisperLoom.TestTarget"));
    }

    [Fact]
    public void Reinsert_and_selection_combos_fire_and_swallow_their_key()
    {
        var m = new HotkeyStateMachine(null, null, null) { Reinsert = Hotkey.DefaultReinsert, Selection = Hotkey.Parse("Ctrl+Alt+Shift+R") };
        Assert.Equal("Ctrl+Alt+Shift+V", Hotkey.DefaultReinsert.ToString());
        Assert.Equal(Hotkey.DefaultSelection, Hotkey.Parse("Ctrl+Alt+Shift+S"));

        foreach (var vk in new[] { Hotkey.VkLControl, Hotkey.VkLMenu, Hotkey.VkLShift })
        {
            Assert.Equal(HookDecision.Pass, m.OnKey(new KeyInput(vk, true)));
        }
        Assert.Equal(new HookDecision(true, HotkeyAction.Reinsert), m.OnKey(new KeyInput('V', true)));
        Assert.True(m.OnKey(new KeyInput('V', false)).Swallow);
        Assert.Equal(new HookDecision(true, HotkeyAction.Selection), m.OnKey(new KeyInput('R', true)));
        // Ohne Umschalt ist es eine andere Kombi.
        m.OnKey(new KeyInput('R', false));
        m.OnKey(new KeyInput(Hotkey.VkLShift, false));
        Assert.Equal(HookDecision.Pass, m.OnKey(new KeyInput('R', true)));
    }
}
