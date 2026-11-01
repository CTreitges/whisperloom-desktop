using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Profil-Auflösung (WLD-P5b): Prozess, Titel-Regex, Vorrang.</summary>
public sealed class AppProfilesTest
{
    private static readonly WindowInfo Terminal = new("WindowsTerminal", "Claude Code — ~/projekt");
    private static readonly WindowInfo Outlook = new("OUTLOOK", "Posteingang - Outlook");

    [Theory]
    [InlineData("WindowsTerminal.exe")]
    [InlineData("windowsterminal")]
    [InlineData(" WINDOWSTERMINAL.EXE ")]
    public void Process_matches_with_or_without_exe_and_any_case(string process) =>
        Assert.True(AppProfiles.Matches(new AppProfile { Process = process }, Terminal));

    [Fact]
    public void Other_process_does_not_match()
    {
        Assert.False(AppProfiles.Matches(new AppProfile { Process = "OUTLOOK.EXE" }, Terminal));
        Assert.False(AppProfiles.Matches(new AppProfile { Process = "Windows" }, Terminal));
    }

    [Fact]
    public void Title_regex_must_match_case_insensitive()
    {
        Assert.True(AppProfiles.Matches(new AppProfile { Process = "WindowsTerminal.exe", TitlePattern = "claude code" }, Terminal));
        Assert.False(AppProfiles.Matches(new AppProfile { Process = "WindowsTerminal.exe", TitlePattern = "^PowerShell" }, Terminal));
    }

    [Fact]
    public void Disabled_empty_or_invalid_profiles_never_match()
    {
        Assert.False(AppProfiles.Matches(new AppProfile { Process = "WindowsTerminal.exe", Enabled = false }, Terminal));
        Assert.False(AppProfiles.Matches(new AppProfile { Process = "" }, Terminal));
        Assert.False(AppProfiles.Matches(new AppProfile { Process = "WindowsTerminal.exe", TitlePattern = "([" }, Terminal));
    }

    [Fact]
    public void Title_profile_wins_over_process_only_profile_regardless_of_order()
    {
        List<AppProfile> profiles =
        [
            new() { Name = "Terminal", Process = "WindowsTerminal.exe", Stage = "polish" },
            new() { Name = "Claude", Process = "WindowsTerminal.exe", TitlePattern = "Claude", Stage = "prompt" },
        ];
        var match = AppProfiles.Resolve(profiles, Terminal);
        Assert.NotNull(match);
        Assert.Equal(1, match.Index);
        Assert.Equal("prompt", match.Profile.Stage);

        // Titel passt nicht: das Prozess-Profil greift.
        var other = AppProfiles.Resolve(profiles, Terminal with { Title = "PowerShell" });
        Assert.Equal(0, other!.Index);
    }

    [Fact]
    public void First_of_equally_specific_profiles_wins_and_no_match_is_null()
    {
        List<AppProfile> profiles =
        [
            new() { Process = "OUTLOOK.EXE", Stage = "beautify" },
            new() { Process = "outlook", Stage = "summarize" },
            new() { Process = "OUTLOOK.EXE", TitlePattern = "([", Stage = "prompt" },
        ];
        Assert.Equal(0, AppProfiles.Resolve(profiles, Outlook)!.Index);
        Assert.Null(AppProfiles.Resolve(profiles, Terminal));
        Assert.Null(AppProfiles.Resolve([], Terminal));
    }

    [Fact]
    public void Suggestions_are_terminal_prompt_and_outlook_beautify()
    {
        var s = AppProfiles.Suggestions();
        Assert.Equal(("WindowsTerminal.exe", "prompt"), (s[0].Process, s[0].Stage));
        Assert.Equal(("OUTLOOK.EXE", "beautify"), (s[1].Process, s[1].Stage));
        Assert.Equal("Outlook", AppProfiles.DisplayName(s[1]));
        Assert.Equal("windowsterminal", AppProfiles.DisplayName(new AppProfile { Process = "WindowsTerminal.exe" }));
    }

    [Fact]
    public void Profiles_round_trip_in_the_desktop_section()
    {
        var settings = new WhisperLoomSettings();
        var desktop = SettingsStore.Desktop(settings);
        desktop.Profiles.Add(new AppProfile { Name = "Claude", Process = "WindowsTerminal.exe", TitlePattern = "Claude", Stage = "prompt", InsertMode = "type", PressEnter = true });
        SettingsStore.SetDesktop(settings, desktop);

        var json = SettingsJson.Serialize(settings);
        Assert.Contains("\"profiles\"", json);
        Assert.Contains("\"titlePattern\"", json);
        var back = SettingsStore.Desktop(SettingsJson.Deserialize(json)).Profiles.Single();
        Assert.Equal(("Claude", "WindowsTerminal.exe", "Claude", "prompt", "type", true, true),
            (back.Name, back.Process, back.TitlePattern, back.Stage, back.InsertMode, back.PressEnter, back.Enabled));
        Assert.Equal("Ctrl+Alt+Shift+V", SettingsStore.Desktop(settings).HotkeyReinsert);
        Assert.Equal("Ctrl+Alt+Shift+S", settings.Selection.HotkeyCombo); // W2-SEL: Umschreiben ging in „Markierung verarbeiten“ auf
    }
}
