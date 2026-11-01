using System.Text.Json.Nodes;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Regeln für Einstellungs-Pfade unter der Richtlinie (<c>docs/settings-format.md</c>, „Richtlinie: drei Stufen“).
/// Pfade vergleichen immer ohne Rücksicht auf Groß-/Kleinschreibung, weil die App <c>desktop</c> und <c>local</c> so liest.
/// </summary>
public static class ManagedPaths
{
    public const string ManagementSection = "management";

    /// <summary>Einziger Pfad unter <c>management</c>, den eine Firma Fest setzen darf.</summary>
    public const string AllowLeave = "management.allowLeave";

    /// <summary>Gleich <paramref name="parent"/> oder darunter (Präfix nur an Pfadgrenzen).</summary>
    public static bool IsAtOrBelow(string path, string parent) =>
        path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tastenkürzel: ein Pfad-Teil beginnt mit <c>hotkey</c> (<c>desktop.hotkeyHold</c>, <c>desktop.hotkeysEnabled</c>, <c>selection.hotkey</c>).</summary>
    public static bool IsHotkey(string path) =>
        path.Split('.').Any(s => s.StartsWith("hotkey", StringComparison.OrdinalIgnoreCase));

    public static bool IsManagement(string path) => IsAtOrBelow(path, ManagementSection);

    /// <summary>
    /// Darf der Pfad Fest sein? Nein für Tastenkürzel und für <c>management.*</c> außer <see cref="AllowLeave"/>. Der ganze
    /// Abschnitt <c>management</c> zählt als sperrbar, weil er <see cref="AllowLeave"/> enthält; <see cref="PolicySettings.IsLocked"/>
    /// fragt die Blätter einzeln.
    /// </summary>
    public static bool CanBeFixed(string path) =>
        !IsHotkey(path) && (!IsManagement(path) || path.Equals(ManagementSection, StringComparison.OrdinalIgnoreCase) || IsAtOrBelow(path, AllowLeave));

    /// <summary>Knoten am Pfad; je Teil der letzte Name, der ohne Groß-/Kleinschreibung passt (wie die App liest).</summary>
    public static JsonNode? Find(JsonObject root, string path)
    {
        JsonNode? node = root;
        foreach (var part in path.Split('.'))
        {
            if (node is not JsonObject o)
            {
                return null;
            }
            node = o.LastOrDefault(p => p.Key.Equals(part, StringComparison.OrdinalIgnoreCase)).Value;
        }
        return node;
    }

    /// <summary>Setzt den Wert am Pfad; fehlende Abschnitte entstehen, andere Schreibweisen desselben Namens fallen weg.</summary>
    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var o = root;
        for (var i = 0; i < parts.Length; i++)
        {
            var existing = o.LastOrDefault(p => p.Key.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            var name = existing.Key ?? parts[i];
            foreach (var other in o.Select(p => p.Key).Where(k => k != name && k.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                o.Remove(other);
            }
            if (i == parts.Length - 1)
            {
                o[name] = value?.DeepClone();
                return;
            }
            if (existing.Value is not JsonObject child)
            {
                child = new JsonObject();
                o[name] = child;
            }
            o = child;
        }
    }
}
