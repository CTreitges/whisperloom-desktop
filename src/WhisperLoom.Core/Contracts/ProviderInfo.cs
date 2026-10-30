namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Welches Protokoll ein Anbieter spricht (Port von <c>ApiStyle</c>, <c>api/Provider.kt</c>).
/// <see cref="Ollama"/> = native Ollama-API (POST /api/chat, GET /api/tags);
/// <see cref="ElevenLabs"/> = ElevenLabs Speech-to-Text, nur Erkennung.
/// </summary>
public enum ApiStyle
{
    OpenAi,
    Ollama,
    ElevenLabs,
}

/// <summary>Ein Modell für die Auswahllisten (Port von <c>ModelOption</c>).</summary>
/// <param name="TemperatureSupported">Reasoning-Modelle lehnen <c>temperature</c> mit 400 ab.</param>
/// <param name="ReasoningEffort">Wert für <c>reasoning_effort</c>; null = Feld weglassen.</param>
/// <param name="LanguageField">Multipart-Feld der Sprache: <c>language</c> oder <c>languages[]</c>.</param>
/// <param name="ThinkingType">Wert für <c>"thinking": {"type": …}</c>; null = Feld weglassen.</param>
public sealed record ModelOption(
    string Id,
    string Label,
    string Note = "",
    bool TemperatureSupported = true,
    string? ReasoningEffort = null,
    string LanguageField = "language",
    string? ThinkingType = null);

/// <summary>
/// Ein Anbieter aus dem Katalog (Port der Daten von <c>Provider</c>; Inhalt kommt in Phase 1 aus
/// <c>providers.json</c>, verglichen mit <c>ProviderCatalog.kt</c>). Reine Daten, keine Keys.
/// </summary>
/// <param name="AllowsHttp">Unverschlüsseltes http:// nur für den eigenen Server (LAN/VPN).</param>
/// <param name="SttPathOverride">Kompletter Transkriptions-Endpunkt, wenn nicht <c>baseUrl + /audio/transcriptions</c>.</param>
/// <param name="SttMaxBytes">Dokumentiertes Upload-Limit; null = unbekannt.</param>
/// <param name="RewriteLlmModel">Empfehlung für Verschönern/Zusammenfassen/Prompt; "" = wie Glätten.</param>
public sealed record ProviderInfo(
    string Id,
    string Name,
    string BaseUrl,
    bool NeedsKey = true,
    string KeyUrl = "",
    bool AllowsHttp = false,
    int DefaultReadTimeoutSec = 90,
    IReadOnlyList<ModelOption>? SttModels = null,
    IReadOnlyList<ModelOption>? LlmModels = null,
    bool SttSendsPrompt = true,
    bool SttSendsResponseFormat = true,
    string? SttPathOverride = null,
    int? SttMaxBytes = null,
    ApiStyle Api = ApiStyle.OpenAi,
    string Notes = "",
    string RewriteLlmModel = "");
