using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Local;
using WhisperLoom.Core.Tests.Offline;

namespace WhisperLoom.App.Tests.Local;

/// <summary>Installation des Begleitprozesses gegen ein Fake-Release: Manifest-Anker, Archiv-Prüfsumme, Entpacken.</summary>
public sealed class SidecarReleaseTest : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-release-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = ModelDownloader.CreateClient();
    private readonly RangeServer _server = new([]);
    private readonly byte[] _exe = Encoding.ASCII.GetBytes("#!/bin/sh\necho fake\n");
    private byte[] _archive = [];
    private string _file = "";

    public ValueTask InitializeAsync()
    {
        _file = $"whisper-server-{SidecarRelease.Tag}-{SidecarRelease.Os}-x64-cpu" + (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz");
        _archive = Archive();
        _server.Files["/rel/" + _file] = _archive;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private SidecarRelease Release(string? archiveSha = null, bool tamperManifest = false)
    {
        var manifest = new JsonObject
        {
            ["tag"] = SidecarRelease.Tag,
            ["files"] = new JsonArray(new JsonObject
            {
                ["file"] = _file,
                ["sha256"] = archiveSha ?? Sha(_archive),
                ["variant"] = "cpu",
                ["os"] = SidecarRelease.Os,
                ["arch"] = "x64",
            }),
        };
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        _server.Files["/rel/sidecar-manifest.json"] = bytes;
        return new SidecarRelease(_http, _dir)
        {
            BaseUrl = _server.Url("/rel/"),
            ExpectedManifestSha256 = tamperManifest ? new string('0', 64) : Sha(bytes),
        };
    }

    [Fact]
    public async Task InstalliertAusDemManifestUndEntpackt()
    {
        var release = Release();
        Assert.False(release.IsInstalled(SidecarVariant.Cpu));
        var exe = await release.EnsureAsync(SidecarVariant.Cpu, null, TestContext.Current.CancellationToken);
        Assert.Equal(release.ExePath(SidecarVariant.Cpu), exe);
        Assert.Equal(_exe, await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "LICENSE-whisper.cpp.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, _file)), "Archiv bleibt nicht liegen");
        if (!OperatingSystem.IsWindows()) Assert.True(File.GetUnixFileMode(exe).HasFlag(UnixFileMode.UserExecute));
        // Installiert: kein weiterer Request.
        var before = _server.Paths.Count;
        await release.EnsureAsync(SidecarVariant.Cpu, null, TestContext.Current.CancellationToken);
        Assert.Equal(before, _server.Paths.Count);
    }

    [Fact]
    public async Task ManifestOhnePassendePruefsummeWirdAbgelehnt()
    {
        var e = await Assert.ThrowsAsync<DownloadException>(() =>
            Release(tamperManifest: true).EnsureAsync(SidecarVariant.Cpu, null, TestContext.Current.CancellationToken));
        Assert.Equal(SidecarRelease.MsgManifest, e.Message);
        Assert.DoesNotContain(_server.Paths, p => p.EndsWith(_file, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArchivMitFalscherPruefsummeWirdVerworfen()
    {
        var release = Release(archiveSha: new string('a', 64));
        var e = await Assert.ThrowsAsync<DownloadException>(() => release.EnsureAsync(SidecarVariant.Cpu, null, TestContext.Current.CancellationToken));
        Assert.Equal(DownloadException.Kinds.Checksum, e.Kind);
        Assert.False(release.IsInstalled(SidecarVariant.Cpu));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task VarianteFehltImManifest()
    {
        var e = await Assert.ThrowsAsync<DownloadException>(() =>
            Release().EnsureAsync(SidecarVariant.Vulkan, null, TestContext.Current.CancellationToken));
        Assert.Equal(SidecarRelease.MsgManifest, e.Message);
    }

    [Fact]
    public void EntpacktNurDateinamenOhnePfade()
    {
        Directory.CreateDirectory(_dir);
        var zip = Path.Combine(_dir, "a.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(archive.CreateEntry("../../boese.txt").Open());
            w.Write("x");
        }
        SidecarRelease.Extract(zip, Path.Combine(_dir, "ziel"));
        Assert.True(File.Exists(Path.Combine(_dir, "ziel", "boese.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_dir)!, "boese.txt")));
    }

    private byte[] Archive()
    {
        using var ms = new MemoryStream();
        if (OperatingSystem.IsWindows())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "whisper-server.exe", _exe);
                Add(zip, "LICENSE-whisper.cpp.txt", "MIT"u8.ToArray());
            }
        }
        else
        {
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            using (var tar = new TarWriter(gz))
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "whisper-server") { DataStream = new MemoryStream(_exe) });
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "LICENSE-whisper.cpp.txt") { DataStream = new MemoryStream("MIT"u8.ToArray()) });
            }
        }
        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string name, byte[] data)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(data);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
