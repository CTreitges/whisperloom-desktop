using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>Wofür eine Modell-Liste gebraucht wird.</summary>
public enum ModelKind
{
    Stt,
    Llm,
}

/// <summary>Ein Modell aus der Liste des Anbieters (Port von <c>RemoteModel</c>); Flags nur, wenn der Server sie verrät.</summary>
public sealed record RemoteModel(
    string Id,
    string? Label = null,
    string? Note = null,
    bool? TemperatureSupported = null,
    string? ReasoningEffort = null);

/// <summary>GET-Anfrage für eine Modell-Liste; der Key kommt aus dem Zugang.</summary>
public sealed record ModelListRequest(string Url, string? AuthHeader = null, IReadOnlyDictionary<string, string>? Headers = null)
{
    /// <summary>Eigene Header nie auf einen anderen Host weiterleiten — dann keiner Weiterleitung folgen.</summary>
    public bool FollowRedirects => AuthHeader is null && (Headers is null || Headers.Count == 0);
}

/// <summary>Findet ein zwischengespeichertes Server-Modell (<see cref="ModelCache"/>), damit <see cref="AccessResolver"/> rein bleibt.</summary>
public interface IServerModelLookup
{
    RemoteModel? Find(string providerId, ModelKind kind, string baseUrl, string id);

    /// <summary>Das Textmodell hat <c>temperature</c> schon einmal abgelehnt.</summary>
    bool RejectsTemperature(string providerId, string baseUrl, string id) => false;
}

public static class ServerModelLookup
{
    public static readonly IServerModelLookup None = new NoLookup();

    private sealed class NoLookup : IServerModelLookup
    {
        public RemoteModel? Find(string providerId, ModelKind kind, string baseUrl, string id) => null;
    }
}

/// <summary>
/// „Modelle vom Server“ (Port von <c>api/ModelLists.kt</c>): Adresse und Header je Anbieter, welche Einträge für
/// Erkennung bzw. Textverbesserung taugen (<see cref="ModelListFilters"/>) und Flags für Modelle ohne Katalog-Treffer.
/// </summary>
public static partial class ModelLists
{
    /// <summary>Eine Liste ist klein — wer in 15 s nicht antwortet, antwortet gar nicht.</summary>
    public const int TimeoutMs = 15_000;
    public const string AnthropicVersion = "2023-06-01";
    public const string HintElevenLabsModels = "Key oder Berechtigung „Models: Lesen“ prüfen (nur für die Modell-Liste nötig)";

    private const string Models = "/models";

    // Heuristik für Flags (OptionFor), angewandt auf den Namen ohne Anbieter-Präfix.
    [GeneratedRegex("^(ft:)?(o\\d|gpt-5|gpt-6)", RegexOptions.IgnoreCase)]
    private static partial Regex OpenAiReasoning();

    [GeneratedRegex("^gemini-3", RegexOptions.IgnoreCase)]
    private static partial Regex Gemini3();

    [GeneratedRegex("^gpt-.*transcribe", RegexOptions.IgnoreCase)]
    private static partial Regex GptTranscribe();

    [GeneratedRegex("^qwen3", RegexOptions.IgnoreCase)]
    private static partial Regex Qwen3();

    [GeneratedRegex("^gpt-oss", RegexOptions.IgnoreCase)]
    private static partial Regex GptOss();

    public static string Key(this ModelKind kind) => kind == ModelKind.Stt ? "stt" : "llm";

    /// <summary>Adresse und Header der Liste; Ollama nutzt die native Schnittstelle.</summary>
    public static ModelListRequest Request(ApiAccess access, ModelKind kind)
    {
        var provider = access.Provider;
        var b = access.BaseUrl;
        if (provider.IsOllama) return new ModelListRequest(OllamaApi.TagsUrl(b));
        if (provider.Api == ApiStyle.ElevenLabs) return new ModelListRequest(Http.Endpoint(b, Models), ElevenLabsStt.AuthHeader);
        // Ohne limit liefert Anthropic 20 Modelle je Seite; 1000 ist das Maximum.
        if (provider.Id == "anthropic")
        {
            return new ModelListRequest(Http.Endpoint(b, $"{Models}?limit=1000"), Headers: new Dictionary<string, string> { ["anthropic-version"] = AnthropicVersion });
        }
        // Ohne Filter liefert OpenRouter nur Modelle mit Text-Ausgabe.
        if (provider.Id == "openrouter" && kind == ModelKind.Stt) return new ModelListRequest(Http.Endpoint(b, $"{Models}?output_modalities=transcription"));
        return new ModelListRequest(Http.Endpoint(b, Models));
    }

