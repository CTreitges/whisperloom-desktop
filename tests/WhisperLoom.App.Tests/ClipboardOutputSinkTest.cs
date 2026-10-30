using System.Diagnostics;
using WhisperLoom.App.Output;
using WhisperLoom.App.Secrets;

namespace WhisperLoom.App.Tests;

/// <summary>
/// Regression Prüferbefund 1 (WLD-P2): xclip und wl-copy forken ein Kind, das stdout/stderr offen hält, bis eine
/// andere App die Zwischenablage übernimmt. Die Ausgabe darf darauf nicht warten, sonst bleibt das Diktat in
/// „processing“ hängen. Die Fake-Werkzeuge verhalten sich genauso: Elternprozess endet sofort mit 0, ein Kind
/// mit geerbten Handles lebt weiter (es endet von selbst nach 20 s).
/// </summary>
public class ClipboardOutputSinkTest
{
    private static (string File, string[] Args) ForkingTool() => OperatingSystem.IsWindows()
        // start /b: ping erbt den stderr-Handle von cmd und lebt nach dem Exit von cmd weiter.
        ? ("cmd.exe", ["/c", "more >nul & start /b ping -n 20 127.0.0.1 >nul & exit 0"])
        : ("/bin/sh", ["-c", "cat >/dev/null; sleep 20 & exit 0"]);

    private static (string File, string[] Args) HangingTool() => OperatingSystem.IsWindows()
        ? ("cmd.exe", ["/c", "more >nul & ping -n 20 127.0.0.1 >nul"])
        : ("/bin/sh", ["-c", "cat >/dev/null; sleep 20"]);

    [Fact]
    public async Task Returns_when_the_tool_exits_even_if_its_child_keeps_the_handles_open()
    {
        var sink = new ClipboardOutputSink(ProcessRunner.Instance, [ForkingTool()], TimeSpan.FromSeconds(10));
        var watch = Stopwatch.StartNew();

        var write = sink.WriteAsync("Diktat", CancellationToken.None);
        var finished = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(8)));

        Assert.Same(write, finished);
        await write;
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"dauerte {watch.Elapsed}");
    }

    [Fact]
    public async Task Gives_up_after_the_time_limit_when_the_tool_hangs()
    {
        var sink = new ClipboardOutputSink(ProcessRunner.Instance, [HangingTool()], TimeSpan.FromSeconds(1));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<IOException>(() => sink.WriteAsync("Diktat", CancellationToken.None));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"dauerte {watch.Elapsed}");
    }

    [Fact]
    public async Task Falls_back_to_the_next_tool_when_one_is_missing()
    {
        var sink = new ClipboardOutputSink(
            ProcessRunner.Instance,
            [("whisperloom-no-such-tool", []), ForkingTool()],
            TimeSpan.FromSeconds(10));
        await sink.WriteAsync("Diktat", CancellationToken.None);
    }
}
