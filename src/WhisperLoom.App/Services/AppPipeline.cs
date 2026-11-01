using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Prompts;
using WhisperLoom.App.Features.Rewrite;

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
    ILocalRefiner? localRefiner = null) : IDictationPipeline, ISelectionRewriter, IDisposable
{
    private HttpProxyOptions? _proxy;
    private HttpClient? _http;
    private DictationPipeline? _pipeline;

    public Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct) => Current().RunAsync(request, ct);

    public Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct) =>
        Current().RefineAsync(raw, language, refinement, ct);

    /// <summary>
    /// Markierung umschreiben (WLD-P5b): derselbe Weg wie „Neu verarbeiten“ (Zugang, Fallback-Zugang, Netz-Vorprüfung), nur
    /// mit dem Prompt „Umschreiben nach Anweisung“ als Stufe „Prompt“ (die Regeln danach ändern den Text nicht). Ohne lokales
    /// Textmodell: dessen Auftrag ist fest die Stufe, nicht das Umschreiben — bei lokaler Erkennung gibt es kein Ergebnis.
    /// </summary>
    public async Task<RefinedText> RewriteAsync(string selection, string instruction, string language, CancellationToken ct)
    {
        Current();
        var options = CoreText.Options(cache);
        var pipeline = new DictationPipeline(settings, secrets, _http!, new RewritePrompt(instruction), CoreText.Polisher,
            new DictationPipelineOptions { ModelCache = options.ModelCache });
        var refined = await pipeline.RefineAsync(selection, language, new Refinement(RefineMode.Prompt), ct).ConfigureAwait(false);
        return refined.Model is null ? refined : refined with { Text = RewritePrompt.CleanOutput(refined.Text) };
    }

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
