using WhisperLoom.App.Cli;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Die lokalen Teile einer App-Instanz: Erkennung über den Begleitprozess und Textverbesserung über Ollama, dazu die
/// HTTP-Clients (Downloads mit Proxy der Einstellungen, Ollama ohne Proxy auf 127.0.0.1).
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
        Refiner = new OllamaLocalRefiner(_ollamaHttp, () => LocalSettings.Of(settings()).OllamaModel, CoreText.Prompts, TextCleanup.For(CoreText.Polisher),
            () => LocalSettings.Of(settings()).OllamaUrl);
    }

    public LocalStt Stt { get; }

    public OllamaLocalRefiner Refiner { get; }

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
        _download.Dispose();
        _ollamaHttp.Dispose();
    }
}
