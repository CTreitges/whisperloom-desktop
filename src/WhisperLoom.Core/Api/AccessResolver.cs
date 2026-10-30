using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Leitet aus den Einstellungswerten den Zugang ab (Port von <c>AccessResolver</c>, <c>api/ApiAccess.kt</c>). Hier
/// entscheidet sich, gegen welchen Server mit welchem Key gesprochen wird — ein Key geht nie an einen fremden Anbieter.
/// </summary>
public static class AccessResolver
{
    /// <summary>Wert von <c>llm.providerId</c>, der den Erkennungs-Zugang wiederverwendet.</summary>
    public const string LlmSame = "same";

    /// <param name="readTimeoutSec">0 = Anbieter-Standard (90 s, eigener Server 600 s).</param>
    /// <param name="serverModels">Geladene Modell-Listen (<see cref="ModelCache"/>) — Flags für Server-Modelle.</param>
    public static ApiAccess ResolveStt(
        string providerId,
        string baseUrl,
        string apiKey,
        string model,
        int readTimeoutSec = 0,
        IServerModelLookup? serverModels = null)
    {
        var provider = ProviderCatalog.ById(providerId);
        var modelId = IfBlank(model.Trim(), provider.DefaultSttModel);
        var url = IfBlank(baseUrl.Trim(), provider.BaseUrl);
        return new ApiAccess(url, apiKey.Trim(), modelId, TimeoutMs(readTimeoutSec, provider), provider,
            Option(provider, ModelKind.Stt, url, modelId, serverModels ?? ServerModelLookup.None));
    }

    /// <summary>
    /// Textverbesserung: <see cref="LlmSame"/> (oder leer) übernimmt den Erkennungs-Zugang. Derselbe Anbieter explizit
    /// gewählt füllt leere Felder aus der Erkennung; ein anderer Anbieter bekommt seine eigenen Standards.
    /// </summary>
    /// <param name="sttOffline">Erkennung läuft lokal: „wie Erkennung“ hat nichts zu übernehmen (<see cref="RefineBlock.Offline"/>).</param>
    /// <param name="stageModel">Modell der Stufe; Reihenfolge: Stufe → Modell des Zugangs → Empfehlung für <paramref name="stage"/>.
    /// Ohne Katalog und ohne Modell des Zugangs gilt auch das Stufen-Modell nicht.</param>
    public static ApiAccess ResolveLlm(
        ApiAccess stt,
        string providerId,
        string baseUrl,
        string apiKey,
        string model,
        IServerModelLookup? serverModels = null,
        bool sttOffline = false,
        string stageModel = "",
        RefineMode? stage = null)
    {
        var same = string.IsNullOrWhiteSpace(providerId) || providerId == LlmSame;
        if (same && sttOffline)
        {
            return new ApiAccess("", "", "", stt.ReadTimeoutMs, stt.Provider, null, SameAsOffline: true);
        }
        var provider = same ? stt.Provider : ProviderCatalog.ById(providerId);
        var sameProvider = provider.Id == stt.Provider.Id;
        var accessModel = IfBlank(model.Trim(), provider.RecommendedLlmModel(stage));
        var modelId = accessModel.Length == 0 ? "" : IfBlank(stageModel.Trim(), accessModel);

        var url = same ? stt.BaseUrl
            : !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.Trim()
            : sameProvider ? stt.BaseUrl
            : provider.BaseUrl;
        var key = same ? stt.ApiKey
            : !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim()
            : sameProvider ? stt.ApiKey
            : "";
        return new ApiAccess(url, key, modelId, sameProvider ? stt.ReadTimeoutMs : TimeoutMs(0, provider), provider,
            Option(provider, ModelKind.Llm, url, modelId, serverModels ?? ServerModelLookup.None));
    }

