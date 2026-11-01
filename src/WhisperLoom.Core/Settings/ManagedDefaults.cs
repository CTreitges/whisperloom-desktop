using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Vorgaben der Firma (<see cref="PolicySettings.Defaults"/>) und die Änderungen des Nutzers daran
/// (<see cref="ManagementSettings.Overrides"/>). Regel: Ein Vorgabe-Pfad, dessen Wert von der Vorgabe abweicht, ist ein
/// User-Override. Eine neue Vorgabe (Provisioning, Abgleich) fährt nicht darüber; <see cref="Reset"/> setzt zurück.
/// </summary>
public static class ManagedDefaults
{
    /// <summary>Berechnet <see cref="ManagementSettings.Overrides"/> neu: alle Vorgabe-Pfade mit abweichendem Wert, sortiert.</summary>
    public static void TrackOverrides(WhisperLoomSettings settings, PolicySettings policy)
    {
        var tree = SyncImport.Tree(settings);
        settings.Management.Overrides =
        [
            .. (policy.Defaults ?? new Dictionary<string, JsonElement>())
                .Where(d => !policy.IsLocked(d.Key) && !Same(ManagedPaths.Find(tree, d.Key), d.Value))
                .Select(d => d.Key)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Hat der Nutzer den Pfad (oder etwas darunter bzw. darüber) gegen die Vorgabe geändert?</summary>
    public static bool IsOverridden(WhisperLoomSettings settings, string path) =>
        settings.Management.Overrides.Any(o => ManagedPaths.IsAtOrBelow(o, path) || ManagedPaths.IsAtOrBelow(path, o));

    /// <summary>
    /// „Auf Firmenvorgabe zurücksetzen“: setzt die Vorgabe-Werte unter <paramref name="path"/> (null = alle) und entfernt
    /// die Overrides. Gibt neue Einstellungen zurück; <paramref name="settings"/> bleibt unverändert.
    /// </summary>
    public static WhisperLoomSettings Reset(WhisperLoomSettings settings, PolicySettings policy, string? path = null)
    {
        var tree = SyncImport.Tree(settings);
        foreach (var (p, value) in policy.Defaults ?? new Dictionary<string, JsonElement>())
        {
            if ((path is null || ManagedPaths.IsAtOrBelow(p, path)) && !policy.IsLocked(p))
            {
                ManagedPaths.Set(tree, p, JsonNode.Parse(value.GetRawText()));
            }
        }
        var result = SettingsJson.Deserialize(tree.ToJsonString());
        TrackOverrides(result, policy);
        return result;
    }

    private static bool Same(JsonNode? node, JsonElement value) => JsonNode.DeepEquals(node, JsonNode.Parse(value.GetRawText()));
}
