using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>Variante des Begleitprozesses aus dem Release (<c>docs/whisper-server-build.md</c>).</summary>
public enum SidecarVariant
{
    Vulkan,
    Cpu,
}

/// <summary>
/// Installiert <c>whisper-server</c> bei Bedarf aus dem Release <c>whisper-server-&lt;tag&gt;</c> in den Datenordner:
/// <c>sidecar-manifest.json</c> laden (gegen die hier festgeschriebene SHA256 geprüft), daraus URL und SHA256 des Archivs,
/// Download mit Range und Prüfung (<see cref="ModelDownloader"/>), Entpacken nach <c>&lt;ordner&gt;/&lt;tag&gt;-&lt;os&gt;-&lt;variante&gt;/</c>.
/// </summary>
// Doku: https://learn.microsoft.com/dotnet/api/system.formats.tar.tarfile (abgerufen 2026-10-31),
// https://learn.microsoft.com/dotnet/api/system.io.compression.zipfile (abgerufen 2026-10-31)
public sealed class SidecarRelease(HttpClient http, string dir)
{
    public const string Tag = "b5454";

    public const string ReleaseUrl = "https://github.com/CTreitges/whisperloom-desktop/releases/download/whisper-server-" + Tag + "/";

    /// <summary>SHA256 von <c>sidecar-manifest.json</c> des Releases — der Vertrauensanker für die Archiv-Prüfsummen.</summary>
    public const string ManifestSha256 = "cf5c5f6ce6719718f3012ff76654c0e03a88da4f33a641a5ce6eb2fdbb2f4d34";

    public const string MsgManifest = "Begleitprozess: Manifest des Releases passt nicht";

    public const string MsgExtract = "Begleitprozess konnte nicht entpackt werden";

    /// <summary>Eine Installation zur Zeit (Einstellungen und Diktat teilen sich Teildatei und Zielordner).</summary>
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    private static readonly TimeSpan MetaTimeout = TimeSpan.FromSeconds(30);

    public string Dir { get; } = dir;

    /// <summary>Für Tests: andere Release-Adresse (lokaler Fake) und andere Manifest-Prüfsumme.</summary>
    public string BaseUrl { get; init; } = ReleaseUrl;

    public string ExpectedManifestSha256 { get; init; } = ManifestSha256;

    public static string Os => OperatingSystem.IsWindows() ? "win" : "linux";

    public static string ExeName => OperatingSystem.IsWindows() ? "whisper-server.exe" : "whisper-server";

    public static string Key(SidecarVariant variant) => variant.ToString().ToLowerInvariant();

    /// <summary>Pfad der ausführbaren Datei der Variante (ob installiert oder nicht).</summary>
    public string ExePath(SidecarVariant variant) => Path.Combine(Dir, $"{Tag}-{Os}-{Key(variant)}", ExeName);

    public bool IsInstalled(SidecarVariant variant) => File.Exists(ExePath(variant));

    /// <summary>Installiert die Variante, falls nötig, und liefert den Pfad der ausführbaren Datei.</summary>
    /// <exception cref="DownloadException">Download, Prüfsumme oder Platz.</exception>
    public async Task<string> EnsureAsync(SidecarVariant variant, DownloadProgress? progress, CancellationToken ct)
    {
        var exe = ExePath(variant);
        if (File.Exists(exe)) return exe;
        if (RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new DownloadException("Begleitprozess gibt es nur für x64", false);
        }
        await InstallGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(exe)) return exe; // inzwischen von einem anderen Aufrufer installiert
            var archive = await ArchiveAsync(variant, ct).ConfigureAwait(false);
            var store = new ModelStore(Dir);
            if (!await new ModelDownloader(http).DownloadAsync(archive, store, progress, ct).ConfigureAwait(false))
            {
                throw new OperationCanceledException(ct);
            }
            try
            {
                Extract(store.File(archive), Path.GetDirectoryName(exe)!);
                File.Delete(store.File(archive));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new DownloadException(MsgExtract, false, DownloadException.Kinds.Storage, e);
            }
            if (!File.Exists(exe)) throw new DownloadException($"Archiv ohne {ExeName}", false);
            return exe;
        }
        finally
        {
            InstallGate.Release();
        }
    }

    /// <summary>Archiv der Variante aus dem geprüften Manifest; die Größe kommt vom Server (HEAD).</summary>
    internal async Task<OfflineModel> ArchiveAsync(SidecarVariant variant, CancellationToken ct)
    {
        byte[] manifest;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(MetaTimeout);
        try
        {
            manifest = await http.GetByteArrayAsync(BaseUrl + "sidecar-manifest.json", limit.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new DownloadException(ModelDownloader.MsgNet, true, DownloadException.Kinds.Network, e);
        }
        if (!Convert.ToHexStringLower(SHA256.HashData(manifest)).Equals(ExpectedManifestSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new DownloadException(MsgManifest, false, DownloadException.Kinds.Checksum);
        }
        var entry = (JsonNode.Parse(manifest)?["files"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(f =>
            (string?)f["os"] == Os && (string?)f["arch"] == "x64" && (string?)f["variant"] == Key(variant))
            ?? throw new DownloadException(MsgManifest, false);
        var file = (string)entry["file"]!;
        var sha = (string)entry["sha256"]!;
        if (file.Contains('/') || file.Contains('\\')) throw new DownloadException(MsgManifest, false);
        using var head = new HttpRequestMessage(HttpMethod.Head, BaseUrl + file);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(head, limit.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new DownloadException(ModelDownloader.MsgNet, true, DownloadException.Kinds.Network, e);
        }
        using var _ = response;
        var bytes = response.Content.Headers.ContentLength ?? 0;
        if (!response.IsSuccessStatusCode || bytes <= 0) throw new DownloadException(ModelDownloader.MsgHttp((int)response.StatusCode), true, DownloadException.Kinds.Network);
        return new OfflineModel("whisper-server", "whisper-server", file, BaseUrl + file, bytes, sha);
    }

    /// <summary>Entpackt nur einfache Dateinamen in den Zielordner (keine Pfade aus dem Archiv), unter Linux ausführbar.</summary>
    internal static void Extract(string archive, string target)
    {
        var tmp = target + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var e in zip.Entries.Where(e => e.Name.Length > 0))
            {
                e.ExtractToFile(Path.Combine(tmp, e.Name), overwrite: true);
            }
        }
        else
        {
            using var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
            using var tar = new TarReader(gz);
            while (tar.GetNextEntry() is { } e)
            {
                if (e.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                {
                    e.ExtractToFile(Path.Combine(tmp, Path.GetFileName(e.Name)), overwrite: true);
                }
            }
        }
        var exe = Path.Combine(tmp, ExeName);
        if (!OperatingSystem.IsWindows() && File.Exists(exe))
        {
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(tmp, target);
    }
}
