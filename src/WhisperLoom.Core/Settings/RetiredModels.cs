using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Abgeschaltete OpenAI-Modelle (<see cref="ProviderCatalog.SuccessorOf"/>) werden still zum Nachfolger (Port von
/// <c>PrefsMigration.toV8</c>, TD-A4): Erkennung, Text-Zugang, jede Stufe und beide Fallbacks, je mit dem Anbieter, zu dem
/// das Feld gehört. Andere Anbieter und eigene Server bleiben unberührt. Läuft beim Laden der Einstellungen und beim
/// Lesen jeder Sync-/Provisioning-/Server-Datei; idempotent (ein Nachfolger ist nie selbst abgeschaltet), daher ohne
/// neue Schema-Version.
/// </summary>
public static class RetiredModels
{
    /// <summary>Stellt die Modelle in <paramref name="root"/> (Sync-Format v1) an Ort und Stelle um.</summary>
    /// <returns>Geänderte Pfade (<c>stt.model</c>, <c>llm.stageModels.polish</c> …), ohne Werte.</returns>
    public static IReadOnlyList<string> Apply(JsonObject root)
    {
        var changed = new List<string>();
        var stt = Provider(root, "stt", "openai");
        var llm = Provider(root, "llm", LlmSettings.Same);
        // "wie Erkennung": der Text-Zugang läuft beim Anbieter der Erkennung.
        var llmProvider = llm is LlmSettings.Same or "" ? stt : llm;
        Migrate(root, "stt", "model", stt, changed);
        Migrate(root, "sttFallback", "model", Provider(root, "sttFallback", "openai"), changed);
        Migrate(root, "llm", "model", llmProvider, changed);
        Migrate(root, "llmFallback", "model", Provider(root, "llmFallback", "openai"), changed);
        if (root["llm"] is JsonObject l && l["stageModels"] is JsonObject stages)
        {
            foreach (var stage in stages.Select(s => s.Key).ToList())
            {
                MigrateField(stages, stage, llmProvider, $"llm.stageModels.{stage}", changed);
            }
        }
        return changed;
    }

    private static string Provider(JsonObject root, string section, string fallback) =>
        root[section] is JsonObject o && o["providerId"] is JsonValue v && v.TryGetValue<string>(out var id) ? id.Trim() : fallback;

    private static void Migrate(JsonObject root, string section, string field, string providerId, List<string> changed)
    {
        if (root[section] is JsonObject o) MigrateField(o, field, providerId, $"{section}.{field}", changed);
    }

    private static void MigrateField(JsonObject o, string field, string providerId, string path, List<string> changed)
    {
        if (o[field] is not JsonValue v || !v.TryGetValue<string>(out var model)) return;
        if (ProviderCatalog.SuccessorOf(providerId, model) is { } successor)
        {
            o[field] = successor;
            changed.Add(path);
        }
    }
}
