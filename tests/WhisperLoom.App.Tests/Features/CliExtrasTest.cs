using Avalonia.Headless.XUnit;
using WhisperLoom.App.Cli;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Features;

/// <summary>CLI/IPC der Extras (WLD-P5b): reprocess, reinsert, recent-insert.</summary>
public sealed class CliExtrasTest
{
    [Fact]
    public void New_verbs_parse_and_need_a_running_instance()
    {
        var r = CommandLine.Parse(["reprocess", "beautify"]);
        Assert.Equal("reprocess", r.Verb);
        Assert.Equal(["beautify"], r.Args);
        Assert.True(r.NeedsRunningInstance);
        Assert.Equal(["polish", "1761825600000-00000001"], CommandLine.Parse(["reprocess", "polish", "1761825600000-00000001"]).Args);
        Assert.True(CommandLine.Parse(["reinsert"]).NeedsRunningInstance);
        Assert.Equal(["3"], CommandLine.Parse(["recent-insert", "3"]).Args);

        Assert.Throws<UsageException>(() => CommandLine.Parse(["reprocess"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["reprocess", "a", "b", "c"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["reinsert", "x"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["recent-insert"]));
    }

    [AvaloniaFact]
    public async Task Host_runs_the_verbs_and_shows_the_recent_list()
    {
        var data = Path.Combine(Path.GetTempPath(), "wl-cli-" + Guid.NewGuid().ToString("N"));
        var output = new CaptureSink();
        var pipeline = new FakeDictationPipeline { RawText = "Hallo Welt" };
        try
        {
            var host = new AppHost(new AppOptions(data), new MemorySecretStore(), _ => pipeline,
                new FakeRecorder(), output);
            Assert.Equal("no-entry", (await host.ExecuteAsync("reinsert", [])).Message);
            await host.ExecuteAsync("toggle", []);
            await host.ExecuteAsync("toggle", []);

            var reprocess = await host.ExecuteAsync("reprocess", ["summarize"]);
            Assert.Equal(("done", true), (reprocess.Message, reprocess.Ok));
            Assert.Equal("[summarize] Hallo Welt", output.Written[^1]);
            var recent = reprocess.Status!["recent"]!.AsArray();
            Assert.Equal(2, recent.Count);
            Assert.Equal(("summarize", "[summarize] Hallo Welt"), (recent[0]!["stage"]!.GetValue<string>(), recent[0]!["label"]!.GetValue<string>()));

            var insert = await host.ExecuteAsync("recent-insert", ["1"]);
            Assert.Equal(("copied", "Hallo Welt"), (insert.Message, output.Written[^1]));
            Assert.False((await host.ExecuteAsync("recent-insert", ["9"])).Ok);
            Assert.Equal("copied", (await host.ExecuteAsync("reinsert", [])).Message);
            Assert.Equal("[summarize] Hallo Welt", output.Written[^1]);
            var bad = await host.ExecuteAsync("reprocess", ["quatsch"]);
            Assert.Equal(("stage-unavailable", false), (bad.Message, bad.Ok));
            Assert.Null(host.Controller.Rewriter);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }
}
