using System.Text.Json;
using Velopack.Sources;

namespace WhisperLoom.App.Updates;

/// <summary>
/// GitHub-Releases als Velopack-Quelle mit Vorabversionen und bis zu <see cref="PerPage"/> Releases. Velopacks
/// <see cref="GithubSource"/> fragt nur die letzten 10 Releases ab und filtert erst danach: Nach 10 Beta-/Test-Releases
/// sähe eine Prüfung im Kanal Stabil den letzten stabilen Release nicht mehr. Welche Releases zu welchem Kanal gehören,
/// entscheidet die Datei <c>releases.&lt;kanal&gt;.json</c> im Release, nicht das Pre-release-Flag.
/// </summary>
// Doku: github.com/velopack/velopack, src/lib-csharp/Sources/GithubSource.cs und GitBase.cs, Tag 1.2.161;
// docs.github.com/rest/releases/releases#list-releases (per_page höchstens 100) (abgerufen 2026-11-02).
internal sealed class GithubReleasesSource(string repoUrl, IFileDownloader? downloader = null)
    : GithubSource(repoUrl, accessToken: null, prerelease: true, downloader)
{
    public const int PerPage = 100;

    protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
    {
        var uri = new Uri(GetApiBaseUrl(RepoUri), $"repos{RepoUri.AbsolutePath}/releases?per_page={PerPage}&page=1");
        var json = await Downloader.DownloadString(uri.ToString(), GetRequestHeaders("application/vnd.github.v3+json")).ConfigureAwait(false);
        return Parse(json, includePrereleases);
    }

    /// <summary>Veröffentlichungsdatum des GitHub-Releases, aus dem das Paket stammt; null bei anderen Quellen.</summary>
    internal static DateTimeOffset? PublishedOf(Velopack.VelopackAsset asset) =>
        (asset as GitBaseAsset)?.Release.PublishedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;

    /// <summary>Neueste zuerst; ohne <paramref name="includePrereleases"/> nur stabile.</summary>
    internal static GithubRelease[] Parse(string json, bool includePrereleases) =>
        [.. (JsonSerializer.Deserialize<GithubRelease[]>(json) ?? [])
            .Where(r => includePrereleases || !r.Prerelease)
            .OrderByDescending(r => r.PublishedAt)];
}
