using System.Text.Json.Nodes;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Request-Body für POST /chat/completions (Port von <c>api/ChatPayload.kt</c>). Reasoning-Modelle lehnen
/// <c>temperature</c> ab und wollen <c>reasoning_effort</c> + <c>max_completion_tokens</c>; Claude 5 und DeepSeek schalten
/// das Nachdenken über <c>thinking</c> ab.
/// </summary>
public static class ChatPayload
{
    /// <summary>Deckel gegen Endlos-Ausgaben; die Denk-Token zählen mit (4096 reichten Claude Haiku 5.5 nicht).</summary>
    public const int MaxCompletionTokens = 16384;

    public sealed record Sampling(
        int? Temperature = null,
        string? ReasoningEffort = null,
        int? MaxCompletionTokens = null,
        string? ThinkingType = null);

    public static Sampling SamplingFor(ApiAccess access)
    {
        var option = access.ModelOption;
        // Unbekanntes Modell (frei eingetippt): klassisch mit temperature 0.
        var temperatureOk = option?.TemperatureSupported ?? true;
        return new Sampling(
            temperatureOk ? 0 : null,
            access.Provider.IsCustom ? "none" : option?.ReasoningEffort,
            temperatureOk ? null : MaxCompletionTokens,
            option?.ThinkingType);
    }

    public static string Build(ApiAccess access, string systemPrompt, string userText)
    {
        var s = SamplingFor(access);
        var json = new JsonObject { ["model"] = access.Model };
        if (s.Temperature is { } t) json["temperature"] = t;
        if (s.ReasoningEffort is { } e) json["reasoning_effort"] = e;
        if (s.MaxCompletionTokens is { } m) json["max_completion_tokens"] = m;
        if (s.ThinkingType is { } k) json["thinking"] = new JsonObject { ["type"] = k };
        json["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = userText });
        return json.ToJsonString();
    }
}
