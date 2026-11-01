using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Installiert <c>llama-server</c> (mitgeliefertes Textmodell, W2-LOCAL) bei Bedarf aus dem offiziellen llama.cpp-Release
/// <see cref="Tag"/> in den Datenordner: Archiv mit hier festgeschriebener Größe und SHA-256 (<see cref="ModelDownloader"/>:
/// Range, Prüfsumme, Platz, Wiederholungen), Entpacken nach <c>&lt;ordner&gt;/&lt;tag&gt;-&lt;os&gt;-&lt;variante&gt;/</c>.
/// Varianten wie bei <c>whisper-server</c>: Vulkan (GPU) und CPU, Windows und Linux x64.
/// </summary>
// Doku: https://github.com/ggml-org/llama.cpp/releases/tag/b11569 (Größen und SHA-256 aus dem Feld „digest“ der
// Release-Assets, abgerufen 2026-11-01), https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md
public sealed class LlamaRelease(HttpClient http, string dir)
{
    public const string Tag = "b11569";

    public const string ReleaseUrl = "https://github.com/ggml-org/llama.cpp/releases/download/" + Tag + "/";

    public const string MsgExtract = "Textmodell-Server konnte nicht entpackt werden";

    /// <summary>Archiv je Betriebssystem und Variante: Name, Größe, SHA-256.</summary>
    public static readonly IReadOnlyDictionary<(string Os, SidecarVariant Variant), (string File, long Bytes, string Sha256)> Assets =
        new Dictionary<(string, SidecarVariant), (string, long, string)>
        {
            [("win", SidecarVariant.Cpu)] = ($"llama-{Tag}-bin-win-cpu-x64.zip", 19_502_348L, "8f6829181423f1f1f1fc27c6976445e54b09ac9751f337be14be28bd1d313372"),
            [("win", SidecarVariant.Vulkan)] = ($"llama-{Tag}-bin-win-vulkan-x64.zip", 32_084_619L, "da9a44e01cc51b5ef3070be3369dde6c8af0c7853c23f814b59a1bc971ab30c8"),
            [("linux", SidecarVariant.Cpu)] = ($"llama-{Tag}-bin-ubuntu-x64.tar.gz", 17_712_742L, "ad8acc130fa5337b7c044c8d4fef4ce0a6efb3543b8495b272b0a7f6848b6224"),
            [("linux", SidecarVariant.Vulkan)] = ($"llama-{Tag}-bin-ubuntu-vulkan-x64.tar.gz", 30_275_565L, "4e32ba6b004ce366b9306a6b451efccedb4b14fb75c0867bd2368d259c55315a"),
        };

    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    public string Dir { get; } = dir;

    /// <summary>Für Tests: andere Release-Adresse (lokaler Fake) bzw. andere Archive.</summary>
    public string BaseUrl { get; init; } = ReleaseUrl;

    public IReadOnlyDictionary<(string Os, SidecarVariant Variant), (string File, long Bytes, string Sha256)> Files { get; init; } = Assets;

    public static string ExeName => OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";

    public string ExePath(SidecarVariant variant) => Path.Combine(Dir, $"{Tag}-{SidecarRelease.Os}-{SidecarRelease.Key(variant)}", ExeName);

    public bool IsInstalled(SidecarVariant variant) => File.Exists(ExePath(variant));

    /// <summary>Installiert die Variante, falls nötig, und liefert den Pfad der ausführbaren Datei.</summary>
    /// <exception cref="DownloadException">Download, Prüfsumme oder Platz.</exception>
    public async Task<string> EnsureAsync(SidecarVariant variant, DownloadProgress? progress, CancellationToken ct)
    {
        var exe = ExePath(variant);
        if (File.Exists(exe)) return exe;
        if (RuntimeInformation.OSArchitecture != Architecture.X64) throw new DownloadException("Textmodell-Server gibt es nur für x64", false);
        await InstallGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(exe)) return exe;
            var (file, bytes, sha) = Files[(SidecarRelease.Os, variant)];
            var archive = new OfflineModel("llama-server", "llama-server", file, BaseUrl + file, bytes, sha);
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

    /// <summary>Link-Ziel nur ein Dateiname im selben Ordner: kein Pfad, nicht leer, nicht „.“/„..“ (Prüfrunde 1 D-1).</summary>
    internal static bool IsSafeLinkTarget(string target) =>
        target is not ("" or "." or "..") && Path.GetFileName(target) == target;

    /// <summary>
    /// Entpackt nur einfache Dateinamen (ohne Pfade aus dem Archiv) in den Zielordner. Linux: Rechte aus dem Archiv,
    /// Symlinks der Bibliotheken (<c>libllama.so.0 → libllama.so.0.6.0</c>) nur auf Namen im selben Ordner.
    /// </summary>
    internal static void Extract(string archive, string target)
    {
        var tmp = target + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var e in zip.Entries)
            {
                // W2-FIX N-9: nie aus dem Zielordner heraus (Zips „made by Unix“ mit ..\, absolut, Laufwerk).
                if (ArchiveNames.SafePath(tmp, e.Name) is { } path)
                {
                    e.ExtractToFile(path, overwrite: true);
                }
            }
        }
        else
        {
            using var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
            using var tar = new TarReader(gz);
            var links = new List<(string Name, string Target)>();
            while (tar.GetNextEntry() is { } e)
            {
                if (ArchiveNames.SafePath(tmp, e.Name) is not { } path) continue;
                var name = Path.GetFileName(path);
                if (e.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                {
                    e.ExtractToFile(path, overwrite: true);
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, e.Mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute));
                }
                else if (e.EntryType == TarEntryType.SymbolicLink && IsSafeLinkTarget(e.LinkName))
                {
                    links.Add((name, e.LinkName));
                }
            }
            if (OperatingSystem.IsWindows())
            {
                // Ohne Symlinks: Kopien, Ketten (libllama.so → .so.0 → .so.0.6.0) in mehreren Durchgängen.
                for (var progress = true; progress;)
                {
                    progress = false;
                    foreach (var (name, linkTarget) in links.Where(l => !File.Exists(Path.Combine(tmp, l.Name)) && File.Exists(Path.Combine(tmp, l.Target))))
                    {
                        File.Copy(Path.Combine(tmp, linkTarget), Path.Combine(tmp, name));
                        progress = true;
                    }
                }
            }
            else
            {
                foreach (var (name, linkTarget) in links)
                {
                    File.CreateSymbolicLink(Path.Combine(tmp, name), linkTarget);
                }
            }
        }
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(tmp, target);
    }
}