    /// <summary>
    /// Fallback der Erkennung: eigener Anbieter, Key und Modell. Leere Felder füllen sich nur aus <paramref name="primary"/>,
    /// wenn es derselbe Anbieter (und derselbe Server) ist.
    /// </summary>
    public static ApiAccess ResolveSttFallback(
        ApiAccess primary,
        string providerId,
        string baseUrl,
        string apiKey,
        string model,
        IServerModelLookup? serverModels = null)
    {
        var same = ProviderCatalog.ById(providerId).Id == primary.Provider.Id;
        return ResolveStt(
            providerId,
            string.IsNullOrWhiteSpace(baseUrl) ? (same ? primary.BaseUrl : "") : baseUrl,
            string.IsNullOrWhiteSpace(apiKey) ? (same && SameServer(baseUrl, primary) ? primary.ApiKey : "") : apiKey,
            model,
            serverModels: serverModels);
    }

    /// <summary>
    /// Fallback der Textverbesserung. Leere Adresse und leerer Key kommen von einem Zugang desselben Anbieters und Servers:
    /// erst vom KI-Zugang, dann von der Erkennung (nur online). Das Modell gilt für alle Stufen; leer = Empfehlung für
    /// <paramref name="stage"/>. Die Stufen-Modelle gehören zum ersten Anbieter und zählen hier nie.
    /// </summary>
    public static ApiAccess ResolveLlmFallback(
        ApiAccess stt,
        ApiAccess primary,
        string providerId,
        string baseUrl,
        string apiKey,
        string model,
        IServerModelLookup? serverModels = null,
        bool sttOffline = false,
        RefineMode? stage = null)
    {
        var provider = ProviderCatalog.ById(providerId);
        ApiAccess?[] donors = [primary.SameAsOffline ? null : primary, sttOffline ? null : stt];
        var donor = donors.FirstOrDefault(d => d is not null && d.Provider.Id == provider.Id && SameServer(baseUrl, d));
        var url = IfBlank(baseUrl.Trim(), donor?.BaseUrl ?? provider.BaseUrl);
        var modelId = IfBlank(model.Trim(), provider.RecommendedLlmModel(stage));
        return new ApiAccess(url, IfBlank(apiKey.Trim(), donor?.ApiKey ?? ""), modelId, TimeoutMs(0, provider), provider,
            Option(provider, ModelKind.Llm, url, modelId, serverModels ?? ServerModelLookup.None));
    }

    /// <summary>
    /// Darf ein leerer Key des Fallbacks den von <paramref name="donor"/> erben? Bei fester Adresse ist derselbe Anbieter
    /// derselbe Server; beim eigenen Server und Ollama lokal nur bei leerer oder gleicher Adresse.
    /// </summary>
    private static bool SameServer(string baseUrl, ApiAccess donor) =>
        !donor.Provider.NeedsUrl || string.IsNullOrWhiteSpace(baseUrl) || Normalized(baseUrl) == Normalized(donor.BaseUrl);

    private static string Normalized(string url) => url.Trim().TrimEnd('/').ToLowerInvariant();

    /// <summary>
    /// Katalog zuerst; nur ohne Treffer den Cache fragen und Flags ableiten. Hat ein Textmodell <c>temperature</c> schon
    /// einmal abgelehnt, geht es ohne raus — auch ein Katalog-Modell (der Katalog darf irren).
    /// </summary>
    private static ModelOption? Option(ProviderInfo provider, ModelKind kind, string url, string id, IServerModelLookup serverModels)
    {
        var catalog = kind == ModelKind.Stt ? provider.SttModel(id) : provider.LlmModel(id);
        var option = catalog is not null || id.Trim().Length == 0
            ? catalog
            : ModelLists.OptionFor(provider, kind, id, serverModels.Find(provider.Id, kind, url, id));
        var rejected = kind == ModelKind.Llm && id.Trim().Length > 0 && option?.TemperatureSupported != false &&
            serverModels.RejectsTemperature(provider.Id, url, id);
        return rejected ? (option ?? new ModelOption(id, id)) with { TemperatureSupported = false } : option;
    }

    private static int TimeoutMs(int seconds, ProviderInfo provider) => (seconds > 0 ? seconds : provider.DefaultReadTimeoutSec) * 1000;

    private static string IfBlank(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
