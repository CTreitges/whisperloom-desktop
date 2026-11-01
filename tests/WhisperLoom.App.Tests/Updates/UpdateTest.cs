using WhisperLoom.App.Cli;
using WhisperLoom.App.Services;
using WhisperLoom.App.Updates;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>Fake statt Velopack: zählt Aufrufe, liefert eine einstellbare neue Version.</summary>
internal sealed class FakeUpdateClient : IUpdateClient
{
    public bool IsInstalled { get; set; } = true;
    public string? CurrentVersion { get; set; } = "0.9.0";
    public string? Available { get; set; }
    public Exception? CheckError { get; set; }
    public int Checks { get; private set; }
    public int Downloads { get; private set; }
    public int Restarts { get; private set; }
    public int Exits { get; private set; }

    public Task<string?> CheckAsync(CancellationToken ct)
    {
        Checks++;
        return CheckError is null ? Task.FromResult(Available) : Task.FromException<string?>(CheckError);
    }

    public Task DownloadAsync(CancellationToken ct)
    {
        Downloads++;
        return Task.CompletedTask;
    }

    public void ApplyAndRestart() => Restarts++;

    public void ApplyAndExit() => Exits++;
}

public sealed class UpdateServiceTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-update-" + Guid.NewGuid().ToString("N"));
    private readonly AppLog _log;

    public UpdateServiceTest() => _log = new AppLog(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string LogText() => File.Exists(_log.FilePath) ? File.ReadAllText(_log.FilePath) : "";

    private UpdateService Service(FakeUpdateClient client, PolicySettings? policy = null, bool answer = false, List<string>? asked = null) =>
        new(client, _log, () => policy ?? PolicySettings.None, v =>
        {
            asked?.Add(v);
            return Task.FromResult(answer);
        });

    [Fact]
    public async Task PolicyAusPrueftNichtUndLoggt()
    {
        var client = new FakeUpdateClient { Available = "0.9.1" };
        var outcome = await Service(client, new PolicySettings { UpdatesEnabled = false }).CheckOnceAsync(CancellationToken.None);
        Assert.Equal(UpdateOutcome.DisabledByPolicy, outcome);
        Assert.Equal(0, client.Checks);
        Assert.Contains("update.check status=disabled-by-policy", LogText());
    }

    [Fact]
    public async Task NichtInstalliertPrueftNicht()
    {
        var client = new FakeUpdateClient { IsInstalled = false, Available = "0.9.1" };
        Assert.Equal(UpdateOutcome.NotInstalled, await Service(client).CheckOnceAsync(CancellationToken.None));
        Assert.Equal(0, client.Checks);
    }

    [Fact]
    public async Task AktuellFragtNicht()
    {
        var asked = new List<string>();
        var client = new FakeUpdateClient();
        Assert.Equal(UpdateOutcome.Current, await Service(client, asked: asked).CheckOnceAsync(CancellationToken.None));
        Assert.Equal(1, client.Checks);
        Assert.Empty(asked);
        Assert.Contains("update.check status=current version=0.9.0", LogText());
    }

    [Fact]
    public async Task AbgelehntLaedtUndInstalliertNichts()
    {
        var asked = new List<string>();
        var client = new FakeUpdateClient { Available = "0.9.1" };
        Assert.Equal(UpdateOutcome.Declined, await Service(client, answer: false, asked: asked).CheckOnceAsync(CancellationToken.None));
        Assert.Equal(["0.9.1"], asked);
        Assert.Equal((0, 0), (client.Downloads, client.Restarts));
    }

    [Fact]
    public async Task BestaetigtLaedtUndStartetNeu()
    {
        var client = new FakeUpdateClient { Available = "0.9.1" };
        Assert.Equal(UpdateOutcome.Applied, await Service(client, answer: true).CheckOnceAsync(CancellationToken.None));
        Assert.Equal((1, 1, 0), (client.Downloads, client.Restarts, client.Exits));
        Assert.Contains("update.apply version=0.9.1", LogText());
    }

    [Fact]
    public async Task FeedFehlerHaeltDieAppNichtAn()
    {
        var client = new FakeUpdateClient { CheckError = new HttpRequestException("offline") };
        Assert.Equal(UpdateOutcome.Failed, await Service(client).CheckOnceAsync(CancellationToken.None));
        Assert.Contains("update.check status=failed errorClass=HttpRequestException", LogText());
        // Keine Fehlermeldung (könnte URLs enthalten) im Log.
        Assert.DoesNotContain("offline", LogText());
    }

    [Fact]
    public async Task PrueftBeimStartUndDannTaeglich()
    {
        var client = new FakeUpdateClient();
        using var cts = new CancellationTokenSource();
        var waits = new List<TimeSpan>();
        var service = new UpdateService(client, _log, () => PolicySettings.None, _ => Task.FromResult(false), (span, ct) =>
        {
            waits.Add(span);
            if (waits.Count == 2)
            {
                cts.Cancel();
            }
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(cts.Token));
        Assert.Equal(2, client.Checks);
        Assert.All(waits, w => Assert.Equal(TimeSpan.FromDays(1), w));
    }

    [Fact]
    public async Task RichtlinieGiltJeRundeNeu()
    {
        var policy = PolicySettings.None;
        var client = new FakeUpdateClient();
        var service = new UpdateService(client, _log, () => policy, _ => Task.FromResult(false));
        await service.CheckOnceAsync(CancellationToken.None);
        policy = new PolicySettings { UpdatesEnabled = false };
        Assert.Equal(UpdateOutcome.DisabledByPolicy, await service.CheckOnceAsync(CancellationToken.None));
        Assert.Equal(1, client.Checks);
    }
}

public sealed class UpdateCommandTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-update-cli-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private Task<int> Run(FakeUpdateClient client, PolicySettings? policy = null) =>
        UpdateCommand.RunAsync(client, policy ?? PolicySettings.None, new AppLog(_dir), _out, _err, CancellationToken.None);

    [Fact]
    public async Task NeueVersionWirdOhneNeustartInstalliert()
    {
        var client = new FakeUpdateClient { Available = "0.9.1" };
        Assert.Equal(0, await Run(client));
        Assert.Equal((1, 1, 0), (client.Downloads, client.Exits, client.Restarts));
        Assert.Contains("0.9.0 -> 0.9.1", _out.ToString());
    }

    [Fact]
    public async Task AktuellIstErfolg()
    {
        var client = new FakeUpdateClient();
        Assert.Equal(0, await Run(client));
        Assert.Equal(0, client.Downloads);
        Assert.Contains("aktuell", _out.ToString());
    }

    [Fact]
    public async Task PolicyAusVerweigert()
    {
        var client = new FakeUpdateClient { Available = "0.9.1" };
        Assert.Equal(UpdateCommand.DisabledExitCode, await Run(client, new PolicySettings { UpdatesEnabled = false }));
        Assert.Equal(0, client.Checks);
        Assert.Contains("updatesEnabled=false", _err.ToString());
    }

    [Fact]
    public async Task NichtInstalliertIstFehler()
    {
        Assert.Equal(1, await Run(new FakeUpdateClient { IsInstalled = false }));
        Assert.Contains("nicht installiert", _err.ToString());
    }

    [Fact]
    public async Task FeedFehlerIstFehler()
    {
        Assert.Equal(1, await Run(new FakeUpdateClient { CheckError = new IOException("feed fehlt") }));
        Assert.Contains("IOException", _err.ToString());
    }
}

