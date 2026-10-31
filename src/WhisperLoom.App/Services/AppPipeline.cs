using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Services;

/// <summary>
/// Die echte <see cref="DictationPipeline"/> der App mit den Text-Bausteinen aus <see cref="CoreText"/>. Der HttpClient folgt
/// der Proxy-Einstellung: ändert sie sich, gilt ab dem nächsten Diktat ein neuer Client. Ein Diktat zur Zeit (Controller).
/// </summary>
public sealed class AppPipeline(Func<WhisperLoomSettings> settings, ISecretStore secrets, ModelCache cache) : IDictationPipeline, IDisposable
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
            _pipeline = new DictationPipeline(settings, secrets, _http, CoreText.Prompts, CoreText.Polisher, CoreText.Options(cache));
        }
        return _pipeline;
    }
}
