using System.Runtime.InteropServices;
using Microsoft.Extensions.Time.Testing;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Tests.Local;

/// <summary>
/// Randfälle der Begleitprozesse (Kompass 01a1ca1a-456f, TS-WBERICHT Hinweise 7 und 10) ohne echten Prozess und ohne Netz:
/// Die Vulkan-Sperre gilt dauerhaft nur nach einem Startfehler, nach einem Download- oder Netzfehler nur zehn Minuten;
/// <c>IsReadyAsync</c> prüft wie im Vertrag auch die Startbarkeit (x64).
/// </summary>
public sealed class CompanionEdgeTest : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wl-companion-").FullName;
    private readonly CountingHandler _net = new();
    private readonly HttpClient _http;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 25, 20, 0, 0, TimeSpan.Zero));

    public CompanionEdgeTest() => _http = new HttpClient(_net);

    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Jede Anfrage scheitert wie offline (Manifest nicht erreichbar).</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            throw new HttpRequestException("offline (Test)");
        }
    }

    // Erkennung (whisper-server)

    private WhisperLoomSettings SttSettings()
    {
        var s = new WhisperLoomSettings { Engine = "offline" };
        s.Offline.Model = "small";
        LocalSettings.Set(s, new LocalSettings { Backend = "auto" });
        Directory.CreateDirectory(Path.Combine(_dir, "models"));
        using var small = File.Create(Path.Combine(_dir, "models", ModelCatalog.Small.FileName));
        small.SetLength(ModelCatalog.Small.Bytes);
        return s;
    }

    private LocalStt Stt(WhisperLoomSettings s, Architecture arch = Architecture.X64) =>
        new(_dir, () => s, _http) { Time = _time, Arch = () => arch };

    [Fact]
    public async Task Stt_Downloadfehler_sperrt_Vulkan_nur_zehn_Minuten()
    {
        var s = SttSettings();
        await using var stt = Stt(s);
        await Assert.ThrowsAsync<LocalNotAvailableException>(() => stt.EnsureServerAsync(CancellationToken.None));
        Assert.Equal(2, _net.Requests); // Vulkan, dann CPU
        Assert.True(stt.VulkanBlocked);

        _time.Advance(TimeSpan.FromMinutes(9));
        await Assert.ThrowsAsync<LocalNotAvailableException>(() => stt.EnsureServerAsync(CancellationToken.None));
        Assert.Equal(3, _net.Requests); // innerhalb der Sperre nur CPU

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(stt.VulkanBlocked);
        await Assert.ThrowsAsync<LocalNotAvailableException>(() => stt.EnsureServerAsync(CancellationToken.None));
        Assert.Equal(5, _net.Requests); // nach Ablauf wieder Vulkan, dann CPU
    }

    [Fact]
    public async Task Stt_Startfehler_sperrt_Vulkan_fuer_die_Sitzung()
    {
        var s = SttSettings();
        await using var stt = Stt(s);
        foreach (var variant in new[] { SidecarVariant.Vulkan, SidecarVariant.Cpu })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stt.Release.ExePath(variant))!);
            await File.WriteAllTextAsync(stt.Release.ExePath(variant), "kaputt", TestContext.Current.CancellationToken);
        }
        await Assert.ThrowsAsync<LocalNotAvailableException>(() => stt.EnsureServerAsync(CancellationToken.None));
        Assert.True(stt.VulkanBlocked);
        _time.Advance(TimeSpan.FromHours(2));
        Assert.True(stt.VulkanBlocked);
        Assert.Equal(0, _net.Requests);
    }

    [Fact]
    public void Nicht_wiederholbarer_Downloadfehler_sperrt_Vulkan_fuer_die_Sitzung()
    {
        // Prüfsumme, Manifest, Archiv ohne Exe: ein neuer Versuch hilft nicht (Prüfer-Befund 3, PR #61).
        var block = new VulkanBlock(_time);
        block.Failed(new DownloadException(ModelDownloader.MsgChecksum, false, DownloadException.Kinds.Checksum));
        _time.Advance(TimeSpan.FromHours(2));
        Assert.True(block.Active);
    }

    [Fact]
    public void Wiederholbarer_Downloadfehler_sperrt_Vulkan_zehn_Minuten()
    {
        var block = new VulkanBlock(_time);
        block.Failed(new DownloadException(ModelDownloader.MsgNet, true, DownloadException.Kinds.Network));
        _time.Advance(TimeSpan.FromMinutes(9));
        Assert.True(block.Active);
        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(block.Active);
    }

    [Fact]
    public async Task Stt_ist_auf_arm64_nicht_bereit()
    {
        var s = SttSettings();
        await using var x64 = Stt(s);
        Assert.True(await x64.IsReadyAsync(CancellationToken.None));
        await using var arm = Stt(s, Architecture.Arm64);
        Assert.False(await arm.IsReadyAsync(CancellationToken.None));
    }

    /// <summary>
    /// Hinweis 6: Der Leerlauf läuft ab, während ein Diktat den Gate hält (<c>EnsureServerAsync</c>) und den laufenden
    /// Prozess übernimmt (<c>_idle?.Cancel()</c>). Danach darf der Timer nicht mehr stoppen. Messbar ohne Prozess: ein Stopp
    /// geht über <c>StopCoreAsync</c>, das den Leerlauf-Timer leert.
    /// </summary>
    private static async Task TakeoverDuringIdleStop(SemaphoreSlim gate, Action schedule, Func<CancellationTokenSource?> idle, Func<Task> idleTask)
    {
        await gate.WaitAsync(TestContext.Current.CancellationToken); // ein Diktat hält den Gate
        schedule(); // Leerlauf 1 ms: läuft ab und wartet dann am Gate
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var timer = idle();
        Assert.NotNull(timer);
        timer.Cancel(); // Übernahme wie in EnsureServerAsync
        gate.Release();
        await idleTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Same(timer, idle()); // kein Stopp nach der Übernahme
    }

    [Fact]
    public async Task Stt_Leerlauf_stoppt_nicht_nach_Uebernahme()
    {
        var s = SttSettings();
        await using var stt = new LocalStt(_dir, () => s, _http) { IdleTimeout = TimeSpan.FromMilliseconds(1) };
        await TakeoverDuringIdleStop(stt.Gate, () => stt.ScheduleIdleStop(0), () => stt.Idle, () => stt.IdleTask);
    }

    [Fact]
    public async Task Stt_Leerlauf_ohne_Uebernahme_stoppt()
    {
        var s = SttSettings();
        await using var stt = new LocalStt(_dir, () => s, _http) { IdleTimeout = TimeSpan.FromMilliseconds(1) };
        stt.ScheduleIdleStop(0);
        await stt.IdleTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Null(stt.Idle);
    }

    [Fact]
    public async Task Text_Leerlauf_stoppt_nicht_nach_Uebernahme()
    {
        var s = new WhisperLoomSettings { Engine = "offline" };
        LocalSettings.Set(s, new LocalSettings { TextEngine = "bundled" });
        await using var engine = new BundledTextEngine(_dir, () => s, CoreText.Prompts, new TextCleanup([]), _http) { IdleTimeout = TimeSpan.FromMilliseconds(1) };
        await TakeoverDuringIdleStop(engine.Gate, () => engine.ScheduleIdleStop(0), () => engine.Idle, () => engine.IdleTask);
    }

    // Textmodell (llama-server)

    private BundledTextEngine Text(Architecture arch = Architecture.X64)
    {
        var s = new WhisperLoomSettings { Engine = "offline" };
        LocalSettings.Set(s, new LocalSettings { TextEngine = "bundled", Backend = "auto" });
        var engine = new BundledTextEngine(_dir, () => s, CoreText.Prompts, new TextCleanup([]), _http, deviceRam: () => 32L << 30)
        {
            Time = _time,
            Arch = () => arch,
        };
        engine.Release.DownloadBackoffMs = 1; // sonst 2 + 4 + 8 s Wartezeit je gescheitertem Download
        Directory.CreateDirectory(engine.Models.Dir);
        using var f = File.Create(engine.Models.File(TextModelCatalog.Default.File));
        f.SetLength(TextModelCatalog.Default.File.Bytes);
        return engine;
    }

    [Fact]
    public async Task Text_Downloadfehler_sperrt_Vulkan_nur_zehn_Minuten()
    {
        await using var engine = Text();
        await Assert.ThrowsAsync<LocalNotAvailableException>(() => engine.EnsureServerAsync(CancellationToken.None));
        var both = _net.Requests; // Vulkan und CPU (je mit den Wiederholungen des Downloads)
        Assert.True(both > 0);
        Assert.True(engine.VulkanBlocked);
        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.False(engine.VulkanBlocked);
        await Assert.ThrowsAsync<LocalNotAvailableException>(() => engine.EnsureServerAsync(CancellationToken.None));
        Assert.Equal(2 * both, _net.Requests); // wieder beide Varianten, nicht nur die CPU
    }

    [Fact]
    public async Task Text_ist_auf_arm64_nicht_bereit()
    {
        await using var x64 = Text();
        Assert.True(await x64.IsReadyAsync(CancellationToken.None));
        await using var arm = Text(Architecture.Arm64);
        Assert.False(await arm.IsReadyAsync(CancellationToken.None));
    }
}
