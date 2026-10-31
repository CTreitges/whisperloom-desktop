using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Hebt eine Datei schrittweise auf <see cref="WhisperLoomSettings.CurrentSchemaVersion"/>. Ein neues Schema bekommt
/// hier einen weiteren Schritt; Dateien einer neueren Version werden abgelehnt statt still gekürzt.
/// </summary>
public static class SyncMigration
{
    /// <summary>Schema ohne <c>schemaVersion</c> (von Hand geschriebene Dateien, frühe Vorschau-Builds).</summary>
    public const int Unversioned = 0;

    private static readonly IReadOnlyList<Action<JsonObject>> Steps = [V0ToV1];

    /// <summary>Migriert <paramref name="root"/> an Ort und Stelle.</summary>
    /// <returns>Die Ausgangsversion.</returns>
    /// <exception cref="SyncFormatException"><c>schemaVersion</c> ist keine ganze Zahl ≥ 0.</exception>
    /// <exception cref="NotSupportedException">Neuere Version, als diese App kennt.</exception>
    public static int Migrate(JsonObject root)
    {
        var version = Unversioned;
        if (root["schemaVersion"] is { } node)
        {
            if (node is not JsonValue v || !v.TryGetValue<int>(out version) || version < 0)
            {
                throw new SyncFormatException("schemaVersion muss eine ganze Zahl ≥ 0 sein.");
            }
        }
        if (version > WhisperLoomSettings.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Die Datei hat Schema {version}, diese Version kennt nur bis {WhisperLoomSettings.CurrentSchemaVersion}. Bitte WhisperLoom aktualisieren.");
        }
        for (var from = version; from < WhisperLoomSettings.CurrentSchemaVersion; from++)
        {
            Steps[from](root);
        }
        root["schemaVersion"] = WhisperLoomSettings.CurrentSchemaVersion;
        return version;
    }

    /// <summary>
    /// v0 → v1: Altwert „paragraphs“ (frühere Stufe „Absätze“ auf Android) wird „polish“; Füllwörter klein und ohne
    /// Duplikate wie in <see cref="PolishSettings"/> beschrieben.
    /// </summary>
    private static void V0ToV1(JsonObject root)
    {
        foreach (var section in new[] { "refine", "share" })
        {
            if (root[section] is JsonObject o && o["stage"] is JsonValue s && s.TryGetValue<string>(out var stage) && stage == "paragraphs")
            {
                o["stage"] = "polish";
            }
        }
        if (root["polish"] is JsonObject polish)
        {
            foreach (var list in new[] { "customFillers", "disabledFillers" })
            {
                if (polish[list] is JsonArray a && a.All(n => n is JsonValue v && v.TryGetValue<string>(out _)))
                {
                    var words = a.Select(n => n!.GetValue<string>().Trim().ToLowerInvariant()).Where(w => w != "").Distinct().ToList();
                    polish[list] = new JsonArray([.. words.Select(w => (JsonNode)w)]);
                }
            }
        }
    }
}
