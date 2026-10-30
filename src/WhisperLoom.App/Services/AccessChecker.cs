using System.Net;
using System.Net.Http.Headers;

namespace WhisperLoom.App.Services;

/// <summary>
/// Anbieter für die Auswahllisten der Einstellungen. IDs und Basis-URLs wie <c>ProviderCatalog.kt</c> (Android 3.9.5).
/// Übergang bis WLD-INT: dann liefert der Core-Katalog (<c>providers.json</c>, WLD-P1b) Liste und Modelle.
/// </summary>
public sealed record ProviderChoice(string Id, string Name, string BaseUrl, bool Stt, bool Llm)
{
    public static readonly IReadOnlyList<ProviderChoice> All =
    [
        new("openai", "OpenAI", "https://api.openai.com/v1", true, true),
        new("groq", "Groq", "https://api.groq.com/openai/v1", true, true),
        new("mistral", "Mistral (Voxtral)", "https://api.mistral.ai/v1", true, true),
        new("elevenlabs", "ElevenLabs (Scribe)", "https://api.elevenlabs.io/v1", true, false),
        new("together", "Together AI", "https://api.together.ai/v1", true, true),
        new("deepinfra", "DeepInfra", "https://api.deepinfra.com/v1/openai", true, true),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", true, true),
        new("anthropic", "Anthropic (Claude)", "https://api.anthropic.com/v1", false, true),
        new("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", false, true),
        new("deepseek", "DeepSeek", "https://api.deepseek.com", false, true),
        new("ollama", "Ollama (lokal)", "http://127.0.0.1:11434", false, true),
        new("ollama-cloud", "Ollama Cloud", "https://ollama.com", false, true),
        new("custom", "Eigener Server (OpenAI-kompatibel)", "", true, true),
    ];

    public static ProviderChoice? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    public override string ToString() => Name;
}

/// <summary>Ergebnis von „Zugang prüfen“ als Lokalisierungs-Schlüssel (<c>Access_*</c>) plus HTTP-Status.</summary>
public sealed record AccessResult(bool Ok, string MessageKey, int? StatusCode = null);

public interface IAccessChecker
{
    Task<AccessResult> CheckAsync(string providerId, string baseUrl, string? key, CancellationToken ct);
}

/// <summary>
/// „Zugang prüfen“: GET <c>&lt;Basis-URL&gt;/models</c> mit dem gespeicherten Key (Anthropic: <c>x-api-key</c>,
/// ElevenLabs: <c>xi-api-key</c>, sonst Bearer). Liest den Antwort-Körper nicht; nur der Status zählt.
/// </summary>
// Doku: platform.openai.com/docs/api-reference/models/list, docs.anthropic.com/en/api/models-list (abgerufen 2026-10-30).
public sealed class HttpAccessChecker(Func<HttpClient> client) : IAccessChecker
{
    public async Task<AccessResult> CheckAsync(string providerId, string baseUrl, string? key, CancellationToken ct)
    {
        var url = string.IsNullOrWhiteSpace(baseUrl) ? ProviderChoice.Find(providerId)?.BaseUrl ?? "" : baseUrl;
        if (url.Length == 0 || !Uri.TryCreate(url.TrimEnd('/') + "/models", UriKind.Absolute, out var uri))
        {
            return new AccessResult(false, "Access_NoUrl");
        }
        if (string.IsNullOrEmpty(key) && providerId is not ("ollama" or "custom"))
        {
            return new AccessResult(false, "Access_NoKey");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrEmpty(key))
        {
            switch (providerId)
            {
                case "anthropic":
                    request.Headers.Add("x-api-key", key);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    break;
                case "elevenlabs":
                    request.Headers.Add("xi-api-key", key);
                    break;
                default:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    break;
            }
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            return response.StatusCode switch
            {
                _ when response.IsSuccessStatusCode => new AccessResult(true, "Access_Ok", status),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AccessResult(false, "Access_Denied", status),
                _ => new AccessResult(false, "Access_Http", status),
            };
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new AccessResult(false, "Access_Network");
        }
    }
}

/// <summary>HttpClient nach der Proxy-Einstellung: System (Standard), aus oder manuell.</summary>
// Doku: learn.microsoft.com/dotnet/api/system.net.http.httpclienthandler.proxy (abgerufen 2026-10-30).
public static class HttpClients
{
    public static HttpClient Create(DesktopSettings desktop)
    {
        var handler = new HttpClientHandler();
        switch (desktop.ProxyMode)
        {
            case "off":
                handler.UseProxy = false;
                break;
            case "manual" when Uri.TryCreate(desktop.ProxyUrl, UriKind.Absolute, out var proxy):
                handler.Proxy = new WebProxy(proxy);
                handler.UseProxy = true;
                break;
        }
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    }
}
