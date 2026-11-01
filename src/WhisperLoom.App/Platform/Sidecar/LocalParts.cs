using WhisperLoom.App.Cli;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Die lokalen Teile einer App-Instanz: Erkennung über den Begleitprozess und Textverbesserung nach <c>local.textEngine</c>
/// (Ollama, mitgeliefertes Modell, lokaler Server; <see cref="Text"/>), dazu die HTTP-Clients (Downloads mit Proxy der
/// Einstellungen, Ollama und lokale Server ohne Proxy).
/// </summary>
public sealed class LocalParts : IAsyncDisposable
{
    private readonly HttpClient _download;
    private readonly HttpClient _ollamaHttp;

    /// <param name="desktop">Proxy für Downloads (beim Start gelesen; <paramref name="settings"/> gilt erst nach dem Aufbau).</param>
    internal LocalParts(AppOptions options, Func<WhisperLoomSettings> settings, DesktopSettings desktop)
    {
        _download = DownloadClient(desktop);
        _ollamaHttp = Http.CreateClient(new HttpProxyOptions(ProxyMode.None));
        Stt = new LocalStt(Bench.LocalDir(options), settings, _download, CoreText.VocabularyPrompt);
        var cleanup = TextCleanup.For(CoreText.Polisher);
        Refiner = new OllamaLocalRefiner(_ollamaHttp, () => LocalSettings.Of(settings()).OllamaModel, CoreText.Prompts, cleanup,
            () => LocalSettings.Of(settings()).OllamaUrl);
        Bundled = new BundledTextEngine(Bench.LocalDir(options), settings, CoreText.Prompts, cleanup, _download, options.TestTextModel);
        Server = new LocalServerRefiner(_ollamaHttp, () => LocalTextEngine.TextServer(LocalSettings.Of(settings())), CoreText.Prompts, cleanup);
        Text = new LocalTextEngine(Refiner, Bundled, Server, settings);
    }

    public LocalStt Stt { get; }

    /// <summary>Ollama (<c>local.textEngine = "ollama"</c>).</summary>
    public OllamaLocalRefiner Refiner { get; }

    /// <summary>Mitgeliefertes Textmodell (<c>"bundled"</c>).</summary>
    public BundledTextEngine Bundled { get; }

    /// <summary>Lokaler OpenAI-kompatibler Text-Server (<c>"server"</c>).</summary>
    public LocalServerRefiner Server { get; }

    /// <summary>Die lokale Textverbesserung nach <c>local.textEngine</c> (für die Pipeline).</summary>
    public LocalTextEngine Text { get; }

    /// <summary>Client für Downloads (Hugging Face, GitHub-Release): folgt Weiterleitungen, Proxy der Einstellungen.</summary>
    public HttpClient Download => _download;

    /// <summary>Client ohne Proxy für Ollama.</summary>
    public HttpClient OllamaHttp => _ollamaHttp;

    /// <summary>Adresse von Ollama aus den Einstellungen (leer = Standard).</summary>
    public static string OllamaUrl(WhisperLoomSettings settings) =>
        LocalSettings.Of(settings).OllamaUrl is { Length: > 0 } url ? url : OllamaLocalRefiner.DefaultBaseUrl;

    /// <summary>Wie <see cref="HttpClients.Create"/>, aber mit Weiterleitungen (302 → CDN) und ohne Gesamt-Zeitlimit.</summary>
    public static HttpClient DownloadClient(DesktopSettings desktop)
    {
        var handler = (SocketsHttpHandler)Http.CreateHandler(HttpClients.Proxy(desktop));
        handler.AllowAutoRedirect = true;
        return ModelDownloader.CreateClient(handler);
    }

    public async ValueTask DisposeAsync()
    {
        await Stt.DisposeAsync().ConfigureAwait(false);
        await Bundled.DisposeAsync().ConfigureAwait(false);
        _download.Dispose();
        _ollamaHttp.Dispose();
    }
}
