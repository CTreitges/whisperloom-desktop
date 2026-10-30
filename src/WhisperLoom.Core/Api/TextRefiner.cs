using System.Text;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Textverbesserung über einen Online-Zugang (Port von <c>api/TextRefiner.kt</c>): POST /chat/completions bzw. bei
/// Ollama POST /api/chat. Lehnt der Server <c>temperature</c> mit 400 ab, folgt genau ein zweiter Versuch ohne.
/// </summary>
/// <param name="connectTimeoutMs">Kürzer, wenn ein Ersatz bereitsteht (Fallback-Zugang, lokales Textmodell).</param>
/// <param name="onTemperatureRejected">Der zweite Versuch ohne <c>temperature</c> hat geklappt — mit diesem Zugang.
/// Der Aufrufer merkt es sich (<see cref="ModelCache"/>).</param>
public sealed class TextRefiner(
    ApiAccess access,
    HttpClient http,
    IRefinePromptBuilder prompts,
    TextCleanup cleanup,
    int connectTimeoutMs = Http.ConnectTimeoutMs,
    Action<ApiAccess>? onTemperatureRejected = null) : ITextRefiner
{
    /// <summary>Das Modell hat an seiner Längengrenze aufgehört — nur der Anfang des Texts.</summary>
    public const string MsgTruncated = "Antwort des Modells abgeschnitten (Längengrenze)";

    /// <summary>Keine oder leere Antwort (nur Nachdenken oder Markierung).</summary>
    public const string MsgEmpty = "Leere Antwort des Modells";

    /// <summary>Der Erkennungs-Anbieter hat keinen Chat (ElevenLabs), ein eigener Zugang fehlt.</summary>
    public const string MsgNoLlm = "Der Erkennungs-Anbieter kann keinen Text verbessern — unter „KI-Zugang“ einen eigenen Zugang eintragen";

    /// <summary>„Wie Erkennung“ bei Together/DeepInfra ohne eingetragenes Textmodell.</summary>
    public const string MsgNoModel = "Kein Textmodell eingetragen — unter „KI-Zugang“ ein Modell eintragen";

    /// <summary>„Wie Erkennung“ bei lokaler Erkennung: kein Online-Zugang, an den der Text gehen dürfte.</summary>
    public const string MsgOffline = "Offline-Erkennung ohne Textverbesserung — unter „KI-Zugang“ einen eigenen Zugang eintragen";

    private const string Length = "length";

    public string ModelLabel => access.ModelOption?.Label ?? access.Model;

    public async Task<string> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || refinement.Mode == RefineMode.Off) return raw;
        // Klarer Hinweis statt 404/400 und ohne Anfrage.
        switch (access.RefineBlock)
        {
            case RefineBlock.NoChat: throw new ApiNotConfiguredException(MsgNoLlm);
            case RefineBlock.NoModel: throw new ApiNotConfiguredException(MsgNoModel);
            case RefineBlock.Offline: throw new ApiNotConfiguredException(MsgOffline);
        }
        if (string.IsNullOrWhiteSpace(access.BaseUrl)) throw new ApiNotConfiguredException();

        var (system, user) = Messages(prompts, raw, language, refinement);
        var output = access.Provider.IsOllama
            ? await OllamaAsync(system, user, ct).ConfigureAwait(false)
            : await OpenAiAsync(system, user, ct).ConfigureAwait(false);
        return cleanup.Finish(raw, refinement.Mode, output);
    }

    /// <summary>System-Prompt und markiertes Diktat — online wie lokal derselbe Auftrag. Deutsch nur bei "de".</summary>
    public static (string System, string User) Messages(IRefinePromptBuilder prompts, string raw, string language, Refinement refinement)
    {
        var german = language == "de";
        var system = prompts.Build(refinement.Mode, german, refinement.SmartFillers, refinement.Paragraphs, TextCleanup.IsShort(raw));
        return (system, prompts.UserText(raw, german));
    }

    private async Task<string?> OpenAiAsync(string system, string user, CancellationToken ct)
    {
        string body;
        try
        {
            body = await ChatAsync(access, system, user, ct).ConfigureAwait(false);
        }
        catch (ApiHttpException e) when (RejectsTemperature(e) && ChatPayload.SamplingFor(access).Temperature is not null)
        {
            var option = access.ModelOption ?? new ModelOption(access.Model, access.Model);
            var withoutTemperature = access with { ModelOption = option with { TemperatureSupported = false } };
            body = await ChatAsync(withoutTemperature, system, user, ct).ConfigureAwait(false);
            onTemperatureRejected?.Invoke(withoutTemperature);
        }
        var choice = JsonNode.Parse(body)?["choices"]?[0];
        if (Text(choice?["finish_reason"]) == Length) throw new RefineRejectedException(MsgTruncated);
        return Text(choice?["message"]?["content"]);
    }

    private Task<string> ChatAsync(ApiAccess a, string system, string user, CancellationToken ct) =>
        Http.PostAsync(http, Http.Endpoint(a.BaseUrl, "/chat/completions"), a.ApiKey,
            Encoding.UTF8.GetBytes(ChatPayload.Build(a, system, user)), "application/json",
            new HttpCallOptions(a.ReadTimeoutMs, ConnectTimeoutMs: connectTimeoutMs), ct);

    private async Task<string?> OllamaAsync(string system, string user, CancellationToken ct)
    {
        var body = await Http.PostAsync(http, OllamaApi.ChatUrl(access.BaseUrl), access.ApiKey,
            Encoding.UTF8.GetBytes(OllamaApi.ChatPayload(access.Model, system, user)), "application/json",
            new HttpCallOptions(access.ReadTimeoutMs, ConnectTimeoutMs: connectTimeoutMs), ct).ConfigureAwait(false);
        if (Text(JsonNode.Parse(body)?["done_reason"]) == Length) throw new RefineRejectedException(MsgTruncated);
        return OllamaApi.ParseChat(body);
    }

    /// <summary>OpenAI: „Unsupported parameter: 'temperature' …“; Claude 5.x ebenso mit 400.</summary>
    private static bool RejectsTemperature(ApiHttpException e) =>
        e.StatusCode == 400 && e.Message.Contains("temperature", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
