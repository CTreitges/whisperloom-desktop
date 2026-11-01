using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Offline;

namespace WhisperLoom.App.Tests.Local;

/// <summary>
/// W2-LOCAL: mitgeliefertes Textmodell (<c>local.textEngine = "bundled"</c>) ohne echten Prozess: Modellwahl, RAM-Grenze,
/// Engine-Wahl, Vorladen, Startargumente und die Installation von <c>llama-server</c> gegen ein Fake-Release.
/// </summary>
public sealed class BundledTextEngineTest : IAsyncLifetime
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wl-bundled-").FullName;
    private readonly RangeServer _server = new([]);
    private readonly HttpClient _http = ModelDownloader.CreateClient();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    private static WhisperLoomSettings Settings(string engine = "bundled", string model = "")
    {
        var s = new WhisperLoomSettings { Engine = "offline" };
        LocalSettings.Set(s, new LocalSettings { TextEngine = engine, BundledModel = model });
        return s;
    }

    private BundledTextEngine Engine(WhisperLoomSettings s, long ram = 32L << 30) =>
        new(_dir, () => s, CoreText.Prompts, new TextCleanup([]), _http, deviceRam: () => ram);

    [Fact]
    public async Task Missing_model_is_not_ready_with_a_clear_message()
    {
        await using var engine = Engine(Settings());

        Assert.False(await engine.IsReadyAsync(CancellationToken.None));
        var e = await Assert.ThrowsAsync<LocalNotAvailableException>(() => engine.EnsureServerAsync(CancellationToken.None));
        Assert.Equal(BundledTextEngine.MsgModelMissing(TextModelCatalog.Default), e.Message);
    }

    [Fact]
    public async Task Model_needs_enough_ram()
    {
        var s = Settings(model: TextModelCatalog.Qwen3_4B.Id);
        await using var engine = Engine(s, ram: 8L << 30);

        Assert.Equal(TextModelCatalog.MsgTooLittleRam(TextModelCatalog.Qwen3_4B), engine.Model().Error);
        Assert.False(await engine.IsReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Installed_model_is_ready_and_the_label_is_its_id()
    {
        await using var engine = Engine(Settings());
        Directory.CreateDirectory(engine.Models.Dir);
        using (var f = File.Create(engine.Models.File(TextModelCatalog.Default.File)))
        {
            f.SetLength(TextModelCatalog.Default.File.Bytes);
        }

        Assert.True(await engine.IsReadyAsync(CancellationToken.None));
        Assert.Equal("qwen2.5-1.5b", engine.ModelLabel);
    }

    [Theory]
    [InlineData("ollama", typeof(OllamaLocalRefiner))]
    [InlineData("bundled", typeof(BundledTextEngine))]
    [InlineData("server", typeof(LocalServerRefiner))]
    [InlineData("unbekannt", typeof(OllamaLocalRefiner))]
    public async Task Text_engine_follows_the_setting(string key, Type expected)
    {
        var s = Settings(key);
        await using var bundled = Engine(s);
        var text = Text(s, bundled);

        Assert.IsType(expected, text.Current);
    }

    [Fact]
    public void Text_server_is_the_named_or_the_first_text_server()
    {
        var local = new LocalSettings
        {
            Servers = [new() { Name = "w", Kind = "stt", BaseUrl = "http://127.0.0.1:1/v1" }, new() { Name = "a", Kind = "text", BaseUrl = "http://127.0.0.1:2/v1" },
                new() { Name = "b", Kind = "text", BaseUrl = "http://127.0.0.1:3/v1" }],
        };

        Assert.Equal("a", LocalTextEngine.TextServer(local)!.Name);
        local.TextServer = "b";
        Assert.Equal("b", LocalTextEngine.TextServer(local)!.Name);
        local.TextServer = "w"; // Erkennungs-Server taugt nicht
        Assert.Null(LocalTextEngine.TextServer(local));
    }

    [Theory]
    [InlineData("bundled", true, true)]
    [InlineData("bundled", false, false)]
    [InlineData("ollama", true, false)]
    public async Task Warm_up_only_for_the_bundled_model_and_a_local_stage(string engine, bool localStage, bool expected)
    {
        var s = Settings(engine);
        await using var bundled = Engine(s);

        var warmed = await LocalWarmUp.WarmUpAsync(_ => Task.FromResult(localStage), Text(s, bundled));

        Assert.Equal(expected, warmed);
        Assert.Null(bundled.Server); // Modell fehlt: kein Prozess, keine Ausnahme
    }

    [Fact]
    public void Arguments_bind_to_loopback_and_use_the_gpu_only_with_vulkan()
    {
        var cpu = LlamaServerProcess.Arguments("m.gguf", 4242, gpu: false);
        var gpu = LlamaServerProcess.Arguments("m.gguf", 4242, gpu: true);

        Assert.Equal(["-m", "m.gguf", "--host", "127.0.0.1", "--port", "4242"], cpu.Take(6));
        Assert.DoesNotContain("-ngl", cpu);
        Assert.Contains("-ngl", gpu);
        Assert.Contains("--no-webui", cpu);
    }

    [Fact]
    public async Task Release_installs_the_pinned_archive_and_rejects_a_tampered_one()
    {
        var (name, archive) = FakeArchive();
        _server.Files["/rel/" + name] = archive;
        var key = (SidecarRelease.Os, SidecarVariant.Cpu);
        var good = new LlamaRelease(_http, Path.Combine(_dir, "good")) { BaseUrl = _server.Url("/rel/"), Files = new Dictionary<(string, SidecarVariant), (string, long, string)> { [key] = (name, archive.Length, Sha(archive)) } };
        var bad = new LlamaRelease(_http, Path.Combine(_dir, "bad")) { BaseUrl = _server.Url("/rel/"), Files = new Dictionary<(string, SidecarVariant), (string, long, string)> { [key] = (name, archive.Length, new string('0', 64)) } };

        var exe = await good.EnsureAsync(SidecarVariant.Cpu, null, CancellationToken.None);
        var e = await Assert.ThrowsAsync<DownloadException>(() => bad.EnsureAsync(SidecarVariant.Cpu, null, CancellationToken.None));

        Assert.Equal(good.ExePath(SidecarVariant.Cpu), exe);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, OperatingSystem.IsWindows() ? "llama.dll" : "libllama.so.0")));
        Assert.Equal(DownloadException.Kinds.Checksum, e.Kind);
        Assert.False(bad.IsInstalled(SidecarVariant.Cpu));
    }

    [Theory]
    [InlineData("libllama.so.0.6.0", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData("../../etc/passwd", false)]
    [InlineData("sub/lib.so", false)]
    public void Link_targets_stay_in_the_folder(string target, bool safe)
    {
        Assert.Equal(safe, LlamaRelease.IsSafeLinkTarget(target));
    }

    [Fact]
    public void Linux_archive_keeps_library_links_and_drops_paths()
    {
        var archive = Path.Combine(_dir, "a.tar.gz");
        File.WriteAllBytes(archive, Tar());
        var target = Path.Combine(_dir, "x");

        LlamaRelease.Extract(archive, target);

        Assert.True(File.Exists(Path.Combine(target, "llama-server")));
        Assert.Equal("lib", File.ReadAllText(Path.Combine(target, "libllama.so.0"))); // Link bzw. unter Windows Kopie
        Assert.False(File.Exists(Path.Combine(target, "evil")));
        Assert.False(Path.Exists(Path.Combine(target, "up")));
        Assert.False(Path.Exists(Path.Combine(target, "self")));
    }

    private LocalTextEngine Text(WhisperLoomSettings s, BundledTextEngine bundled) => new(
        new OllamaLocalRefiner(_http, () => "", CoreText.Prompts, new TextCleanup([])),
        bundled,
        new LocalServerRefiner(_http, () => null, CoreText.Prompts, new TextCleanup([])),
        () => s);

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static (string Name, byte[] Bytes) FakeArchive()
    {
        if (!OperatingSystem.IsWindows()) return ("llama-test.tar.gz", Tar());
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var n in new[] { "llama-server.exe", "llama.dll" })
            {
                using var w = new StreamWriter(zip.CreateEntry(n).Open());
                w.Write("fake");
            }
        }
        return ("llama-test.zip", ms.ToArray());
    }

    /// <summary>Wie das echte Linux-Archiv: Ordner davor, Bibliothek mit Symlink-Kette, dazu ein Link nach außen.</summary>
    private static byte[] Tar()
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gz))
        {
            void Add(string name, string content)
            {
                var e = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.ASCII.GetBytes(content)), Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute };
                tar.WriteEntry(e);
            }
            Add("llama-b0/llama-server", "#!/bin/sh\n");
            Add("llama-b0/libllama.so.0.6.0", "lib");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "llama-b0/libllama.so.0") { LinkName = "libllama.so.0.6.0" });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "llama-b0/libllama.so") { LinkName = "libllama.so.0" });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "llama-b0/evil") { LinkName = "../../etc/passwd" });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "llama-b0/up") { LinkName = ".." }); // Prüfrunde 1 D-1
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "llama-b0/self") { LinkName = "." });
        }
        return ms.ToArray();
    }
}
