using System.Text;
using System.Text.Json;
using Velopack.Logging;
using Velopack.Sources;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.Updates;
using WhisperLoom.App.ViewModels.Updates;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>
/// W3-CHAN gegen echtes Velopack: Ordner-Feed (wie <c>pack.py --channel</c> ihn schreibt) und eine gefälschte GitHub-API.
/// <c>WHISPERLOOM_TEST_FEED</c> zeigt auf einen echt gepackten Feed (1.0.1 stabil, 1.1.0-beta.1 beta); ohne die Variable
/// erzeugt der Test dieselben Index-Dateien selbst.
/// </summary>
public sealed class VelopackFeedTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-feed-" + Guid.NewGuid().ToString("N"));

    public VelopackFeedTest() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Index(string version, string file) =>
        $$"""{"Assets":[{"PackageId":"WhisperLoom","Version":"{{version}}","Type":"Full","FileName":"{{file}}","SHA1":"00","SHA256":"00","Size":1}]}""";

    /// <summary>Feed mit 1.0.1 (stabil) und 1.1.0-beta.1 (beta) für das laufende OS.</summary>
    private string Feed()
    {
        if (Environment.GetEnvironmentVariable("WHISPERLOOM_TEST_FEED") is { Length: > 0 } real)
        {
            return real;
        }
        var os = UpdateChannels.CurrentOs;
        var feed = Path.Combine(_dir, "feed");
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(feed, $"releases.{os}.json"), Index("1.0.1", "WhisperLoom-1.0.1-full.nupkg"));
        File.WriteAllText(Path.Combine(feed, $"releases.{os}-beta.json"), Index("1.1.0-beta.1", $"WhisperLoom-1.1.0-beta.1-{os}-beta-full.nupkg"));
        return feed;
    }

    /// <summary>Echter Velopack-Client für den Feed, mit vorgegebener installierter Version (Tests laufen nicht installiert).</summary>
    private sealed class InstalledAs(IUpdateClient inner, string version) : IUpdateClient
    {
        public bool IsInstalled => true;
        public string? CurrentVersion => version;
        public Task<UpdateRelease?> CheckAsync(string channel, CancellationToken ct) => inner.CheckAsync(channel, ct);
        public Task<IReadOnlyList<UpdateRelease>> ListAsync(string channel, CancellationToken ct) => inner.ListAsync(channel, ct);
        public Task DownloadAsync(UpdateRelease release, bool downgrade, CancellationToken ct) => Task.CompletedTask;
        public void ApplyAndRestart(UpdateRelease release) { }
        public void ApplyAndExit(UpdateRelease release) { }
    }

    [Fact]
    public async Task Ordner_Feed_Stabil_findet_101_Beta_findet_beta1()
    {
        var feedDir = Feed();
        var client = new VelopackUpdateClient(feedDir, []);
        var stable = await client.ListAsync(UpdateChannel.Stable.FeedName(UpdateChannels.CurrentOs), CancellationToken.None);
        var beta = await client.ListAsync(UpdateChannel.Beta.FeedName(UpdateChannels.CurrentOs), CancellationToken.None);
        var test = await client.ListAsync(UpdateChannel.Test.FeedName(UpdateChannels.CurrentOs), CancellationToken.None);
        TestContext.Current.TestOutputHelper?.WriteLine($"FEED {feedDir} stable={string.Join(",", stable.Select(r => r.Version))} beta={string.Join(",", beta.Select(r => r.Version))} test={test.Count}");
        Assert.Equal(["1.0.1"], stable.Select(r => r.Version));
        Assert.Equal(["1.1.0-beta.1"], beta.Select(r => r.Version));
        Assert.Empty(test);
        Assert.All(stable, r => Assert.IsType<Velopack.VelopackAsset>(r.Asset, exactMatch: false));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Ordner_Feed_Beta_nach_Stabil_ist_Downgrade()
    {
        Loc.Instance.SetLanguage("de");
        var asked = new List<(string, bool)>();
        var vm = new VersionListViewModel(new InstalledAs(new VelopackUpdateClient(Feed(), []), "1.1.0-beta.1"), new AppLog(_dir),
            () => PolicySettings.None, (v, down) =>
            {
                asked.Add((v, down));
                return Task.FromResult(false);
            });
        await vm.LoadAsync();
        TestContext.Current.TestOutputHelper?.WriteLine("LIST " + string.Join(" | ", vm.Rows.Select(r => $"{r.ChannelLabel} {r.Version} installed={r.IsInstalled} downgrade={r.IsDowngrade}")));
        Assert.Equal(["1.1.0-beta.1", "1.0.1"], vm.Rows.Select(r => r.Version));
        Assert.True(vm.Rows[0].IsInstalled);
        Assert.True(vm.Rows[1].IsDowngrade);
        Assert.Equal(UpdateOutcome.Declined, await vm.BackToStableAsync());
        Assert.Equal([("1.0.1", true)], asked);
    }

    /// <summary>GitHub-API und Release-Dateien aus dem Speicher; merkt sich die abgefragten URLs.</summary>
    private sealed class FakeGithub(int releasesPerPage) : IFileDownloader
    {
        private const string Base = "https://github.invalid/dl/";
        public List<string> Urls { get; } = [];

        /// <summary>11 Test-Releases (neuer) und darunter der stabile 1.0.1.</summary>
        private static List<object> All()
        {
            var os = UpdateChannels.CurrentOs;
            var list = new List<object>();
            for (var n = 11; n >= 1; n--)
            {
                list.Add(Release($"1.1.0-test.main.{n}", prerelease: true, new DateTime(2026, 11, 2, 0, n, 0, DateTimeKind.Utc), $"releases.{os}-test.json"));
            }
            list.Add(Release("1.0.1", prerelease: false, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), $"releases.{os}.json"));
            return list;
        }

        private static object Release(string version, bool prerelease, DateTime published, string index) => new Dictionary<string, object>
        {
            ["name"] = "WhisperLoom " + version,
            ["prerelease"] = prerelease,
            ["published_at"] = published,
            ["assets"] = new[] { new Dictionary<string, string> { ["name"] = index, ["browser_download_url"] = Base + version + "/" + index, ["url"] = "api" } },
        };

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Urls.Add(url);
            var perPage = int.Parse(url[(url.IndexOf("per_page=", StringComparison.Ordinal) + 9)..].Split('&')[0], System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(JsonSerializer.Serialize(All().Take(Math.Min(perPage, releasesPerPage))));
        }

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Urls.Add(url);
            var version = url[Base.Length..].Split('/')[0];
            return Task.FromResult(Encoding.UTF8.GetBytes(Index(version, $"WhisperLoom-{version}-full.nupkg")));
        }

        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task GitHub_stabiler_Release_hinter_elf_Test_Releases_bleibt_sichtbar()
    {
        var github = new FakeGithub(releasesPerPage: 100);
        var source = new GithubReleasesSource(VelopackUpdateClient.RepoUrl, github);
        var feed = await source.GetReleaseFeed(NullVelopackLogger.Instance, null, UpdateChannels.CurrentOs);
        Assert.Equal(["1.0.1"], feed.Assets.Select(a => a.Version.ToString()));
        Assert.Contains(github.Urls, u => u.Contains("/repos/CTreitges/whisperloom-desktop/releases?per_page=100", StringComparison.Ordinal));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), GithubReleasesSource.PublishedOf(feed.Assets[0]));

        var tests = await source.GetReleaseFeed(NullVelopackLogger.Instance, null, UpdateChannel.Test.FeedName(UpdateChannels.CurrentOs));
        Assert.Equal(11, tests.Assets.Length);
    }

    [Fact]
    public async Task Velopack_GithubSource_sieht_den_stabilen_Release_dahinter_nicht()
    {
        // Dokumentiert den Grund für GithubReleasesSource: Velopack fragt nur 10 Releases ab und filtert danach.
        var source = new GithubSource(VelopackUpdateClient.RepoUrl, accessToken: null, prerelease: false, new FakeGithub(releasesPerPage: 100));
        var feed = await source.GetReleaseFeed(NullVelopackLogger.Instance, null, UpdateChannels.CurrentOs);
        Assert.Empty(feed.Assets);
    }

    [Fact]
    public void Release_Liste_neueste_zuerst_ohne_Vorab_nur_stabile()
    {
        var json = """
            [{"name":"a","prerelease":true,"published_at":"2026-11-02T10:00:00Z","assets":[]},
             {"name":"b","prerelease":false,"published_at":"2026-11-01T10:00:00Z","assets":[]},
             {"name":"c","prerelease":false,"published_at":"2026-11-03T10:00:00Z","assets":[]}]
            """;
        Assert.Equal(["c", "a", "b"], GithubReleasesSource.Parse(json, includePrereleases: true).Select(r => r.Name));
        Assert.Equal(["c", "b"], GithubReleasesSource.Parse(json, includePrereleases: false).Select(r => r.Name));
        Assert.Empty(GithubReleasesSource.Parse("[]", includePrereleases: true));
    }
}
