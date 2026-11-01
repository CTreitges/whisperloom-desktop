using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Abgeschaltete OpenAI-Modelle (<see cref="ProviderCatalog.SuccessorOf"/>) werden still zum Nachfolger (Port von
/// <c>PrefsMigration.toV8</c>, TD-A4): Erkennung, Text-Zugang, jede Stufe und beide Fallbacks, je mit dem Anbieter, zu dem
/// das Feld gehört. Andere Anbieter und eigene Server bleiben unberührt. Läuft beim Laden der Einstellungen, auf dem
/// zusammengeführten Stand von Import/Provisioning/Server-Konfiguration, auf den festgehaltenen Fest-Werten und den
/// Vorgaben der Richtlinie; idempotent (ein Nachfolger ist nie selbst abgeschaltet), daher ohne neue Schema-Version.
/// </summary>
public static class RetiredModels
{
    private static readonly string[] Sections = ["stt", "sttFallback", "llm", "llmFallback"];

    /// <summary>Vollständige Einstellungen (settings.json, zusammengeführter Stand): fehlender Anbieter = Standard.</summary>
    public static IReadOnlyList<string> ApplyFull(JsonObject root) => Apply(root, root);

    /// <summary>
    /// Stellt die Modelle in <paramref name="root"/> (Sync-Format v1) an Ort und Stelle um. Der Anbieter eines Abschnitts
    /// kommt aus <paramref name="root"/>, sonst aus <paramref name="context"/>, sonst gilt der Standard — ist
    /// <paramref name="context"/> null (Teil-Datei ohne bekannten Stand), wird ein Abschnitt ohne eigenen
    /// <c>providerId</c> nicht angefasst: Er könnte zu einem eigenen Server gehören.
    /// </summary>
    /// <returns>Geänderte Pfade (<c>stt.model</c>, <c>llm.stageModels.polish</c> …), ohne Werte.</returns>
    public static IReadOnlyList<string> Apply(JsonObject root, JsonObject? context)
    {
        var changed = new List<string>();
        var stt = Provider(root, context, "stt", "openai");
        var llm = Provider(root, context, "llm", LlmSettings.Same);
        // "wie Erkennung": der Text-Zugang läuft beim Anbieter der Erkennung.
        var llmProvider = llm is LlmSettings.Same or "" ? stt : llm;
        Migrate(root, "stt", "model", stt, changed);
        Migrate(root, "sttFallback", "model", Provider(root, context, "sttFallback", "openai"), changed);
        Migrate(root, "llm", "model", llmProvider, changed);
        Migrate(root, "llmFallback", "model", Provider(root, context, "llmFallback", "openai"), changed);
        if (root["llm"] is JsonObject l && l["stageModels"] is JsonObject stages)
        {
            foreach (var stage in stages.Select(s => s.Key).ToList())
            {
                MigrateField(stages, stage, llmProvider, $"llm.stageModels.{stage}", changed);
            }
        }
        return changed;
    }

    /// <summary>
    /// Vorgaben der Richtlinie (Stufe „Vorgabe“): Modell-Werte gehen ebenfalls auf den Nachfolger, sonst schriebe
    /// „Auf Firmenvorgabe zurücksetzen“ das abgeschaltete Modell zurück. Der Anbieter kommt aus den Vorgaben, sonst aus
    /// <paramref name="settings"/>.
    /// </summary>
    /// <returns>Die Richtlinie (dieselbe Instanz ohne Änderung) und die umgestellten Vorgabe-Pfade.</returns>
    public static (PolicySettings Policy, IReadOnlyList<string> Changed) ApplyDefaults(PolicySettings policy, WhisperLoomSettings settings)
    {
        if (policy.Defaults is not { Count: > 0 } defaults || !defaults.Keys.Any(IsModelArea))
        {
            return (policy, []);
        }
        var tree = JsonNode.Parse(SettingsJson.Serialize(settings))!.AsObject();
        foreach (var (path, value) in defaults)
        {
            ManagedPaths.Set(tree, path, JsonSerializer.SerializeToNode(value));
        }
        ApplyFull(tree);
        var result = new Dictionary<string, JsonElement>(defaults, StringComparer.OrdinalIgnoreCase);
        var changed = new List<string>();
        foreach (var (path, value) in defaults)
        {
            if (ManagedPaths.Find(tree, path) is { } now && !JsonNode.DeepEquals(now, JsonSerializer.SerializeToNode(value)))
            {
                result[path] = JsonSerializer.SerializeToElement(now);
                changed.Add(path);
            }
        }
        return changed.Count == 0 ? (policy, []) : (policy with { Defaults = result }, changed);
    }

    /// <summary>Liegt <paramref name="path"/> in einem Abschnitt mit Modell (stt, sttFallback, llm, llmFallback) oder darüber?</summary>
    private static bool IsModelArea(string path) =>
        Sections.Any(s => ManagedPaths.IsAtOrBelow(path, s) || ManagedPaths.IsAtOrBelow(s, path));

    private static string? Provider(JsonObject root, JsonObject? context, string section, string standard) =>
        Explicit(root, section) ?? (context is null ? null : Explicit(context, section) ?? standard);

    private static string? Explicit(JsonObject root, string section) =>
        root[section] is JsonObject o && o["providerId"] is JsonValue v && v.TryGetValue<string>(out var id) ? id.Trim() : null;

    private static void Migrate(JsonObject root, string section, string field, string? providerId, List<string> changed)
    {
        if (root[section] is JsonObject o) MigrateField(o, field, providerId, $"{section}.{field}", changed);
    }

    private static void MigrateField(JsonObject o, string field, string? providerId, string path, List<string> changed)
    {
        if (providerId is null || o[field] is not JsonValue v || !v.TryGetValue<string>(out var model)) return;
        if (ProviderCatalog.SuccessorOf(providerId, model) is { } successor)
        {
            o[field] = successor;
            changed.Add(path);
        }
    }
}
