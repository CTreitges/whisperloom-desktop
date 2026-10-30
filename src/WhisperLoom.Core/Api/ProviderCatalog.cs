using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Anbieter- und Modell-Katalog (Port von <c>api/ProviderCatalog.kt</c>, Stand siehe <see cref="CatalogDate"/>). Die Daten
/// liegen als eingebettete <c>providers.json</c>; <c>tools/check_provider_parity.py</c> vergleicht sie mit Kotlin.
/// Reihenfolge = Reihenfolge im Dropdown; das erste Modell je Liste ist der Standard (beim Textmodell die Empfehlung zum
/// Glätten, <see cref="ProviderInfo.RewriteLlmModel"/> die zum Umformulieren).
/// </summary>
public static class ProviderCatalog
{
    public const string CustomId = "custom";
    public const string OpenAiId = "openai";
    public const string OllamaId = "ollama";
    public const string OllamaCloudId = "ollama-cloud";
    public const string ElevenLabsId = "elevenlabs";

    private const string Resource = "WhisperLoom.Core.Api.providers.json";

    private static readonly (string Date, IReadOnlyList<ProviderInfo> Providers) Data = Load();

    public static string CatalogDate => Data.Date;

    public static IReadOnlyList<ProviderInfo> Providers => Data.Providers;

    public static ProviderInfo Custom { get; } = Providers.First(p => p.Id == CustomId);

    public static ProviderInfo OpenAi { get; } = Providers.First(p => p.Id == OpenAiId);

    /// <summary>Anbieter für die Auswahl der Erkennung.</summary>
    public static IReadOnlyList<ProviderInfo> SttProviders { get; } = [.. Providers.Where(p => p.HasStt)];

    /// <summary>Anbieter für die Auswahl der Textverbesserung.</summary>
    public static IReadOnlyList<ProviderInfo> LlmProviders { get; } = [.. Providers.Where(p => p.HasLlm)];

    public static ProviderInfo? Find(string id) => Providers.FirstOrDefault(p => p.Id == id);

    /// <summary>Unbekannte oder leere ID → OpenAI (wie Android: Bestandsnutzer ohne gespeicherten Anbieter).</summary>
    public static ProviderInfo ById(string id) => Find(id) ?? OpenAi;

    private static (string, IReadOnlyList<ProviderInfo>) Load()
    {
        using var stream = typeof(ProviderCatalog).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Ressource {Resource} fehlt");
        var root = JsonNode.Parse(stream)!.AsObject();
        var providers = root["providers"]!.AsArray().Select(n => Provider(n!.AsObject())).ToList();
        return ((string)root["catalogDate"]!, providers);
    }

    // Felder wie die Parameter von Provider/ModelOption in Kotlin; fehlend = Kotlin-Standardwert.
    private static ProviderInfo Provider(JsonObject o) => new(
        Id: (string)o["id"]!,
        Name: (string)o["name"]!,
        BaseUrl: (string)o["baseUrl"]!,
        NeedsKey: (bool?)o["needsKey"] ?? true,
        KeyUrl: (string?)o["keyUrl"] ?? "",
        AllowsHttp: (bool?)o["allowsHttp"] ?? false,
        DefaultReadTimeoutSec: (int?)o["defaultReadTimeoutSec"] ?? 90,
        SttModels: Models(o["sttModels"]),
        LlmModels: Models(o["llmModels"]),
        SttSendsPrompt: (bool?)o["sttSendsPrompt"] ?? true,
        SttSendsResponseFormat: (bool?)o["sttSendsResponseFormat"] ?? true,
        SttPathOverride: (string?)o["sttPathOverride"],
        SttMaxBytes: (int?)o["sttMaxBytes"],
        Api: Style((string?)o["api"] ?? "OPENAI"),
        Notes: (string?)o["notes"] ?? "",
        RewriteLlmModel: (string?)o["rewriteLlmModel"] ?? "");

    private static List<ModelOption> Models(JsonNode? node) =>
        node?.AsArray().Select(n => n!.AsObject()).Select(m => new ModelOption(
            Id: (string)m["id"]!,
            Label: (string)m["label"]!,
            Note: (string?)m["note"] ?? "",
            TemperatureSupported: (bool?)m["temperatureSupported"] ?? true,
            ReasoningEffort: (string?)m["reasoningEffort"],
            LanguageField: (string?)m["languageField"] ?? "language",
            ThinkingType: (string?)m["thinkingType"])).ToList() ?? [];

    private static ApiStyle Style(string name) => name switch
    {
        "OPENAI" => ApiStyle.OpenAi,
        "OLLAMA" => ApiStyle.Ollama,
        "ELEVENLABS" => ApiStyle.ElevenLabs,
        _ => throw new InvalidOperationException($"Unbekanntes Protokoll {name} in providers.json"),
    };
}
