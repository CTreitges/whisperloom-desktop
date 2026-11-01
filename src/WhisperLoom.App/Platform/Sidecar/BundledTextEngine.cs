using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Mitgeliefertes Textmodell (<c>local.textEngine = "bundled"</c>, Rolle von Gemma/LiteRT auf Android): <c>llama-server</c>
/// startet bei Bedarf bzw. schon während der Aufnahme (<see cref="WarmUpAsync"/>), Vulkan und bei Fehler CPU, endet nach
/// Leerlauf, mit dem Modellwechsel und mit der App. Prompts und Nacharbeit wie online (<see cref="TextRefiner"/>), nur der
/// Transport ist lokal. Ordner: <c>&lt;lokal&gt;/text-models</c> und <c>&lt;lokal&gt;/llama-server</c>.
/// </summary>
public sealed class BundledTextEngine : ILocalRefiner, IAsyncDisposable
{
    public static string MsgModelMissing(TextModel m) => $"Lokales Textmodell {m.Label} fehlt — unter „Lokale Modelle“ laden";

    public const string MsgStartFailed = "Lokales Textmodell startet nicht";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(180);

    private readonly Func<WhisperLoomSettings> _settings;
    private readonly IRefinePromptBuilder _prompts;
    private readonly TextCleanup _cleanup;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<long> _deviceRam;
    private LlamaServerProcess? _server;
    private CancellationTokenSource? _idle;
    private bool _vulkanFailed;

    /// <param name="testModel">Nur Tests und Smokes (<c>--test-text-model</c>): GGUF-Datei statt Katalog-Modell.</param>
    /// <param name="deviceRam">Gesamter Arbeitsspeicher; Standard <see cref="TextModelCatalog.DeviceRamBytes"/>.</param>
    public BundledTextEngine(string localDir, Func<WhisperLoomSettings> settings, IRefinePromptBuilder prompts, TextCleanup cleanup,
        HttpClient? downloadHttp = null, string? testModel = null, Func<long>? deviceRam = null)
    {
        _settings = settings;
        _prompts = prompts;
        _cleanup = cleanup;
        _http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _deviceRam = deviceRam ?? TextModelCatalog.DeviceRamBytes;
        TestModel = testModel;
        Models = new ModelStore(Path.Combine(localDir, "text-models"));
        Release = new LlamaRelease(downloadHttp ?? _http, Path.Combine(localDir, "llama-server"));
    }

    public ModelStore Models { get; }

    public LlamaRelease Release { get; }

    public string? TestModel { get; }

    /// <summary>Leerlauf bis zum Ende des Begleitprozesses; null = aus den Einstellungen (Minuten).</summary>
    public TimeSpan? IdleTimeout { get; init; }

    /// <summary>Der laufende Begleitprozess (null = keiner).</summary>
    public LlamaServerProcess? Server => _server;

    /// <summary>Das gewählte Katalog-Modell (<c>local.bundledModel</c>, "" = Empfehlung).</summary>
    public TextModel Selected => TextModelCatalog.ById(LocalSettings.Of(_settings()).BundledModel);

    public string ModelLabel => TestModel is not null ? Path.GetFileNameWithoutExtension(TestModel) : Selected.Id;

    /// <summary>Pfad der Modelldatei, oder warum es nicht geht (Datei fehlt, zu wenig Arbeitsspeicher).</summary>
    public (string Path, string? Error) Model()
    {
        if (TestModel is not null)
        {
            return (TestModel, WhisperModels.CheckFile(TestModel) == WhisperModels.FileCheck.Ok ? null : MsgStartFailed);
        }
        var model = Selected;
        if (!TextModelCatalog.Fits(model, _deviceRam())) return ("", TextModelCatalog.MsgTooLittleRam(model));
        return Models.IsInstalled(model.File) ? (Models.File(model.File), null) : ("", MsgModelMissing(model));
    }

    public Task<bool> IsReadyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Model().Error is null);
    }

    public async Task<string> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || refinement.Mode == RefineMode.Off) return raw;
        var server = await EnsureServerAsync(ct).ConfigureAwait(false);
        try
        {
            var access = LocalServerRefiner.Access(new LocalServer { Name = ModelLabel, Kind = "text", BaseUrl = server.BaseUrl, Model = ModelLabel }, ModelLabel);
            return await LocalServerRefiner.RefineAtAsync(_http, access, raw, language, refinement, _prompts, _cleanup, ct).ConfigureAwait(false);
        }
        finally
        {
            ScheduleIdleStop(LocalSettings.Of(_settings()).IdleMinutes);
        }
    }

    /// <summary>Vorladen während der Aufnahme: startet den Begleitprozess, Fehler zeigen sich erst beim Verbessern.</summary>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        try
        {
            await EnsureServerAsync(ct).ConfigureAwait(false);
            ScheduleIdleStop(LocalSettings.Of(_settings()).IdleMinutes);
        }
        catch (Exception e) when (e is LocalNotAvailableException or OperationCanceledException)
        {
            // Beim Verbessern kommt die Meldung (Rohtext + Hinweis).
        }
    }

    /// <exception cref="LocalNotAvailableException">Modell fehlt, zu wenig RAM, oder weder Vulkan noch CPU starten.</exception>
    public async Task<LlamaServerProcess> EnsureServerAsync(CancellationToken ct, DownloadProgress? progress = null)
    {
        var (path, error) = Model();
        if (error is not null) throw new LocalNotAvailableException(error);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _idle?.Cancel();
            if (_server is { HasExited: false } running && running.Model == path) return running;
            await StopCoreAsync().ConfigureAwait(false);
            if (LocalSettings.Of(_settings()).Backend != "cpu" && !_vulkanFailed)
            {
                try
                {
                    return _server = await StartAsync(SidecarVariant.Vulkan, path, progress, ct).ConfigureAwait(false);
                }
                catch (Exception e) when (IsStartFailure(e) && !ct.IsCancellationRequested)
                {
                    _vulkanFailed = true;
                }
            }
            try
            {
                return _server = await StartAsync(SidecarVariant.Cpu, path, progress, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (IsStartFailure(e) && !ct.IsCancellationRequested)
            {
                throw new LocalNotAvailableException($"{MsgStartFailed}: {e.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

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

    private static bool IsStartFailure(Exception e) =>
        e is SidecarStartException or DownloadException or IOException or UnauthorizedAccessException or HttpRequestException;

    private async Task<LlamaServerProcess> StartAsync(SidecarVariant variant, string model, DownloadProgress? progress, CancellationToken ct)
    {
        var exe = await Release.EnsureAsync(variant, progress, ct).ConfigureAwait(false);
        return await LlamaServerProcess.StartAsync(exe, model, _http, gpu: variant == SidecarVariant.Vulkan, StartTimeout, ct).ConfigureAwait(false);
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
            }
            catch (ObjectDisposedException)
            {
            }
        });
    }
}