public sealed class UpdateCliTest
{
    [Fact]
    public void VersionUndUpdateLaufenLokal()
    {
        var version = CommandLine.Parse(["--version"]);
        Assert.Equal("version", version.Verb);
        Assert.True(version.IsLocal);
        var update = CommandLine.Parse(["update", "--feed", "feed"]);
        Assert.Equal("update", update.Verb);
        Assert.Equal([Path.GetFullPath("feed")], update.Args);
        Assert.True(update.IsLocal);
        Assert.Empty(CommandLine.Parse(["update"]).Args);
        Assert.Equal(["https://example.invalid/feed"], CommandLine.Parse(["update", "--feed", "https://example.invalid/feed"]).Args);
    }

    [Theory]
    [InlineData("status", "--feed", "x")]
    [InlineData("update", "--feed")]
    [InlineData("update", "jetzt")]
    public void FalscheUpdateArgumente(params string[] args) => Assert.Throws<UsageException>(() => CommandLine.Parse(args));

    [Theory]
    [InlineData("http://example.invalid/feed", false)]
    [InlineData("HTTP://10.0.0.5:8080/feed", false)]
    [InlineData("http://localhost:8080/feed", true)]
    [InlineData("http://127.0.0.1:9/feed", true)]
    [InlineData("http://[::1]:9/feed", true)]
    [InlineData("https://example.invalid/feed", true)]
    public void N18_Feed_per_http_nur_zu_localhost(string feed, bool ok)
    {
        if (ok)
        {
            Assert.Equal([feed], CommandLine.Parse(["update", "--feed", feed]).Args);
        }
        else
        {
            Assert.Throws<UsageException>(() => CommandLine.Parse(["update", "--feed", feed]));
        }
    }

    [Theory]
    [InlineData("0.9.1+abc123", "0.9.1")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.2")]
    [InlineData(null, "0.0.0")]
    public void VersionOhneCommitKennung(string? informational, string expected) => Assert.Equal(expected, AppVersion.Format(informational));
}
