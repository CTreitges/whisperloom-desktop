using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace WhisperLoom.App.Updates;

/// <summary>
/// <see cref="IUpdateClient"/> über Velopack. Standard-Feed sind die GitHub-Releases des Repos (alle Kanäle,
/// <see cref="GithubReleasesSource"/>); <c>--feed</c> ersetzt ihn durch einen Ordner oder eine URL (Tests, eigener
/// Server der IT). Velopack prüft die SHA-Prüfsumme jedes geladenen Pakets.
/// </summary>
// Doku: docs.velopack.io/getting-started/csharp, docs.velopack.io/reference/cs/Velopack/UpdateManager,
// docs.velopack.io/distributing/github-actions (abgerufen 2026-11-01); Context7 /velopack/velopack.docs, Kanäle und
// UpdateOptions (abgerufen 2026-11-02).
internal sealed class VelopackUpdateClient : IUpdateClient
{
    public const string RepoUrl = "https://github.com/CTreitges/whisperloom-desktop";

    private readonly IUpdateSource _source;
    private readonly string[] _restartArgs;
    private readonly string? _builtChannel;

    /// <param name="feed">Ordner oder URL eines Velopack-Feeds; null = GitHub-Releases.</param>
    /// <param name="restartArgs">Argumente für den Neustart nach dem Update (die des ersten Starts).</param>
    public VelopackUpdateClient(string? feed, string[] restartArgs)
    {
        _source = SourceFor(feed);
        _restartArgs = restartArgs;
        // Kanal, mit dem die laufende Version gepackt wurde (null = nicht installiert).
        _builtChannel = VelopackLocator.IsCurrentSet ? VelopackLocator.Current.Channel : null;
    }

    public bool IsInstalled => Manager(UpdateChannels.CurrentOs, false).IsInstalled;

    public string? CurrentVersion => Manager(UpdateChannels.CurrentOs, false).CurrentVersion?.ToString();

    internal static IUpdateSource SourceFor(string? feed) => feed switch
    {
        null => new GithubReleasesSource(RepoUrl),
        _ when Uri.TryCreate(feed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) => new SimpleWebSource(uri),
        _ => new SimpleFileSource(new DirectoryInfo(feed)),
    };

    /// <summary>
    /// <c>ExplicitChannel</c> nur beim Kanalwechsel (Ziel ≠ Kanal der laufenden Version, ohne Paket = OS-Name),
    /// <c>AllowVersionDowngrade</c> nur für einen bestätigten Downgrade aus der Versionsliste.
    /// </summary>
    internal static UpdateOptions OptionsFor(string channel, string? builtChannel, bool downgrade, string os) => new()
    {
        ExplicitChannel = string.Equals(channel, builtChannel ?? os, StringComparison.OrdinalIgnoreCase) ? null : channel,
        AllowVersionDowngrade = downgrade,
    };

    private UpdateManager Manager(string channel, bool downgrade) =>
        new(_source, OptionsFor(channel, _builtChannel, downgrade, UpdateChannels.CurrentOs));

    public async Task<UpdateRelease?> CheckAsync(string channel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = await Manager(channel, false).CheckForUpdatesAsync().ConfigureAwait(false);
        return info is null ? null : Release(info.TargetFullRelease, channel);
    }

    public async Task<IReadOnlyList<UpdateRelease>> ListAsync(string channel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var feed = await _source.GetReleaseFeed(NullVelopackLogger.Instance, null, channel).ConfigureAwait(false);
        return [.. feed.Assets.Where(a => a.Type == VelopackAssetType.Full).Select(a => Release(a, channel))];
    }

    public Task DownloadAsync(UpdateRelease release, bool downgrade, CancellationToken ct) =>
        Manager(release.Channel, downgrade).DownloadUpdatesAsync(new UpdateInfo(Asset(release), downgrade), cancelToken: ct);

    public void ApplyAndRestart(UpdateRelease release) =>
        Manager(release.Channel, true).ApplyUpdatesAndRestart(Asset(release), _restartArgs);

    public void ApplyAndExit(UpdateRelease release) => Manager(release.Channel, true).ApplyUpdatesAndExit(Asset(release));

    internal static UpdateRelease Release(VelopackAsset asset, string channel) =>
        new(asset.Version.ToString(), channel, GithubReleasesSource.PublishedOf(asset), asset);

    private static VelopackAsset Asset(UpdateRelease release) =>
        release.Asset as VelopackAsset ?? throw new InvalidOperationException("Kein Velopack-Paket.");
}
