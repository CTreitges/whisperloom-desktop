using System.Text.Json.Nodes;

namespace WhisperLoom.Core.Settings;

/// <summary>JSON-Baum als Pfad → Blattwert (Objekte werden aufgelöst, Listen und Skalare sind Blätter).</summary>
internal static class SyncTree
{
    public static Dictionary<string, JsonNode?> Leaves(JsonObject root)
    {
        var leaves = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        Walk(root, "", leaves);
        return leaves;
    }

    private static void Walk(JsonObject o, string prefix, Dictionary<string, JsonNode?> leaves)
    {
        foreach (var (name, value) in o)
        {
            var path = prefix + name;
            if (value is JsonObject child && child.Count > 0)
            {
                Walk(child, path + ".", leaves);
            }
            else
            {
                leaves[path] = value;
            }
        }
    }

    /// <summary>
    /// Mischt <paramref name="source"/> in <paramref name="target"/>: Objekte rekursiv, alles andere ersetzt.
    /// Felder, die <paramref name="skip"/> ablehnt, bleiben wie in <paramref name="target"/>; sie landen in <paramref name="skipped"/>.
    /// Ein Abschnitt mit abgelehnten Feldern darunter wird nicht durch einen Nicht-Objekt-Wert ersetzt.
    /// </summary>
    public static void Merge(JsonObject target, JsonObject source, string prefix, Func<string, bool> skip, List<string> skipped)
    {
        foreach (var (name, value) in source)
        {
            var path = prefix + name;
            if (value is JsonObject so && target[name] is JsonObject to)
            {
                Merge(to, so, path + ".", skip, skipped);
            }
            else if (value is JsonObject newObject && target[name] is null && !skip(path))
            {
                var created = new JsonObject();
                target[name] = created;
                Merge(created, newObject, path + ".", skip, skipped);
            }
            // Ersetzen eines Abschnitts durch Nicht-Objekt/null überschriebe auch gesperrte Felder darunter.
            else if (skip(path) || (target[name] is JsonObject existing && Leaves(existing).Keys.Any(k => skip(path + "." + k))))
            {
                if (!JsonNode.DeepEquals(target[name], value))
                {
                    skipped.Add(path);
                }
            }
            else
            {
                target[name] = value?.DeepClone();
            }
        }
    }

    /// <summary>Kurze Anzeige eines Blattwerts für die Vorschau.</summary>
    public static string? Show(JsonNode? node) => node?.ToJsonString();
}
