using System.Net;
using System.Net.Http.Headers;
using WhisperLoom.Core.Api;

namespace WhisperLoom.App.Services;

/// <summary>Anbieter für die Auswahllisten der Einstellungen, aus dem Core-Katalog (<c>providers.json</c>).</summary>
public sealed record ProviderChoice(string Id, string Name, string BaseUrl, bool Stt, bool Llm)
{
    public static readonly IReadOnlyList<ProviderChoice> All =
        [.. ProviderCatalog.Providers.Select(p => new ProviderChoice(p.Id, p.Name, p.BaseUrl, p.HasStt, p.HasLlm))];

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
/// „Zugang prüfen“: GET <c>&lt;Basis-URL&gt;/models</c> (Ollama: <c>/api/tags</c>) mit dem gespeicherten Key (Anthropic:
/// <c>x-api-key</c>, ElevenLabs: <c>xi-api-key</c>, sonst Bearer). Liest den Antwort-Körper nicht; nur der Status zählt.
/// W2: Einträge (<c>custom:&lt;id&gt;</c>, <c>local:&lt;name&gt;</c>) gehen immer an ihre eigene Adresse, nie an die übergebene.
/// </summary>
// Doku: platform.openai.com/docs/api-reference/models/list, docs.anthropic.com/en/api/models-list (abgerufen 2026-10-30).
/// <param name="client">Liefert den Client je Prüfung; geteilt, außer <paramref name="ownsClient"/>.</param>
/// <param name="ownsClient">Jeder gelieferte Client gehört der Prüfung und wird danach freigegeben (WLD-INT-17).</param>
public sealed class HttpAccessChecker(Func<HttpClient> client, Func<ProviderDirectory>? providers = null, bool ownsClient = false) : IAccessChecker
{
    /// <summary>Für eine Fabrik, die je Prüfung einen neuen Client baut (Einstellungen: Proxy kann wechseln).</summary>
    public static HttpAccessChecker PerCheck(Func<HttpClient> create, Func<ProviderDirectory>? providers = null) =>
        new(create, providers, ownsClient: true);

    public async Task<AccessResult> CheckAsync(string providerId, string baseUrl, string? key, CancellationToken ct)
    {
        var provider = (providers?.Invoke() ?? ProviderDirectory.Catalog).Find(providerId);
        var url = provider is { IsEntry: true } ? provider.BaseUrl
            : string.IsNullOrWhiteSpace(baseUrl) ? provider?.BaseUrl ?? ""
            : baseUrl;
        var rules = provider?.Rules ?? providerId;
        var path = rules == ProviderCatalog.OllamaId ? "/api/tags" : "/models";
        if (url.Length == 0 || !Uri.TryCreate(url.TrimEnd('/') + path, UriKind.Absolute, out var uri))
        {
            return new AccessResult(false, "Access_NoUrl");
        }
        if (string.IsNullOrEmpty(key) && (provider?.NeedsKey ?? rules is not ("ollama" or "custom")))
        {
            return new AccessResult(false, "Access_NoKey");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrEmpty(key))
        {
            switch (rules)
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
            var http = client();
            using var owned = ownsClient ? http : null;
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
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

/// <summary>HttpClient nach der Proxy-Einstellung: System (Standard), aus oder manuell — über den Core-Handler (<see cref="Http"/>).</summary>
public static class HttpClients
{
    /// <summary>Eine manuelle Adresse, die keine absolute URL ist, gilt als System-Proxy.</summary>
    public static HttpProxyOptions Proxy(DesktopSettings desktop) => desktop.ProxyMode switch
    {
        "off" => new HttpProxyOptions(ProxyMode.None),
        "manual" when Uri.TryCreate(desktop.ProxyUrl, UriKind.Absolute, out _) => new HttpProxyOptions(ProxyMode.Manual, desktop.ProxyUrl),
        _ => new HttpProxyOptions(ProxyMode.System),
    };

    public static HttpClient Create(DesktopSettings desktop) => Http.CreateClient(Proxy(desktop));
}
