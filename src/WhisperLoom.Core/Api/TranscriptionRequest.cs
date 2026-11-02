using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Was an POST /audio/transcriptions geht — Endpunkt und Multipart-Felder je Anbieter und Modell (Port von
/// <c>api/TranscriptionRequest.kt</c>). Ein falsches Feld heißt 400/422 beim Anbieter.
/// </summary>
public static partial class TranscriptionRequest
{
    public const string Path = "/audio/transcriptions";

    /// <summary>Mistral nimmt höchstens 100 Einträge in <c>context_bias</c>.</summary>
    public const int MaxContextBias = 100;

    /// <summary>Sprachfeld von gpt-transcribe: Liste erwarteter Sprachen (<see cref="Contracts.ModelOption.LanguageField"/>).</summary>
    private const string LanguagesField = "languages[]";

    private const string KeywordsField = "keywords[]";

    /// <summary>Höchstzahl der Begriffe an den Firmen-Proxy (docs/server-api.md §3.7, W3-REST) — wie ElevenLabs/Mistral.</summary>
    public const int MaxProxyKeywords = 100;

    private const int MaxTermChars = 49;
    private const int MaxTermWords = 5;

    [GeneratedRegex("[<>\r\n]")]
    private static partial Regex KeywordForbidden();

    /// <summary>Jeder Leerraum, auch Unicode (U+00A0, U+202F …) — Mistral prüft <c>\s</c> nicht nur auf ASCII.</summary>
    [GeneratedRegex(@"[\s\p{Z}]+")]
    private static partial Regex Whitespace();

    /// <summary>DeepInfra legt Audio nicht unter der Chat-Base-URL ab → Override aus dem Katalog.</summary>
    public static string Url(ApiAccess access) => access.Provider.SttPathOverride ?? Http.Endpoint(access.BaseUrl, Path);

    /// <summary>
    /// Textfelder in Sendereihenfolge (die Datei kommt zuletzt): <c>languages[]</c> statt <c>language</c> bei gpt-transcribe,
    /// keine Sprache bei "auto", bei „Deutsch + Englisch“ an gpt-transcribe beide Codes, an alle anderen keinen (sie kennen
    /// nur einen und erkennen selbst), <c>prompt</c>/<c>response_format</c> nur, wenn der Anbieter sie kennt, kein leeres
    /// <c>model</c>. Das Vokabular zusätzlich als Einzelbegriffe: <c>keywords[]</c> (gpt-transcribe) bzw. statt
    /// <c>prompt</c> als <c>context_bias</c> (Mistral). Der Firmen-Proxy (<see cref="CompanyProvider.EntryId"/>) bekommt die
    /// neutrale Form — <c>languages[]</c> bei „Deutsch + Englisch“, Vokabular als höchstens <see cref="MaxProxyKeywords"/>
    /// <c>keywords[]</c> — und übersetzt je Anbieter seines Zugangs (W3-REST, docs/server-api.md §3.7).
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Fields(ApiAccess access, string language, string prompt)
    {
        var fields = new List<(string Name, string Value)>();
        if (!string.IsNullOrWhiteSpace(access.Model)) fields.Add(("model", access.Model));
        if (access.Provider.SttSendsResponseFormat) fields.Add(("response_format", "json"));
        var lang = language.Trim();
        var proxy = access.Provider.Id == CompanyProvider.EntryId;
        var field = access.ModelOption?.LanguageField ?? (proxy && lang == WhisperLoomSettings.LanguageDeEn ? LanguagesField : "language");
        if (lang == WhisperLoomSettings.LanguageDeEn)
        {
            if (field == LanguagesField)
            {
                fields.Add((field, "de"));
                fields.Add((field, "en"));
            }
        }
        else if (lang.Length > 0 && lang != "auto")
        {
            fields.Add((field, lang));
        }
        if (access.Provider.SttSendsPrompt && !string.IsNullOrWhiteSpace(prompt)) fields.Add(("prompt", prompt.Trim()));
        if (access.ModelOption?.KeywordsField is { } keywords) fields.AddRange(Keywords(prompt).Select(t => (keywords, t)));
        else if (proxy) fields.AddRange(Keywords(prompt, MaxProxyKeywords).Select(t => (KeywordsField, t)));
        if (access.Provider.SttBiasField is { } bias) fields.AddRange(ContextBias(prompt).Select(t => (bias, t)));
        return fields;
    }

    /// <summary>
    /// Vokabular als <c>keywords[]</c> für gpt-transcribe: OpenAI lehnt die GANZE Anfrage ab, wenn ein Begriff <c>&lt;</c>,
    /// <c>&gt;</c> oder einen Zeilenumbruch enthält — solche Begriffe fallen weg, ebenso Freitext-Stücke.
    /// Quelle (laut Android): developers.openai.com/api/docs/guides/speech-to-text (Stand 2026-10-28).
    /// </summary>
    public static IReadOnlyList<string> Keywords(string prompt, int limit = int.MaxValue) =>
        Vocabulary.Terms(prompt, limit, accept: t => IsTerm(t) && !KeywordForbidden().IsMatch(t));

    /// <summary>
    /// Vokabular als <c>context_bias</c> für Mistral: höchstens <see cref="MaxContextBias"/> Einträge, je einer ohne
    /// Leerraum und Komma (Pattern <c>^[^,\s]+$</c>). Mehrteilige Begriffe werden wie im Doku-Beispiel
    /// <c>affordable_health_care</c> mit <c>_</c> verbunden — auch bei geschütztem Leerzeichen (U+00A0).
    /// Quelle (laut Android): docs.mistral.ai/api/endpoint/audio/transcriptions (Stand 2026-10-28).
    /// </summary>
    public static IReadOnlyList<string> ContextBias(string prompt) =>
        Vocabulary.Terms(prompt, MaxContextBias, transform: t => Whitespace().Replace(t, "_"), accept: IsTerm);

    /// <summary>
    /// Erkannte Sprache aus der Antwort; null = keine gemeldet. gpt-transcribe meldet <c>languages: [{"code": "de"}]</c>
    /// (leer bei unsicherer Erkennung) — die erste zählt, 3-stellig wird ISO 639-1. verbose_json und eigene Server:
    /// <c>language</c>. Sonst (json) kommt keine Sprache.
    /// </summary>
    public static string? DetectedLanguage(JsonObject json)
    {
        if (json["languages"] is JsonArray { Count: > 0 } list && list[0] is JsonObject first &&
            first["code"] is JsonValue c && c.TryGetValue<string>(out var code) && !string.IsNullOrWhiteSpace(code))
        {
            return ElevenLabsStt.IsoLanguage(code);
        }
        return json["language"] is JsonValue l && l.TryGetValue<string>(out var language) && !string.IsNullOrWhiteSpace(language)
            ? language
            : null;
    }

    /// <summary>Ein Begriff, kein Freitext: höchstens 5 Wörter und 49 Zeichen — dieselben Grenzen wie bei ElevenLabs.</summary>
    private static bool IsTerm(string term) => term.Length <= MaxTermChars && Whitespace().Split(term).Length <= MaxTermWords;
}
