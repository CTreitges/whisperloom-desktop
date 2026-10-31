using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Local;

/// <summary>
/// Lokale Textverbesserung über Ollama auf 127.0.0.1:11434 (Rolle von <c>llm/LocalRefiner</c>; Goal: Ollama statt LiteRT).
/// Derselbe Auftrag wie online (<see cref="TextRefiner"/> mit Ollama-Zugang). Das Modell kommt aus den Einstellungen und
/// gilt je Aufruf frisch. Keine Installation, kein <c>ollama pull</c>: fehlt Ollama oder das Modell, ist der Verbesserer
/// nicht bereit.
/// </summary>
// Doku: https://github.com/ollama/ollama/blob/main/docs/api.md (abgerufen 2026-10-31)
public sealed class OllamaLocalRefiner(
    HttpClient http,
    Func<string> model,
    IRefinePromptBuilder prompts,
    TextCleanup cleanup,
    string baseUrl = OllamaLocalRefiner.DefaultBaseUrl) : ILocalRefiner
{
    public const string DefaultBaseUrl = "http://127.0.0.1:11434";

    /// <summary>Hinweis mit Link, wenn Ollama fehlt (keine Auto-Installation).</summary>
    public const string DownloadUrl = "https://ollama.com/download";

    public const string MsgNoModel = "Kein Ollama-Modell gewählt — unter „Lokal“ ein Modell wählen";
    public const string MsgUnreachable = "Ollama nicht erreichbar (127.0.0.1:11434) — Ollama installieren und starten: " + DownloadUrl;
    public const string MsgModelMissing = "Ollama-Modell nicht installiert";

    private const int TagsTimeoutMs = 3_000;

    public string ModelLabel => model().Trim();

    /// <summary>Ollama antwortet und das gewählte Modell ist installiert. Wirft nur bei Abbruch.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        var name = model().Trim();
        if (name.Length == 0) return false;
        var models = await ListModelsAsync(http, baseUrl, ct).ConfigureAwait(false);
        return models is not null && Contains(models, name);
    }

    public async Task<string> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || refinement.Mode == RefineMode.Off) return raw;
        var name = model().Trim();
        if (name.Length == 0) throw new LocalNotAvailableException(MsgNoModel);
        var access = AccessResolver.ResolveLlm(AccessResolver.ResolveStt("openai", "", "", ""), "ollama", baseUrl, "", name);
        try
        {
            return await new TextRefiner(access, http, prompts, cleanup).RefineAsync(raw, language, refinement, ct).ConfigureAwait(false);
        }
        catch (ApiNetworkException) when (!ct.IsCancellationRequested)
        {
            throw new LocalNotAvailableException(MsgUnreachable);
        }
        catch (ApiHttpException e) when (e.StatusCode == 404)
        {
            throw new LocalNotAvailableException($"{MsgModelMissing}: {name}");
        }
    }

    /// <summary>Modelle aus <c>/api/tags</c>; null = Ollama nicht erreichbar oder Fehlerstatus. Wirft nur bei Abbruch.</summary>
    public static async Task<IReadOnlyList<string>?> ListModelsAsync(HttpClient http, string baseUrl = DefaultBaseUrl, CancellationToken ct = default)
    {
        try
        {
            var options = new HttpCallOptions(ReadTimeoutMs: TagsTimeoutMs, ConnectTimeoutMs: TagsTimeoutMs, Style: ApiStyle.Ollama);
            var body = await Http.GetAsync(http, OllamaApi.TagsUrl(baseUrl), "", options, ct).ConfigureAwait(false);
            return OllamaApi.ParseTags(body);
        }
        catch (Exception e) when (e is ApiNetworkException or ApiHttpException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>„gemma3“ meint „gemma3:latest“ (so zeigt Ollama ein Modell ohne Tag).</summary>
    public static bool Contains(IReadOnlyList<string> models, string name) =>
        models.Any(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(m, name + ":latest", StringComparison.OrdinalIgnoreCase));
}
