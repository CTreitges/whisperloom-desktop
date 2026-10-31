using System.Security.Cryptography;
using WhisperLoom.Core.Local;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>
/// Port von <c>whisper/ModelDownloaderTest.kt</c>: Tests gegen einen lokalen Server, der Range kann und sich für die
/// Fehlerfälle verstellen lässt (Akzeptanz A4.5: Abbruch → Range-Fortsetzung → SHA256 ok bzw. falsch → Fehler).
/// </summary>
public sealed class ModelDownloaderTest : IAsyncLifetime
{
    private readonly byte[] _data = [.. Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31 + i / 7))];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wl-dl-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = ModelDownloader.CreateClient();
    private RangeServer _server = null!;
    private ModelStore _store = null!;
    private OfflineModel _model = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _server = new RangeServer(_data);
        _store = new ModelStore(Path.Combine(_root, "models"));
        // Lädt von seiner eigenen URL — der Pfad weicht bewusst vom Dateinamen ab.
        _model = new OfflineModel("test", "Test", "ggml-test.bin", _server.Url("/whisper/resolve/main/ggml-test.bin"), _data.Length, Sha256(_data));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ModelDownloader Downloader(int retries = 3, Func<string, long>? freeSpace = null, int readTimeoutMs = 30_000) =>
        new(_http, readTimeoutMs, retries, backoffMs: 5, progressIntervalMs: 50, freeSpace: freeSpace ?? (_ => long.MaxValue));

    private void WritePart(byte[] bytes)
    {
        _store.EnsureDir();
        File.WriteAllBytes(_store.PartFile(_model), bytes);
    }

    [Fact]
    public async Task KompletterDownloadWirdVerifiziertUndUmbenannt()
    {
        var progress = new List<long>();
        var done = await Downloader().DownloadAsync(_model, _store, (bytes, total, _) =>
        {
            Assert.Equal(_model.Bytes, total);
            progress.Add(bytes);
        }, Ct);
        Assert.True(done);
        Assert.True(_store.IsInstalled(_model));
        Assert.Equal(_data, File.ReadAllBytes(_store.File(_model)));
        Assert.False(File.Exists(_store.PartFile(_model)));
        Assert.Equal(_model.Bytes, progress[^1]);
        Assert.Equal(progress.Order(), progress);
        Assert.Equal([null], _server.Ranges);
    }

    [Fact]
    public async Task CancelMittendrinLaesstTeildateiStehen()
    {
        _server.SlowChunkMs = 20;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var done = await Downloader().DownloadAsync(_model, _store, (bytes, _, _) =>
        {
            if (bytes < _data.Length) cts.Cancel();
        }, cts.Token);
        Assert.False(done);
        var part = new FileInfo(_store.PartFile(_model));
        Assert.True(part.Exists);
        Assert.InRange(part.Length, 1, _data.Length - 1);
        Assert.False(File.Exists(_store.File(_model)));
        Assert.False(_store.IsInstalled(_model));
    }

    [Fact]
    public async Task CancelImBackoffStartetKeinenWeiterenVersuch()
    {
        _server.StatusOverride = 503;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var downloader = new ModelDownloader(_http, retries: 3, backoffMs: 2_000, freeSpace: _ => long.MaxValue);
        var task = downloader.DownloadAsync(_model, _store, ct: cts.Token);
        for (var i = 0; i < 500 && _server.Statuses.Count == 0 && !task.IsCompleted; i++) await Task.Delay(10, Ct);
        await cts.CancelAsync();
        Assert.False(await task);
        Assert.Equal([503], _server.Statuses);
        Assert.False(File.Exists(_store.PartFile(_model)));
    }

    [Fact]
    public async Task CancelBeiHaengenderVerbindungIstEinAbbruchKeinNetzfehler()
    {
        _server.HoldMs = 3_000;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(100);
        var started = Environment.TickCount64;
        Assert.False(await Downloader(readTimeoutMs: 200).DownloadAsync(_model, _store, ct: cts.Token));
        Assert.True(Environment.TickCount64 - started < 2_500, $"kam nach {Environment.TickCount64 - started} ms");
    }

    [Fact]
    public async Task ResumeSetztMitRangeFortUndPrueftDieGesamtsumme()
    {
        WritePart(_data[..100_000]);
        var progress = new List<long>();
        Assert.True(await Downloader().DownloadAsync(_model, _store, (bytes, _, _) => progress.Add(bytes), Ct));
        Assert.Equal(["bytes=100000-"], _server.Ranges);
        Assert.Equal([206], _server.Statuses);
        Assert.True(progress[0] >= 100_000);
        Assert.Equal(_data, File.ReadAllBytes(_store.File(_model)));
        Assert.True(_store.IsInstalled(_model));
    }

    [Fact]
    public async Task ServerOhneRangeStartetVonVorn()
    {
        _server.RangeSupported = false;
        WritePart(_data[..50_000]);
        Assert.True(await Downloader().DownloadAsync(_model, _store, ct: Ct));
        Assert.Equal(["bytes=50000-"], _server.Ranges);
        Assert.Equal([200], _server.Statuses);
        Assert.Equal(_data, File.ReadAllBytes(_store.File(_model)));
    }

    [Fact]
    public async Task KaputteTeildateiFaelltBeiDerPruefsummeAufUndWirdVerworfen()
    {
        WritePart(Enumerable.Repeat((byte)9, 10_000).ToArray());
        var e = await Assert.ThrowsAsync<DownloadException>(() => Downloader().DownloadAsync(_model, _store, ct: Ct));
        Assert.Equal(DownloadException.Kinds.Checksum, e.Kind);
        Assert.False(e.Retryable);
        Assert.False(File.Exists(_store.PartFile(_model)));
        Assert.False(File.Exists(_store.File(_model)));
        // Der nächste Versuch lädt von vorn und klappt.
        Assert.True(await Downloader().DownloadAsync(_model, _store, ct: Ct));
        Assert.Equal(["bytes=10000-", null], _server.Ranges);
        Assert.True(_store.IsInstalled(_model));
    }

    [Fact]
    public async Task ChecksumMismatchLoeschtDateiUndMeldetFehler()
    {
        var wrong = _model with { Sha256 = new string('0', 64) };
        var e = await Assert.ThrowsAsync<DownloadException>(() => Downloader().DownloadAsync(wrong, _store, ct: Ct));
        Assert.Equal(ModelDownloader.MsgChecksum, e.Message);
        Assert.Equal(DownloadException.Kinds.Checksum, e.Kind);
        Assert.False(e.Retryable);
        Assert.False(File.Exists(_store.PartFile(wrong)));
        Assert.False(File.Exists(_store.File(wrong)));
    }

    /// <summary>A4.5: Der Server bricht ab, der Download setzt per Range fort, die Prüfsumme stimmt.</summary>
    [Fact]
    public async Task NetzabbruchWirdWiederholtUndFortgesetzt()
    {
        _server.TruncateFirstResponseAt = 120_000;
        Assert.True(await Downloader(retries: 2).DownloadAsync(_model, _store, ct: Ct));
        Assert.Equal([null, "bytes=120000-"], _server.Ranges);
        Assert.Equal([200, 206], _server.Statuses);
        Assert.Equal(_data, File.ReadAllBytes(_store.File(_model)));
    }

    /// <summary>A4.5 mit falscher Prüfsumme: Abbruch, Fortsetzung per Range, dann Prüfsummenfehler und keine Datei.</summary>
    [Fact]
    public async Task NetzabbruchMitFalscherPruefsummeEndetImFehler()
    {
        _server.TruncateFirstResponseAt = 120_000;
        var wrong = _model with { Sha256 = Sha256([1, 2, 3]) };
        var e = await Assert.ThrowsAsync<DownloadException>(() => Downloader(retries: 2).DownloadAsync(wrong, _store, ct: Ct));
        Assert.Equal(DownloadException.Kinds.Checksum, e.Kind);
        Assert.Equal([null, "bytes=120000-"], _server.Ranges);
        Assert.False(File.Exists(_store.File(wrong)));
        Assert.False(File.Exists(_store.PartFile(wrong)));
    }

    [Fact]
    public async Task Http503NachAllenVersuchenRetryable()
    {
        _server.StatusOverride = 503;
        var e = await Assert.ThrowsAsync<DownloadException>(() => Downloader(retries: 2).DownloadAsync(_model, _store, ct: Ct));
        Assert.True(e.Retryable);
        Assert.Equal(DownloadException.Kinds.Network, e.Kind);
        Assert.Equal(ModelDownloader.MsgNet, e.Message);
        Assert.Equal(3, _server.Ranges.Count);
    }

    [Fact]
    public async Task Http404NichtRetryable()
    {
        _server.StatusOverride = 404;
        var e = await Assert.ThrowsAsync<DownloadException>(() => Downloader().DownloadAsync(_model, _store, ct: Ct));
        Assert.False(e.Retryable);
        Assert.Equal("Server antwortet mit HTTP 404", e.Message);
        Assert.Single(_server.Ranges);
    }

    [Fact]
    public async Task FalscheDateigroesseVomServer()
    {
        _server.Body = [.. _data, .. new byte[10]];
        var e = await Assert.ThrowsAsync<DownloadException>(() => Downloader().DownloadAsync(_model, _store, ct: Ct));
        Assert.False(e.Retryable);
        Assert.Contains("Dateigröße", e.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(_store.PartFile(_model)));
    }

    [Fact]
    public async Task OhneSpeicherplatzKeinRequest()
    {
        var e = await Assert.ThrowsAsync<DownloadException>(() => Downloader(freeSpace: _ => 0).DownloadAsync(_model, _store, ct: Ct));
        Assert.Equal(DownloadException.Kinds.Storage, e.Kind);
        Assert.False(e.Retryable);
        Assert.Equal(ModelDownloader.MsgStorage, e.Message);
        Assert.Empty(_server.Ranges);
    }

    /// <summary>Kotlin: Textmodell von eigener URL; Desktop: das Archiv des Begleitprozesses lädt genauso.</summary>
    [Fact]
    public async Task ArchivLaedtVonSeinerUrlUnterSeinemDateinamen()
    {
        var archive = _model with { Id = "whisper-server", FileName = "whisper-server-b1-win-x64-cpu.zip", Url = _server.Url("/releases/download/x/a.zip"), ExtraDiskBytes = 1_000 };
        Assert.True(await Downloader().DownloadAsync(archive, _store, ct: Ct));
        Assert.Equal(["/releases/download/x/a.zip"], _server.Paths);
        Assert.Equal(_data, File.ReadAllBytes(Path.Combine(_store.Dir, "whisper-server-b1-win-x64-cpu.zip")));
        Assert.True(_store.IsInstalled(archive));
        Assert.False(_store.IsInstalled(_model));
    }

    [Fact]
    public async Task PlatzpruefungRechnetDenZusatzplatzMit()
    {
        var big = _model with { ExtraDiskBytes = 1_000 };
        var e = await Assert.ThrowsAsync<DownloadException>(() =>
            Downloader(freeSpace: _ => big.Bytes + big.ExtraDiskBytes - 1).DownloadAsync(big, _store, ct: Ct));
        Assert.Equal(DownloadException.Kinds.Storage, e.Kind);
        Assert.Empty(_server.Ranges);
        Assert.True(await Downloader(freeSpace: _ => big.Bytes + big.ExtraDiskBytes).DownloadAsync(big, _store, ct: Ct));
    }

    [Fact]
    public async Task BereitsInstalliertOhneRequest()
    {
        _store.EnsureDir();
        File.WriteAllBytes(_store.File(_model), _data);
        Assert.True(await Downloader().DownloadAsync(_model, _store, ct: Ct));
        Assert.Empty(_server.Ranges);
    }

    [Fact]
    public async Task VollstaendigeTeildateiWirdNurNochVerifiziert()
    {
        WritePart(_data);
        Assert.True(await Downloader().DownloadAsync(_model, _store, ct: Ct));
        Assert.Empty(_server.Ranges);
        Assert.True(_store.IsInstalled(_model));
    }

    [Fact]
    public void ContentRangeTotal()
    {
        Assert.Equal(190_000L, ModelDownloader.ContentRangeTotal("bytes 1000-189999/190000"));
        Assert.Equal(-1L, ModelDownloader.ContentRangeTotal("bytes 0-99/*"));
        Assert.Equal(-1L, ModelDownloader.ContentRangeTotal(null));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
