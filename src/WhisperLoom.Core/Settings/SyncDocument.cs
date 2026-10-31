using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhisperLoom.Core.Settings;

/// <summary>Datei im Sync-Format ist ungültig. Die Meldung nennt Pfade, nie Werte (Keys!).</summary>
public sealed class SyncFormatException(string message) : Exception(message);

/// <summary>
/// Eine gelesene Datei im Format v1 (<c>docs/settings-format.md</c>), schon migriert: die Einstellungen als JSON
/// (ohne <c>secrets</c>/<c>policy</c>), dazu die Provisioning-Zusätze getrennt.
/// </summary>
/// <param name="Settings">Einstellungen samt <c>desktop</c>, <c>android</c> und unbekannten Feldern der obersten Ebene.</param>
/// <param name="Secrets">Provisioning: Anbieter-ID bzw. Zugangsname → Key; leer ohne <c>secrets</c>.</param>
/// <param name="Policy">Provisioning: Abschnitt <c>policy</c> oder null.</param>
public sealed record SyncDocument(JsonObject Settings, IReadOnlyDictionary<string, string> Secrets, JsonObject? Policy)
{
    public const string SecretsSection = "secrets";
    public const string PolicySection = "policy";
    public const string DesktopSection = "desktop";
    public const string AndroidSection = "android";

    /// <summary>Ist es eine Provisioning-Datei (mit Keys oder Richtlinie)?</summary>
    public bool IsProvisioning => Secrets.Count > 0 || Policy is not null;

    /// <exception cref="SyncFormatException">Kein JSON-Objekt, falscher Typ in secrets/policy.</exception>
    /// <exception cref="NotSupportedException">Schema einer neueren Version.</exception>
    public static SyncDocument Parse(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            // Nur Zeile/Position, nicht der Text: die Datei kann Keys enthalten.
            throw new SyncFormatException($"Kein gültiges JSON (Zeile {e.LineNumber + 1}, Position {e.BytePositionInLine + 1}).");
        }
        if (node is not JsonObject root)
        {
            throw new SyncFormatException("Die Datei muss ein JSON-Objekt sein.");
        }
        SyncMigration.Migrate(root);

        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root[SecretsSection] is { } s)
        {
            if (s is not JsonObject so)
            {
                throw new SyncFormatException("secrets muss ein Objekt sein (Anbieter → Key).");
            }
            foreach (var (name, value) in so)
            {
                if (value is not JsonValue v || !v.TryGetValue<string>(out var key) || string.IsNullOrWhiteSpace(key))
                {
                    throw new SyncFormatException($"secrets.{name} muss ein nicht leerer Text sein.");
                }
                if (key.Trim().Any(char.IsWhiteSpace))
                {
                    throw new SyncFormatException($"secrets.{name} enthält Leerraum.");
                }
                secrets[name] = key.Trim();
            }
        }
        JsonObject? policy = null;
        if (root[PolicySection] is { } p)
        {
            policy = p as JsonObject ?? throw new SyncFormatException("policy muss ein Objekt sein.");
        }
        root.Remove(SecretsSection);
        root.Remove(PolicySection);
        return new SyncDocument(root, secrets, policy);
    }
}
