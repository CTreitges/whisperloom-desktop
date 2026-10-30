using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Filterregeln der Modell-Listen je Anbieter (Port der privaten Teile von <c>api/ModelLists.kt</c>, Stand der
/// Anbieter-Doku 2026-09-30). Liefert ein Anbieter keine Felder, entscheidet der Name; unbekannte Felder stören nicht.
/// </summary>
internal static partial class ModelListFilters
{
    private const string Latest = "-latest";
    private const string AsrTask = "automatic-speech-recognition";
    private static readonly HashSet<string> TogetherOtherTypes = ["chat", "language", "code", "image", "embedding", "moderation", "rerank"];

    [GeneratedRegex("whisper|transcribe|voxtral-mini|parakeet|canary|(^|[-/_])asr([-_.]|$)|nova-3|(^|[-/_])stt([-_]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SttName();

    [GeneratedRegex("realtime|(^|[-_])live([-_]|$)|streaming|diarize|tts", RegexOptions.IgnoreCase)]
    private static partial Regex SttNameNot();

    [GeneratedRegex("whisper|transcribe|(^|[-/_])asr|parakeet|canary|tts|speech|orpheus|playai|kokoro|sonic|embed|rerank|moderation|guard|safeguard|shieldstral|ocr|image|dall-e|sora|video|realtime|(^|[-_])live([-_]|$)|(^|[-_])audio|search|computer-use|deep-research", RegexOptions.IgnoreCase)]
    private static partial Regex NotChat();

    [GeneratedRegex("realtime|live|diarize", RegexOptions.IgnoreCase)]
    private static partial Regex OpenAiSttNot();

    [GeneratedRegex("^(ft:)?(gpt-|o\\d|chatgpt-)", RegexOptions.IgnoreCase)]
    private static partial Regex OpenAiChat();

    [GeneratedRegex("transcribe|tts|whisper|realtime|(^|-)live(-|$)|audio|image|dall-e|sora|embedding|moderation|search|computer-use|deep-research|codex|-pro(\\b|-|$)|instruct|cyber|daybreak", RegexOptions.IgnoreCase)]
    private static partial Regex OpenAiNotChat();

    [GeneratedRegex("whisper", RegexOptions.IgnoreCase)]
    private static partial Regex Whisper();

    [GeneratedRegex("whisper|tts|orpheus|playai|guard|safeguard|compound", RegexOptions.IgnoreCase)]
    private static partial Regex GroqNotChat();

    [GeneratedRegex("voxtral.*(transcribe|mini)", RegexOptions.IgnoreCase)]
    private static partial Regex MistralSttName();

    [GeneratedRegex("realtime|tts", RegexOptions.IgnoreCase)]
    private static partial Regex MistralSttNameNot();

    [GeneratedRegex("whisper|parakeet|asr|voxtral|nova-3|deepgram/", RegexOptions.IgnoreCase)]
    private static partial Regex TogetherStt();

    [GeneratedRegex("guard|safeguard", RegexOptions.IgnoreCase)]
    private static partial Regex Guard();

    [GeneratedRegex("embed|aqa|imagen|veo|lyria|tts|image|live|native-audio|transcribe|robotics|computer-use|deep-research|antigravity|omni", RegexOptions.IgnoreCase)]
    private static partial Regex GeminiNotChat();

    [GeneratedRegex("embed", RegexOptions.IgnoreCase)]
    private static partial Regex Embed();

    public static List<RemoteModel> Select(ProviderInfo provider, bool stt, string body, DateOnly today)
    {
        // Einbettungs-Modelle (nomic-embed-text …) können keinen Text verbessern.
        if (provider.IsOllama) return [.. OllamaApi.ParseTags(body).Where(n => !Embed().IsMatch(n)).Select(n => new RemoteModel(n))];
        var list = Entries(body);
        if (provider.Api == ApiStyle.ElevenLabs) return ElevenLabs(list);
        return provider.Id switch
        {
            ProviderCatalog.OpenAiId => OpenAi(stt, list, today),
            "groq" => Groq(stt, list),
            "mistral" => Mistral(stt, list, today),
            "together" => Together(list),
            "deepinfra" => DeepInfra(list),
            "openrouter" => OpenRouter(stt, list, today),
            // Nur Chat-Modelle in der Liste; display_name ist ein guter Anzeigename.
            "anthropic" => [.. list.Where(o => Text(o, "id") is not null).Select(o => new RemoteModel(Text(o, "id")!, Text(o, "display_name")))],
            "gemini" => Gemini(list),
            "deepseek" => [.. list.Where(o => Text(o, "id") is not null).Select(o => new RemoteModel(Text(o, "id")!, Text(o, "name")))],
            _ => Custom(stt, list),
        };
    }

    /// <summary><c>{"data":[…]}</c> (OpenAI-Form) oder ein Array auf oberster Ebene (Together, ElevenLabs).</summary>
    private static List<JsonObject> Entries(string body)
    {
        var text = body.Trim();
        var array = text.StartsWith('[')
            ? JsonNode.Parse(text)!.AsArray()
            : JsonNode.Parse(text)?["data"] as JsonArray ?? throw new JsonException("keine Liste");
        return [.. array.OfType<JsonObject>()];
    }

    private static List<RemoteModel> OpenAi(bool stt, List<JsonObject> list, DateOnly today) => [.. list.Select(o =>
    {
        var id = Text(o, "id");
        if (id is null) return null;
        var shutdown = ModelLists.Date(o["shutdown_date"]);
        var keep = stt
            ? id == "whisper-1" || (id.Contains("transcribe", StringComparison.OrdinalIgnoreCase) && !OpenAiSttNot().IsMatch(id))
            : OpenAiChat().IsMatch(id) && !OpenAiNotChat().IsMatch(id);
        return keep && !Expired(shutdown, today) ? new RemoteModel(id, Note: ExpiryNote(shutdown)) : null;
    }).OfType<RemoteModel>()];

    /// <summary><c>active</c> steht nur im Doku-Beispiel — fehlend zählt als aktiv.</summary>
    private static List<RemoteModel> Groq(bool stt, List<JsonObject> list) => [.. list.Select(o =>
    {
        var id = Text(o, "id");
        if (id is null) return null;
        var keep = Bool(o, "active", true) && (stt ? Whisper().IsMatch(id) : !GroqNotChat().IsMatch(id));
        return keep ? new RemoteModel(id) : null;
    }).OfType<RemoteModel>()];

    private static List<RemoteModel> Mistral(bool stt, List<JsonObject> list, DateOnly today)
    {
        var kept = new List<(RemoteModel Model, List<string> Aliases)>();
        foreach (var o in list)
        {
            var id = Text(o, "id");
            if (id is null) continue;
            var caps = o["capabilities"] as JsonObject;
            var deprecation = ModelLists.Date(o["deprecation"]);
            bool keep;
            if (Expired(deprecation, today) || Bool(o, "archived", false)) keep = false;
            else if (caps is null && stt) keep = MistralSttName().IsMatch(id) && !MistralSttNameNot().IsMatch(id);
            else if (caps is null) keep = ChatByName(id);
            else if (stt) keep = Bool(caps, "audio_transcription", false) && !Bool(caps, "audio_transcription_realtime", false) && !id.Contains("realtime", StringComparison.OrdinalIgnoreCase);
            else keep = Bool(caps, "completion_chat", false) && !Bool(caps, "ocr", false) && !Bool(caps, "moderation", false) && !Bool(caps, "audio_speech", false);
            if (keep) kept.Add((new RemoteModel(id, Note: ExpiryNote(deprecation)), Strings(o["aliases"]) ?? []));
        }
        return PreferLatest(kept);
    }

    /// <summary>Mistral führt ein Modell unter jedem Namen einzeln; je Alias-Gruppe bleibt einer, bevorzugt <c>-latest</c>.</summary>
    private static List<RemoteModel> PreferLatest(List<(RemoteModel Model, List<string> Aliases)> models)
    {
        var seen = new HashSet<string>();
        var result = new List<RemoteModel>();
        foreach (var (model, aliases) in models.OrderByDescending(m => m.Model.Id.EndsWith(Latest, StringComparison.Ordinal)))
        {
            var group = aliases.Append(model.Id).ToList();
            if (group.Any(seen.Contains)) continue;
            seen.UnionWith(group);
            var latest = model.Id.EndsWith(Latest, StringComparison.Ordinal) ? model.Id : aliases.FirstOrDefault(a => a.EndsWith(Latest, StringComparison.Ordinal));
            result.Add(model with { Id = latest ?? model.Id });
        }
        return result;
    }

    /// <summary>Together liefert <c>type</c>, sonst entscheidet der Name; Deepgram läuft dort nur auf eigenen Instanzen.</summary>
    private static List<RemoteModel> Together(List<JsonObject> list) => [.. list.Select(o =>
    {
        var id = Text(o, "id");
        if (id is null) return null;
        var type = Text(o, "type")?.ToLowerInvariant();
        var keep = (type == "transcribe" || (!TogetherOtherTypes.Contains(type ?? "") && TogetherStt().IsMatch(id))) &&
            !id.StartsWith("deepgram/", StringComparison.OrdinalIgnoreCase);
        return keep ? new RemoteModel(id, Text(o, "display_name")) : null;
    }).OfType<RemoteModel>()];

    /// <summary>Jeder DeepInfra-Eintrag trägt genau ein Typ-Tag (stt, chat, tts, embed …).</summary>
    private static List<RemoteModel> DeepInfra(List<JsonObject> list) => [.. list.Select(o =>
    {
        var id = Text(o, "id");
        if (id is null) return null;
        var tags = Strings(o["metadata"]?["tags"]);
        return (tags is not null ? tags.Contains("stt") : SttByName(id)) ? new RemoteModel(id) : null;
    }).OfType<RemoteModel>()];

    /// <summary>OpenRouter: nur reine Text-Ausgabe, ohne Batch, eigene Router und Moderation. Einzige Liste mit Flags.</summary>
    private static List<RemoteModel> OpenRouter(bool stt, List<JsonObject> list, DateOnly today) => [.. list.Select(o =>
    {
        var id = Text(o, "id");
        if (id is null) return null;
        var output = Strings(o["architecture"]?["output_modalities"]);
        var input = Strings(o["architecture"]?["input_modalities"]);
        var expires = ModelLists.Date(o["expiration_date"]);
        var keep = stt
            ? output is null || output.Contains("transcription")
            : (output is null || output.SequenceEqual(["text"])) && (input is null || input.Contains("text")) &&
                !id.EndsWith(":batch", StringComparison.Ordinal) && !id.StartsWith("openrouter/", StringComparison.Ordinal) && !Guard().IsMatch(id);
        if (!keep || Expired(expires, today)) return null;
        return new RemoteModel(
            id,
            Text(o, "name"),
            ExpiryNote(expires),
            stt ? null : Strings(o["supported_parameters"]) is { } p ? p.Contains("temperature") : null,
            stt ? null : EffortNone(o["reasoning"] as JsonObject));
    }).OfType<RemoteModel>()];

    /// <summary>"none" nur, wenn das Modell das Nachdenken abschalten lässt; Pflicht-Reasoning = Feld weglassen.</summary>
    private static string? EffortNone(JsonObject? reasoning)
    {
        if (reasoning is null || Bool(reasoning, "mandatory", false)) return null;
        return (Strings(reasoning["supported_efforts"]) ?? []).Contains("none") ? "none" : null;
    }

    /// <summary>Gemini: IDs kommen als "models/gemini-…", im Chat-Request ohne Präfix.</summary>
    private static List<RemoteModel> Gemini(List<JsonObject> list) => [.. list.Select(o =>
    {
        var raw = Text(o, "id");
        var id = raw is null ? null : raw.StartsWith("models/", StringComparison.Ordinal) ? raw["models/".Length..] : raw;
        if (string.IsNullOrEmpty(id) || GeminiNotChat().IsMatch(id)) return null;
        return new RemoteModel(id, Text(o, "display_name"));
    }).OfType<RemoteModel>()];

    /// <summary>/v1/models listet laut Doku nur Sprachausgabe — leer ist normal. Realtime spricht WebSocket.</summary>
    private static List<RemoteModel> ElevenLabs(List<JsonObject> list) => [.. list.Select(o =>
    {
        var id = Text(o, "model_id");
        if (id is null) return null;
        var keep = id.StartsWith("scribe", StringComparison.OrdinalIgnoreCase) && !id.Contains("realtime", StringComparison.OrdinalIgnoreCase);
        return keep ? new RemoteModel(id, Text(o, "name")) : null;
    }).OfType<RemoteModel>()];

    /// <summary>Eigener Server: speaches kennzeichnet mit <c>task</c>, sonst der Name. Greift nichts, lieber alle zeigen.</summary>
    private static List<RemoteModel> Custom(bool stt, List<JsonObject> list)
    {
        var all = list.Select(o => (Id: Text(o, "id"), Task: Text(o, "task"))).Where(e => e.Id is not null).ToList();
        var kept = all.Where(e => e.Task is not null ? stt && e.Task == AsrTask : stt ? SttByName(e.Id!) : ChatByName(e.Id!)).ToList();
        return [.. (kept.Count > 0 ? kept : all).Select(e => new RemoteModel(e.Id!))];
    }

    private static bool SttByName(string id) => SttName().IsMatch(id) && !SttNameNot().IsMatch(id);

    private static bool ChatByName(string id) => !NotChat().IsMatch(id);

    /// <summary>Abgeschaltet ist, was heute oder früher endet.</summary>
    private static bool Expired(DateOnly? date, DateOnly today) => date is { } d && d <= today;

    private static string? ExpiryNote(DateOnly? date) => date is { } d ? $"Auslauf {d:yyyy-MM-dd}" : null;

    /// <summary>Nicht-leerer Text (auch aus Zahlen, wie <c>optString</c>).</summary>
    private static string? Text(JsonObject o, string key)
    {
        var s = o[key] switch
        {
            JsonValue v when v.TryGetValue<string>(out var str) => str,
            JsonValue v => v.ToJsonString(),
            _ => null,
        };
        s = s?.Trim();
        return string.IsNullOrEmpty(s) || s == "null" ? null : s;
    }

    private static bool Bool(JsonObject o, string key, bool fallback) => o[key] switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) && bool.TryParse(s, out var b) => b,
        _ => fallback,
    };

    private static List<string>? Strings(JsonNode? node) => node is JsonArray a
        ? [.. a.Select(e => e is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString()) : null).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!)]
        : null;
}
