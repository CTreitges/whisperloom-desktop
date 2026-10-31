using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Services;

/// <summary>
/// Die echte <see cref="DictationPipeline"/> der App mit den Text-Bausteinen aus <see cref="CoreText"/>. Der HttpClient folgt
/// der Proxy-Einstellung: ändert sie sich, gilt ab dem nächsten Diktat ein neuer Client. Ein Diktat zur Zeit (Controller).
/// </summary>
/// <param name="localStt">Lokale Erkennung (Begleitprozess, WLD-P4b); null = keine.</param>
/// <param name="localRefiner">Lokale Textverbesserung (Ollama, WLD-P4b); null = keine.</param>
public sealed class AppPipeline(
    Func<WhisperLoomSettings> settings,
    ISecretStore secrets,
    ModelCache cache,
    ILocalStt? localStt = null,
    ILocalRefiner? localRefiner = null) : IDictationPipeline, IDisposable
{
    private HttpProxyOptions? _proxy;
    private HttpClient? _http;
    private DictationPipeline? _pipeline;

    public Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct) => Current().RunAsync(request, ct);

    public Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct) =>
        Current().RefineAsync(raw, language, refinement, ct);

    public void Dispose() => _http?.Dispose();

    private DictationPipeline Current()
    {
        var proxy = HttpClients.Proxy(SettingsStore.Desktop(settings()));
        if (_pipeline is null || proxy != _proxy)
        {
            _http?.Dispose();
            _http = Http.CreateClient(proxy);
            _proxy = proxy;
            var options = CoreText.Options(cache);
            _pipeline = new DictationPipeline(settings, secrets, _http, CoreText.Prompts, CoreText.Polisher, new DictationPipelineOptions
            {
                ModelCache = options.ModelCache,
                TrimSilence = options.TrimSilence,
                VocabularyPrompt = options.VocabularyPrompt,
                LocalStt = localStt,
                LocalRefiner = localRefiner,
            });
        }
        return _pipeline;
    }
}