    /// <summary>Holt die Liste und filtert sie.</summary>
    /// <exception cref="ApiNotConfiguredException">Keine Adresse eingetragen.</exception>
    /// <exception cref="ModelListException">Die Antwort ist keine Modell-Liste.</exception>
    public static async Task<IReadOnlyList<RemoteModel>> LoadAsync(HttpClient http, ApiAccess access, ModelKind kind, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(access.BaseUrl)) throw new ApiNotConfiguredException();
        var r = Request(access, kind);
        var style = access.Provider.Api;
        // ElevenLabs: die Liste braucht „Models: Lesen“ — nicht „Speech to Text“ wie beim Diktat.
        var hints = style == ApiStyle.ElevenLabs
            ? new Dictionary<int, string> { [401] = HintElevenLabsModels, [403] = HintElevenLabsModels }
            : null;
        var body = await Http.GetAsync(http, r.Url, access.ApiKey,
            new HttpCallOptions(TimeoutMs, r.FollowRedirects, r.AuthHeader, r.Headers, TimeoutMs, style, hints), ct).ConfigureAwait(false);
        return Parse(access.Provider, kind, body);
    }

    /// <summary>
    /// Die Modelle der Antwort, die für <paramref name="kind"/> taugen: ohne Duplikate, alphabetisch (Anthropic in
    /// Server-Reihenfolge). Leer ist kein Fehler.
    /// </summary>
    /// <param name="today">Für Abschalt-Daten; Standard: heute (UTC).</param>
    /// <exception cref="ModelListException">Kaputtes JSON oder eine Antwort ohne Liste.</exception>
    public static IReadOnlyList<RemoteModel> Parse(ProviderInfo provider, ModelKind kind, string body, DateOnly? today = null)
    {
        if (kind == ModelKind.Stt ? !provider.HasStt : !provider.HasLlm) return [];
        List<RemoteModel> models;
        try
        {
            models = ModelListFilters.Select(provider, kind == ModelKind.Stt, body, today ?? DateOnly.FromDateTime(DateTime.UtcNow));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new ModelListException(e);
        }
        var unique = models.DistinctBy(m => m.Id).ToList();
        return provider.Id == "anthropic" ? unique : [.. unique.OrderBy(m => m.Id.ToLowerInvariant(), StringComparer.Ordinal)];
    }

    /// <summary>
    /// Flags für eine Modell-ID ohne exakten Katalog-Treffer: Server-Metadaten → Katalog-Modell, dessen Snapshot die ID
    /// ist → Heuristik je Familie. Weiß niemand etwas, bleibt es bei null (Standards).
    /// </summary>
    public static ModelOption? OptionFor(ProviderInfo provider, ModelKind kind, string id, RemoteModel? server)
    {
        var catalog = (kind == ModelKind.Stt ? provider.SttModels : provider.LlmModels) ?? [];
        var b = CatalogBase(catalog, id);
        if (b is not null && b.Id == id) return b;
        if (string.IsNullOrWhiteSpace(id)) return null;
        var name = id[(id.LastIndexOf('/') + 1)..];
        var chat = kind == ModelKind.Llm;
        var temperature = server?.TemperatureSupported ?? b?.TemperatureSupported
            ?? (chat && NoTemperature(provider, name) ? false : null);
        var effort = server?.ReasoningEffort ?? b?.ReasoningEffort ?? (chat ? HeuristicEffort(provider, name) : null);
        var languageField = b?.LanguageField
            ?? (!chat && provider.Id == ProviderCatalog.OpenAiId && GptTranscribe().IsMatch(name) ? "languages[]" : null);
        if (server is null && b is null && temperature is null && effort is null && languageField is null) return null;
        return new ModelOption(id, server?.Label ?? id, server?.Note ?? "", temperature ?? true, effort, languageField ?? "language", b?.ThinkingType);
    }

    /// <summary>Das Katalog-Modell zu <paramref name="id"/>: dieselbe ID, sonst das längste, dessen datierter Snapshot sie ist.</summary>
    public static ModelOption? CatalogBase(IReadOnlyList<ModelOption> catalog, string id) =>
        catalog.FirstOrDefault(m => m.Id == id) ??
        catalog.Where(m => id.StartsWith(m.Id + "-", StringComparison.Ordinal)).MaxBy(m => m.Id.Length);

    /// <summary>OpenAI-Reasoning-Modelle lehnen temperature ab; Google rät bei Gemini 3 von temperature &lt; 1 ab.</summary>
    private static bool NoTemperature(ProviderInfo provider, string name) =>
        OpenAiReasoning().IsMatch(name) || (provider.Id == "gemini" && Gemini3().IsMatch(name));

    private static string? HeuristicEffort(ProviderInfo provider, string name) => provider.Id switch
    {
        "groq" when Qwen3().IsMatch(name) => "none",
        "groq" when GptOss().IsMatch(name) => "low",
        ProviderCatalog.OpenAiId when OpenAiReasoning().IsMatch(name) => "low",
        _ => null,
    };

    /// <summary>Datum aus "2027-02-26", "2027-02-26T00:00:00Z" oder Unix-Sekunden; alles andere = keins.</summary>
    internal static DateOnly? Date(JsonNode? value)
    {
        if (value is not JsonValue v) return null;
        if (v.GetValueKind() == JsonValueKind.Number &&
            double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime);
        }
        if (!v.TryGetValue<string>(out var s)) return null;
        var text = s.Trim();
        return DateOnly.TryParseExact(text[..Math.Min(10, text.Length)], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
