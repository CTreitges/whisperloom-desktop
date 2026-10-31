using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Lokale Erkennung (<see cref="ILocalStt"/>) über den Begleitprozess: startet bei Bedarf (Binary aus dem Release in den
/// Datenordner), Vulkan und bei Fehler CPU, endet nach Leerlauf (Standard 10 min), mit dem Modellwechsel und mit der App.
/// Ordner: <c>&lt;lokal&gt;/models</c> (Modelle) und <c>&lt;lokal&gt;/whisper-server</c> (Binaries).
/// </summary>
public sealed class LocalStt : ILocalStt, IAsyncDisposable
{
    public const string MsgModelMissing = "Lokales Erkennungsmodell fehlt — unter „Lokal“ laden";
    public const string MsgSidecarFailed = "Lokaler Erkennungsdienst startet nicht";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(120);

    private readonly Func<WhisperLoomSettings> _settings;
    private readonly HttpClient _http;
    private readonly Func<WhisperLoomSettings, string> _prompt;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperServerProcess? _server;
    private CancellationTokenSource? _idle;
    private bool _vulkanFailed;

    /// <param name="localDir">Ordner für Modelle und Binaries, Standard <c>&lt;Datenordner&gt;/local</c>.</param>
    /// <param name="downloadHttp">Client für Downloads (mit Proxy der Einstellungen); null = ohne Nachladen.</param>
    /// <param name="prompt">Kontext-Wörter für die Erkennung.</param>
    public LocalStt(string localDir, Func<WhisperLoomSettings> settings, HttpClient? downloadHttp = null, Func<WhisperLoomSettings, string>? prompt = null)
    {
        _settings = settings;
        _prompt = prompt ?? (_ => "");
        _http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        Models = new ModelStore(Path.Combine(localDir, "models"));
        Release = new SidecarRelease(downloadHttp ?? _http, Path.Combine(localDir, "whisper-server"));
    }

    public string Label => "Lokal";

    /// <summary>Leerlauf bis zum Ende des Begleitprozesses; null = aus den Einstellungen (Minuten).</summary>
    public TimeSpan? IdleTimeout { get; init; }

    public ModelStore Models { get; }

    public SidecarRelease Release { get; }

    /// <summary>Client ohne Proxy für den Begleitprozess auf 127.0.0.1.</summary>
    internal HttpClient LoopbackHttp => _http;

    /// <summary>Der laufende Begleitprozess (null = keiner).</summary>
    public WhisperServerProcess? Server => _server;

    /// <summary>"vulkan"/"cpu" und Gerät des zuletzt gestarteten Prozesses; null = noch keiner.</summary>
    public (string Backend, string Device)? LastBackend { get; private set; }

    public Task<bool> IsReadyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Models.IsInstalled(Model()));
    }

    public async Task<TranscriptResult> TranscribeAsync(AudioBuffer audio, string language, CancellationToken ct)
    {
        var s = _settings();
        var server = await EnsureServerAsync(ct).ConfigureAwait(false);
        try
        {
            return await WhisperServerClient.TranscribeAsync(_http, server.BaseUrl, audio, language, _prompt(s), s.Offline.Accurate, ct).ConfigureAwait(false);
        }
        finally
        {
            ScheduleIdleStop(LocalSettings.Of(s).IdleMinutes);
        }
    }

    /// <summary>Startet den Begleitprozess für das gewählte Modell (oder nutzt den laufenden).</summary>
    /// <exception cref="LocalNotAvailableException">Modell fehlt oder der Prozess startet weder mit Vulkan noch mit CPU.</exception>
    public async Task<WhisperServerProcess> EnsureServerAsync(CancellationToken ct, DownloadProgress? progress = null)
    {
        var model = Model();
        if (!Models.IsInstalled(model)) throw new LocalNotAvailableException(MsgModelMissing);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _idle?.Cancel();
            var path = Models.File(model);
            if (_server is { HasExited: false } running && running.Model == path) return running;
            await StopCoreAsync().ConfigureAwait(false);
            var cpuOnly = LocalSettings.Of(_settings()).Backend == "cpu";
            if (!cpuOnly && !_vulkanFailed)
            {
                try
                {
                    return Started(await StartAsync(SidecarVariant.Vulkan, path, progress, ct).ConfigureAwait(false));
                }
                catch (Exception e) when (e is SidecarStartException or DownloadException or IOException && !ct.IsCancellationRequested)
                {
                    _vulkanFailed = true; // für diese Sitzung: CPU
                }
            }
            try
            {
                return Started(await StartAsync(SidecarVariant.Cpu, path, progress, ct).ConfigureAwait(false));
            }
            catch (Exception e) when (e is SidecarStartException or DownloadException or IOException && !ct.IsCancellationRequested)
            {
                throw new LocalNotAvailableException($"{MsgSidecarFailed}: {e.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Beendet den Begleitprozess (Leerlauf, Modellwechsel, App-Ende).</summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _http.Dispose();
        _gate.Dispose();
    }

    private OfflineModel Model() => ModelCatalog.ById(_settings().Offline.Model);

    private async Task<WhisperServerProcess> StartAsync(SidecarVariant variant, string model, DownloadProgress? progress, CancellationToken ct)
    {
        var exe = await Release.EnsureAsync(variant, progress, ct).ConfigureAwait(false);
        return await WhisperServerProcess.StartAsync(exe, model, _http, noGpu: variant == SidecarVariant.Cpu, StartTimeout, ct).ConfigureAwait(false);
    }

    private WhisperServerProcess Started(WhisperServerProcess server)
    {
        _server = server;
        LastBackend = (server.Backend, server.Device);
        return server;
    }

    private async Task StopCoreAsync()
    {
        _idle?.Cancel();
        _idle = null;
        if (_server is { } server)
        {
            _server = null;
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ScheduleIdleStop(int minutes)
    {
        var idle = new CancellationTokenSource();
        Interlocked.Exchange(ref _idle, idle)?.Cancel();
        var delay = IdleTimeout ?? TimeSpan.FromMinutes(minutes > 0 ? minutes : LocalSettings.DefaultIdleMinutes);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, idle.Token).ConfigureAwait(false);
                await StopAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Neue Erkennung oder Ende: der Timer ist hinfällig.
            }
            catch (ObjectDisposedException)
            {
            }
        });
    }
}
