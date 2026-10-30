using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.History;

/// <summary>
/// JSON-Form eines Verlaufseintrags (eine Datei je Eintrag, Name = Id). Port von <c>HistoryEntry.toJson/fromJson</c>,
/// angepasst an den Desktop-Eintrag (eine Fassung statt Fassungen je Verarbeitung). Tolerant gelesen: fehlt ein
/// Feld, gilt der Standard, Unbekanntes wird ignoriert; ohne Rohtext unbrauchbar (null).
/// </summary>
internal static class HistoryJson
{
    private const int Format = 1;

    public static string Write(HistoryEntry e)
    {
        var o = new JsonObject
        {
            ["v"] = Format,
            ["createdAt"] = e.CreatedAt.ToUnixTimeMilliseconds(),
            ["language"] = e.Language,
            ["durationMs"] = e.DurationMs,
            ["raw"] = e.Raw,
            ["text"] = e.Text,
            ["refinement"] = new JsonObject
            {
                ["mode"] = e.Refinement.Mode.Key(),
                ["smartFillers"] = e.Refinement.SmartFillers,
                ["paragraphs"] = e.Refinement.Paragraphs,
            },
            ["sttLabel"] = e.SttLabel,
            ["model"] = e.Model,
            ["skipped"] = e.Skipped,
        };
        return o.ToJsonString();
    }

    /// <returns>null, wenn die Datei kein JSON-Objekt oder ohne Rohtext ist.</returns>
    public static HistoryEntry? Read(string id, string json)
    {
        JsonObject? o;
        try
        {
            o = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (o == null)
        {
            return null;
        }
        var raw = Text(o, "raw");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var refinement = ReadRefinement(o["refinement"] as JsonObject);
        var createdAt = Long(o, "createdAt") ?? (long.TryParse(id.Split('-')[0], out var ms) ? ms : 0L);
        return new HistoryEntry(
            id,
            DateTimeOffset.FromUnixTimeMilliseconds(createdAt),
            Text(o, "language") is { Length: > 0 } language ? language : "auto",
            Long(o, "durationMs") ?? 0L,
            raw,
            Text(o, "text"),
            refinement,
            Text(o, "sttLabel"),
            Text(o, "model") is { Length: > 0 } model ? model : null,
            Text(o, "skipped") is { Length: > 0 } skipped ? skipped : null);
    }

    /// <summary>Unbekannte oder fehlende Stufe gilt als aus.</summary>
    private static Refinement ReadRefinement(JsonObject? r)
    {
        var mode = r == null ? RefineMode.Off : RefineModes.FromKey(Text(r, "mode"));
        return mode == RefineMode.Off
            ? Refinement.Off
            : new Refinement(mode, Bool(r!, "smartFillers", false), Bool(r!, "paragraphs", true));
    }

    /// <summary>Text-Feld; fehlend, null oder kein Text = "".</summary>
    private static string Text(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static long? Long(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<long>(out var n) ? n : null;

    private static bool Bool(JsonObject o, string key, bool fallback) =>
        o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
}
