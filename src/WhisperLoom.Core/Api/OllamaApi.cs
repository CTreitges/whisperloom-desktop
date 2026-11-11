using System.Text.Json.Nodes;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Native Ollama-API, lokal und ollama.com (Port von <c>api/OllamaApi.kt</c>): POST /api/chat und GET /api/tags. Über die
/// native Schnittstelle trennt Ollama das Nachdenken zuverlässig in <c>message.thinking</c> ab.
/// </summary>
// Doku: https://github.com/ollama/ollama/blob/main/docs/api.md (abgerufen 2026-10-30)
public static class OllamaApi
{
    /// <summary>Server-Wurzel: Endungen /api und /v1 (so stehen sie in vielen Anleitungen) werden abgeschnitten.</summary>
    public static string Root(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        while (true)
        {
            var stripped = RemoveSuffix(RemoveSuffix(url, "/api"), "/v1").TrimEnd('/');
            if (stripped == url) return url;
            url = stripped;
        }
    }

    public static string ChatUrl(string baseUrl) => Root(baseUrl) + "/api/chat";

    public static string TagsUrl(string baseUrl) => Root(baseUrl) + "/api/tags";

    /// <summary>
    /// Body für /api/chat. <c>think</c> bewusst nicht false (dann schreibt ein Modell sein Nachdenken in die Antwort);
    /// einzige Ausnahme gpt-oss, das nur Stufen kennt: "low".
    /// </summary>
    public static string ChatPayload(string model, string systemPrompt, string userText)
    {
        var json = new JsonObject { ["model"] = model, ["stream"] = false };
        if (model.Trim().ToLowerInvariant().StartsWith("gpt-oss", StringComparison.Ordinal)) json["think"] = "low";
        json["options"] = new JsonObject { ["temperature"] = 0, ["num_ctx"] = NumCtx(systemPrompt, userText) };
        json["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = userText });
        return json.ToJsonString();
    }

    /// <summary>Kleinster gesendeter Kontext: der Ollama-Standard bei unter 24 GiB VRAM (4k).</summary>
    public const int MinNumCtx = 4096;

    /// <summary>Größter gesendeter Kontext: genug für sehr lange Diktate, ohne kleine Rechner zu überlasten.</summary>
    public const int MaxNumCtx = 16384;

    /// <summary>
    /// <c>options.num_ctx</c> passend zur Länge (TS-WSTUFEN, WLD-P1b-9): ohne kürzt Ollama still auf seinen Standard-Kontext.
    /// Schätzung ≈ 3 Zeichen je Token (vorsichtig für Deutsch): Eingabe <c>⌈(System+Nutzer)/3⌉</c>, Antwort höchstens doppelt
    /// so lang wie das Diktat (<c>⌈2·Nutzer/3⌉</c>, wie <c>TextCleanup.MaxGrowth</c>), dazu 256 Token Reserve. Gerundet auf die
    /// nächste Zweierpotenz zwischen <see cref="MinNumCtx"/> und <see cref="MaxNumCtx"/>: wenige Stufen, denn ein anderes
    /// num_ctx lässt Ollama das Modell neu laden.
    /// </summary>
    // Doku: https://docs.ollama.com/api/chat (options.num_ctx „Context length size (number of tokens)“) und
    // https://docs.ollama.com/context-length (Standard nach VRAM: < 24 GiB → 4k), abgerufen 2026-11-11.
    public static int NumCtx(string systemPrompt, string userText)
    {
        var need = (systemPrompt.Length + userText.Length + 2) / 3 + (2 * userText.Length + 2) / 3 + 256;
        var ctx = MinNumCtx;
        while (ctx < need && ctx < MaxNumCtx) ctx *= 2;
        return ctx;
    }

    /// <summary>Antworttext aus /api/chat — null, wenn keiner da ist.</summary>
    public static string? ParseChat(string body) =>
        JsonNode.Parse(body)?["message"]?["content"] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)
            ? s
            : null;

    /// <summary>Modellnamen aus /api/tags, alphabetisch und ohne Duplikate.</summary>
    public static IReadOnlyList<string> ParseTags(string body)
    {
        if (JsonNode.Parse(body)?["models"] is not JsonArray models) return [];
        return
        [
            .. models.OfType<JsonObject>()
                .Select(m => Name(m["name"]) ?? Name(m["model"]))
                .OfType<string>()
                .Distinct()
                .OrderBy(n => n.ToLowerInvariant(), StringComparer.Ordinal),
        ];
    }

    private static string? Name(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static string RemoveSuffix(string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;
}
