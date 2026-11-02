using WhisperLoom.App.Cli;

namespace WhisperLoom.App.Tests.Features;

/// <summary>W3-PERF N8: <c>--help</c>/<c>-h</c> geben die Aufrufübersicht aus (ohne Instanz); das Verb <c>help</c> öffnet das Hilfefenster.</summary>
public sealed class HelpSwitchTest
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_switch_prints_usage_without_an_instance(string flag)
    {
        var r = CommandLine.Parse([flag]);
        Assert.Equal("usage", r.Verb);
        Assert.True(r.IsLocal); // Program erledigt es selbst: kein Lock, kein Avalonia-Start
        Assert.False(r.NeedsRunningInstance);
        // Mit weiteren Schaltern bzw. einem Verb gilt weiter die Übersicht.
        Assert.Equal("usage", CommandLine.Parse(["--data-dir", Path.GetTempPath(), flag]).Verb);
        Assert.Equal("usage", CommandLine.Parse(["status", flag]).Verb);
    }

    [Fact]
    public void Run_usage_writes_the_overview_and_exits_0()
    {
        using var output = new StringWriter();
        Assert.Equal(0, Program.RunUsage(output));
        Assert.Equal(CommandLine.Usage + Environment.NewLine, output.ToString());
        Assert.Contains("--help", output.ToString());
    }

    [Fact]
    public void Help_verb_still_opens_the_help_window()
    {
        var r = CommandLine.Parse(["help"]);
        Assert.Equal("help", r.Verb);
        Assert.False(r.IsLocal);
        Assert.False(r.NeedsRunningInstance);
    }
}
