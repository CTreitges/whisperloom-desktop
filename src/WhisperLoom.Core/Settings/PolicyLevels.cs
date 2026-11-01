using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Drei Stufen aus <c>policy.levels</c> (Pfad → <c>fixed</c>|<c>default</c>|<c>free</c>) auf eine Richtlinie anwenden, die
/// <c>lockSettings</c> schon als Fest enthält. Ein Eintrag in <c>levels</c> geht <c>lockSettings</c> für den Pfad und alles
/// darunter vor. Tastenkürzel sind nie Fest (gelten dann als Vorgabe, mit Warnung), <c>management.*</c> außer
/// <c>management.allowLeave</c> ist nie Fest und nie Vorgabe (ignoriert, mit Warnung).
/// </summary>
internal static class PolicyLevels
{
    public const string LevelsField = "levels";

    public static PolicySettings Resolve(PolicySettings fromLock, JsonObject policy, JsonObject settings, ICollection<string> warnings)
    {
        var leaves = SyncTree.Leaves(settings).Where(l => l.Key != "schemaVersion").ToList();
        var locked = new List<string>();
        var defaults = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in fromLock.Locked)
        {
            Fix(path, locked, defaults, leaves, warnings);
        }

        switch (policy[LevelsField])
        {
            case null:
                break;
            case JsonObject levels:
                foreach (var (path, node) in levels)
                {
                    var state = node is JsonValue v && v.TryGetValue<string>(out var key) ? ManagedStates.FromKey(key) : null;
                    if (state is null || string.IsNullOrWhiteSpace(path))
                    {
                        throw new SyncFormatException($"policy.levels.{path}: \"fixed\", \"default\" oder \"free\"");
                    }
                    locked.RemoveAll(l => ManagedPaths.IsAtOrBelow(l, path));
                    foreach (var k in defaults.Keys.Where(k => ManagedPaths.IsAtOrBelow(k, path)).ToList())
                    {
                        defaults.Remove(k);
                    }
                    if (state == ManagedState.Fixed)
                    {
                        Fix(path, locked, defaults, leaves, warnings);
                    }
                    else if (state == ManagedState.Default)
                    {
                        Default(path, defaults, leaves, warnings);
                    }
                }
                break;
            default:
                throw new SyncFormatException("policy.levels: Objekt Pfad → \"fixed\", \"default\" oder \"free\"");
        }

        // Tastenkürzel unter einem festen Abschnitt (z. B. "desktop") werden Vorgabe statt Fest.
        foreach (var (path, value) in leaves.Where(l => ManagedPaths.IsHotkey(l.Key) && locked.Any(k => ManagedPaths.IsAtOrBelow(l.Key, k))))
        {
            defaults.TryAdd(path, JsonSerializer.SerializeToElement(value));
        }
        return fromLock with
        {
            Locked = [.. locked.Distinct(StringComparer.OrdinalIgnoreCase)],
            Defaults = defaults.Count > 0 ? defaults : null,
        };
    }

    private static void Fix(string path, List<string> locked, Dictionary<string, JsonElement> defaults,
        List<KeyValuePair<string, JsonNode?>> leaves, ICollection<string> warnings)
    {
        if (ManagedPaths.IsHotkey(path))
        {
            warnings.Add($"policy: {path}: Tastenkürzel sind nie Fest, gilt als Vorgabe");
            Default(path, defaults, leaves, warnings);
        }
        else if (!ManagedPaths.CanBeFixed(path))
        {
            warnings.Add($"policy: {path}: nie Fest (nur {ManagedPaths.AllowLeave}), ignoriert");
        }
        else
        {
            locked.Add(path);
        }
    }

    private static void Default(string path, Dictionary<string, JsonElement> defaults, List<KeyValuePair<string, JsonNode?>> leaves, ICollection<string> warnings)
    {
        if (ManagedPaths.IsManagement(path) || ManagedPaths.IsAtOrBelow(path, SyncDocument.SecretsSection))
        {
            warnings.Add($"policy: {path}: kann keine Vorgabe sein, ignoriert");
            return;
        }
        var values = leaves.Where(l => ManagedPaths.IsAtOrBelow(l.Key, path)).ToList();
        if (values.Count == 0)
        {
            // Ein Tastenkürzel ohne Wert in der Datei bleibt einfach frei.
            if (ManagedPaths.IsHotkey(path))
            {
                return;
            }
            throw new SyncFormatException($"policy.levels.{path}: Vorgabe ohne Wert in den Einstellungen der Datei");
        }
        foreach (var (leaf, value) in values)
        {
            defaults[leaf] = JsonSerializer.SerializeToElement(value);
        }
    }
}
