using Avalonia.Headless.XUnit;
using WhisperLoom.App.Cli;

namespace WhisperLoom.App.Tests;

/// <summary>Verben <c>settings</c> und <c>help</c>: öffnen das Fenster wie das Tray (auch Messzugang für Performance-Messungen).</summary>
public sealed class WindowVerbsTest
{
    [Theory]
    [InlineData("settings")]
    [InlineData("help")]
    public void Verb_wird_erkannt_und_startet_bei_Bedarf_eine_Instanz(string verb)
    {
        var request = CommandLine.Parse([verb, "--data-dir", "x"]);
        Assert.Equal(verb, request.Verb);
        Assert.Empty(request.Args);
        // Wie show/setup: startet die App, wenn keine läuft, und erledigt nichts lokal.
        Assert.False(request.NeedsRunningInstance);
        Assert.False(request.IsLocal);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("help")]
    public void Verb_ohne_Argumente(string verb)
    {
        Assert.Throws<UsageException>(() => CommandLine.Parse([verb, "extra"]));
    }

    [AvaloniaTheory]
    [InlineData("settings")]
    [InlineData("help")]
    public async Task AppHost_oeffnet_das_Fenster(string verb)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-verb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var host = new AppHost(new AppOptions(dir), new MemorySecretStore(), _ => new Core.Testing.FakeDictationPipeline(), new FakeRecorder(), new CaptureSink());
            var opened = new List<string>();
            host.ShowWindow = opened.Add;
            var response = await host.ExecuteAsync(verb, []);
            Assert.True(response.Ok);
            Assert.Equal("shown", response.Message);
            Assert.Equal([verb], opened);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
