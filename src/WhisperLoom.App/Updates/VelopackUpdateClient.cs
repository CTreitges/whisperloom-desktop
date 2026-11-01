using Velopack;
using Velopack.Sources;

namespace WhisperLoom.App.Updates;

/// <summary>
/// <see cref="IUpdateClient"/> über Velopack. Standard-Feed sind die GitHub-Releases des Repos (nur stabile, keine
/// Pre-Releases); <c>--feed</c> ersetzt ihn durch einen Ordner oder eine URL (Tests, eigener Server der IT).
/// </summary>
// Doku: docs.velopack.io/getting-started/csharp, docs.velopack.io/reference/cs/Velopack/UpdateManager,
// docs.velopack.io/distributing/github-actions (abgerufen 2026-11-01).
internal sealed class VelopackUpdateClient : IUpdateClient
{
    public const string RepoUrl = "https://github.com/CTreitges/whisperloom-desktop";

    private readonly UpdateManager _manager;
    private readonly string[] _restartArgs;
    private UpdateInfo? _found;

    /// <param name="feed">Ordner oder URL eines Velopack-Feeds; null = GitHub-Releases.</param>
    /// <param name="restartArgs">Argumente für den Neustart nach dem Update (die des ersten Starts).</param>
    public VelopackUpdateClient(string? feed, string[] restartArgs)
    {
        _manager = feed is null
            ? new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false))
            : new UpdateManager(feed);
        _restartArgs = restartArgs;
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    public async Task<string?> CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(CancellationToken ct) =>
        _manager.DownloadUpdatesAsync(_found ?? throw new InvalidOperationException("Erst CheckAsync."), cancelToken: ct);

    public void ApplyAndRestart() => _manager.ApplyUpdatesAndRestart(_found?.TargetFullRelease, _restartArgs);

    public void ApplyAndExit() => _manager.ApplyUpdatesAndExit(_found?.TargetFullRelease);
}
