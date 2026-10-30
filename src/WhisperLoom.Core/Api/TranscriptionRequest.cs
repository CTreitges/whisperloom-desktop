namespace WhisperLoom.Core.Api;

/// <summary>
/// Was an POST /audio/transcriptions geht — Endpunkt und Multipart-Felder je Anbieter und Modell (Port von
/// <c>api/TranscriptionRequest.kt</c>). Ein falsches Feld heißt 400/422 beim Anbieter.
/// </summary>
public static class TranscriptionRequest
{
    public const string Path = "/audio/transcriptions";

    /// <summary>DeepInfra legt Audio nicht unter der Chat-Base-URL ab → Override aus dem Katalog.</summary>
    public static string Url(ApiAccess access) => access.Provider.SttPathOverride ?? Http.Endpoint(access.BaseUrl, Path);

    /// <summary>
    /// Textfelder in Sendereihenfolge (die Datei kommt zuletzt): <c>languages[]</c> statt <c>language</c> bei gpt-transcribe,
    /// keine Sprache bei "auto", <c>prompt</c>/<c>response_format</c> nur, wenn der Anbieter sie kennt, kein leeres <c>model</c>.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Fields(ApiAccess access, string language, string prompt)
    {
        var fields = new List<(string Name, string Value)>();
        if (!string.IsNullOrWhiteSpace(access.Model)) fields.Add(("model", access.Model));
        if (access.Provider.SttSendsResponseFormat) fields.Add(("response_format", "json"));
        var lang = language.Trim();
        if (lang.Length > 0 && lang != "auto") fields.Add((access.ModelOption?.LanguageField ?? "language", lang));
        if (access.Provider.SttSendsPrompt && !string.IsNullOrWhiteSpace(prompt)) fields.Add(("prompt", prompt.Trim()));
        return fields;
    }
}
