using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Services;

/// <summary>
/// Einstellungen als JSON-Pfade (<c>stt.model</c>, <c>desktop.hotkeyHold</c>): Unterschiede finden und gezielt
/// übertragen. Das Einstellungsfenster speichert sofort und überträgt dabei nur die geänderten Pfade auf den aktuellen
/// Stand der App – Änderungen anderer Stellen (Stufe im Tray, Schriftgröße der Bühne) bleiben so erhalten.
/// </summary>
public static class SettingsMerge
{
    public static JsonObject ToJson(WhisperLoomSettings settings) => JsonNode.Parse(SettingsJson.Serialize(settings))!.AsObject();

    public static WhisperLoomSettings FromJson(JsonObject json) => SettingsJson.Deserialize(json.ToJsonString());

    /// <summary>Blätter: Objekte werden aufgelöst, Listen, Skalare und leere Objekte sind Blätter.</summary>
    public static Dictionary<string, JsonNode?> Leaves(JsonObject root)
    {
        var leaves = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        Walk(root, "", leaves);
        return leaves;
    }

    /// <summary>Pfade, deren Blattwert sich unterscheidet oder nur auf einer Seite existiert.</summary>
    public static IReadOnlyList<string> Changed(WhisperLoomSettings before, WhisperLoomSettings after)
    {
        var a = Leaves(ToJson(before));
        var b = Leaves(ToJson(after));
        return [.. a.Keys.Union(b.Keys).Where(k => !(a.TryGetValue(k, out var x) && b.TryGetValue(k, out var y) && JsonNode.DeepEquals(x, y)))];
    }

    /// <summary>Kopie von <paramref name="target"/>, in der die <paramref name="paths"/> wie in <paramref name="source"/> stehen.</summary>
    public static WhisperLoomSettings Apply(WhisperLoomSettings target, WhisperLoomSettings source, IEnumerable<string> paths)
    {
        var to = ToJson(target);
        var from = ToJson(source);
        foreach (var path in paths)
        {
            if (TryGet(from, path, out var value))
            {
                Set(to, path, value?.DeepClone());
            }
            else
            {
                Remove(to, path);
            }
        }
        return FromJson(to);
    }

    public static bool TryGet(JsonObject root, string path, out JsonNode? value)
    {
        value = null;
        JsonNode? node = root;
        foreach (var part in path.Split('.'))
        {
            if (node is not JsonObject o || !o.TryGetPropertyValue(part, out node))
            {
                return false;
            }
        }
        value = node;
        return true;
    }

    /// <summary>Setzt den Wert und legt fehlende Abschnitte an.</summary>
    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var o = root;
        foreach (var part in parts[..^1])
        {
            if (o[part] is not JsonObject child)
            {
                child = new JsonObject();
                o[part] = child;
            }
            o = child;
        }
        o[parts[^1]] = value;
    }

    public static void Remove(JsonObject root, string path)
    {
        var cut = path.LastIndexOf('.');
        if (cut < 0)
        {
            root.Remove(path);
        }
        else if (TryGet(root, path[..cut], out var parent) && parent is JsonObject o)
        {
            o.Remove(path[(cut + 1)..]);
        }
    }

    private static void Walk(JsonObject o, string prefix, Dictionary<string, JsonNode?> leaves)
    {
        foreach (var (name, value) in o)
        {
            if (value is JsonObject child && child.Count > 0)
            {
                Walk(child, prefix + name + ".", leaves);
            }
            else
            {
                leaves[prefix + name] = value;
            }
        }
    }
}
