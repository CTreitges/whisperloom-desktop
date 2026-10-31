using WhisperLoom.App.Platform.Windows.Input;

namespace WhisperLoom.App.Tests.Windows;

public class HotkeyTest
{
    [Theory]
    [InlineData("Ctrl+Win")]
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl+Alt+Space")]
    [InlineData("Ctrl+Alt+Shift+V")]
    [InlineData("Win+F12")]
    [InlineData("Alt+Pause")]
    public void Parse_and_format_round_trip(string text) => Assert.Equal(text, Hotkey.Parse(text)!.ToString());

    [Fact]
    public void Defaults_match_the_goal()
    {
        Assert.Equal("Ctrl+Win", Hotkey.DefaultHold.ToString());
        Assert.True(Hotkey.DefaultHold.IsModifierOnly);
        Assert.Equal("Ctrl+Space", Hotkey.DefaultToggle.ToString());
        Assert.Equal("Ctrl+Alt+Space", Hotkey.DefaultStage.ToString());
    }

    [Theory]
    [InlineData("strg + leertaste")]
    [InlineData("")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Nope")]
    public void Invalid_text_gives_null_or_default(string text)
    {
        Assert.Null(Hotkey.Parse(text));
        Assert.Equal(Hotkey.DefaultToggle, Hotkey.ParseOr(text, Hotkey.DefaultToggle));
    }

    [Fact]
    public void Parse_accepts_strg_and_any_case()
    {
        Assert.Equal(Hotkey.DefaultToggle, Hotkey.Parse("strg+space"));
        Assert.Equal(Hotkey.DefaultStage, Hotkey.Parse(" ctrl + ALT + space "));
    }

    [Fact]
    public void Left_and_right_modifiers_are_the_same()
    {
        Assert.Equal(HotkeyModifiers.Ctrl, Hotkey.ModifierOf(Hotkey.VkRControl));
        Assert.Equal(HotkeyModifiers.Win, Hotkey.ModifierOf(Hotkey.VkRWin));
        Assert.Equal(HotkeyModifiers.None, Hotkey.ModifierOf(Hotkey.VkSpace));
    }
}

public class HotkeyConflictsTest
{
    private static readonly Hotkey?[] None = [];

    [Fact]
    public void Ctrl_space_warns_about_autocomplete()
    {
        Assert.Contains("Hotkey_Conflict_Autocomplete", HotkeyConflicts.For(Hotkey.DefaultToggle, None));
    }

    [Fact]
    public void Win_space_warns_about_input_language()
    {
        Assert.Contains("Hotkey_Conflict_InputLanguage", HotkeyConflicts.For(Hotkey.Parse("Win+Space")!, None));
    }

    [Fact]
    public void Duplicate_and_overlay_keys_warn()
    {
        Assert.Contains("Hotkey_Conflict_Duplicate", HotkeyConflicts.For(Hotkey.DefaultStage, [Hotkey.DefaultToggle, Hotkey.DefaultStage]));
        Assert.Contains("Hotkey_Conflict_Overlay", HotkeyConflicts.For(Hotkey.Parse("Ctrl+Enter")!, None));
        Assert.Contains("Hotkey_Conflict_NoModifier", HotkeyConflicts.For(Hotkey.Parse("F9")!, None));
        Assert.Contains("Hotkey_Conflict_SingleModifier", HotkeyConflicts.For(Hotkey.Parse("Ctrl")!, None));
    }

    [Fact]
    public void Default_hold_and_stage_have_no_warning()
    {
        Assert.Empty(HotkeyConflicts.For(Hotkey.DefaultHold, [Hotkey.DefaultToggle, Hotkey.DefaultStage]));
        Assert.Empty(HotkeyConflicts.For(Hotkey.DefaultStage, [Hotkey.DefaultHold, Hotkey.DefaultToggle]));
    }
}
