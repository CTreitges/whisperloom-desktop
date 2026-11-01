using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Was <c>WhisperLoom provision &lt;datei&gt;</c> schreibt: Einstellungen, Keys je Zugang und die Richtlinie.
/// Nur übernehmen, wenn <see cref="Errors"/> leer ist; dann alles, sonst nichts.
/// </summary>
/// <param name="Secrets">Zugangsname (<see cref="SecretNames"/>) → Key. Erscheint nie in Meldungen.</param>
public sealed record ProvisioningPlan(
    WhisperLoomSettings? Settings,
    IReadOnlyDictionary<string, string> Secrets,
    PolicySettings Policy,
    IReadOnlyList<SettingsChange> Changes,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool CanApply => Settings is not null && Errors.Count == 0;

    /// <summary>
    /// Keys eigener Anbieter (<c>custom:&lt;id&gt;</c>), die der Aufrufer vor dem Speichern löscht: Die Datei ändert den Host
    /// des Eintrags oder entfernt ihn und liefert keinen neuen Key dafür (W2-LOCAL Prüfrunde 2 R-K1-1). Nicht gespeicherte
    /// Namen zu löschen schadet nicht.
    /// </summary>
    public IReadOnlyList<KeyDrop> DroppedKeys { get; init; } = [];

    /// <summary>Kurzbericht ohne Werte: geänderte Pfade, gesetzte Zugänge, Sperren.</summary>
    public IEnumerable<string> Summary()
    {
        foreach (var c in Changes)
        {
            yield return $"setting {c.Path}";
        }
        foreach (var name in Secrets.Keys.Order(StringComparer.Ordinal))
        {
            yield return $"key {name}";
        }
        foreach (var drop in DroppedKeys)
        {
            yield return $"key-drop {drop.Name}";
        }
        foreach (var path in Policy.Locked)
        {
            yield return $"locked {path}";
        }
        foreach (var path in (Policy.Defaults?.Keys ?? []).Order(StringComparer.Ordinal))
        {
            yield return $"default {path}";
        }
        if (Policy.HistoryEnabled is { } h)
        {
            yield return $"policy historyEnabled={h.ToString().ToLowerInvariant()}";
        }
        if (Policy.UpdatesEnabled is { } u)
        {
            yield return $"policy updatesEnabled={u.ToString().ToLowerInvariant()}";
        }
        if (Policy.AllowedProviders is { } a)
        {
            yield return $"policy allowedProviders={string.Join(",", a)}";
        }
    }
}

/// <summary>
/// Provisioning durch die IT: Die Datei setzt Einstellungen (wie ein Import, aber ohne Rücksicht auf alte Sperren),
/// Keys und eine neue Richtlinie, die die alte ersetzt. Keys in <c>secrets</c> stehen je Anbieter-ID (gilt für jeden
/// Zugang mit diesem Anbieter) oder je Zugangsname (<c>stt</c>, <c>stt_fallback</c>, <c>llm</c>, <c>llm_fallback</c>,
/// hat Vorrang) — Letzteres braucht es für zwei eigene Server mit verschiedenen Keys.
/// </summary>
public static class Provisioning
{
    /// <param name="storedKeys">Gespeicherte Key-Namen (für genaue Warnungen); null = alle Einträge vorher und nachher als
    /// möglicherweise gespeichert ansehen (z. B. Server-Konfiguration ohne Zugriff auf die Ablage).</param>
    public static ProvisioningPlan Plan(WhisperLoomSettings current, SyncDocument file, IReadOnlyCollection<string>? storedKeys = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        PolicySettings policy;
        file = file with { Settings = ManagementInput(file.Settings, warnings) };
        try
        {
            // Die Key-Pfade kommen erst nach der Zuordnung dazu (unten).
            policy = file.Policy is null ? PolicySettings.None : PolicySettings.FromProvisioning(file.Policy, file.Settings, warnings);
        }
        catch (SyncFormatException e)
        {
            return new ProvisioningPlan(null, new Dictionary<string, string>(), PolicySettings.None, [], [e.Message], []);
        }

        // Vorgaben fahren nicht über Änderungen des Nutzers (User-Override).
        var preview = SyncImport.Merge(current, file, policy,
            p => policy.StateOf(p) == ManagedState.Default && ManagedDefaults.IsOverridden(current, p), warnings);
        errors.AddRange(preview.Errors);
        foreach (var path in policy.Locked.Where(p => !KnownPath(p)))
        {
            errors.Add($"policy.lockSettings: Pfad unbekannt: {path}");
        }
        if (file.Policy?[PolicyLevels.LevelsField] is JsonObject levels)
        {
            foreach (var path in levels.Select(l => l.Key).Where(p => !KnownPath(p)))
            {
                errors.Add($"policy.levels: Pfad unbekannt: {path}");
            }
        }
        if (preview.Settings is not null)
        {
            ManagedDefaults.TrackOverrides(preview.Settings, policy);
        }
        foreach (var id in policy.AllowedProviders ?? [])
        {
            // W2: „local“ und Einträge „custom:<id>“/„local:<name>“ sind ebenfalls gültige Freigaben.
            if (ProviderCatalog.Find(id) is null && id != "local" && !SecretNames.IsProviderKey(id) &&
                !(id.StartsWith(LocalServerSource.LocalPrefix, StringComparison.Ordinal) && id.Length > LocalServerSource.LocalPrefix.Length))
            {
                errors.Add($"policy.allowedProviders: Anbieter unbekannt: {id}");
            }
        }

        var secrets = preview.Settings is null ? new Dictionary<string, string>() : MapSecrets(preview.Settings, file.Secrets, errors, warnings);
        if (file.Policy?["lockSettings"]?.GetValueKind() is System.Text.Json.JsonValueKind.True && secrets.Count > 0)
        {
            policy = policy with { Locked = [.. policy.Locked, .. secrets.Keys.Order(StringComparer.Ordinal).Select(n => "secrets." + n)] };
        }
        var drops = preview.Settings is null ? [] : ProviderKeyDrops(current, preview.Settings, file, secrets, storedKeys);
        warnings.AddRange(drops.Select(d => d.EntryRemoved
            ? $"secrets.{d.Name}: Eintrag entfernt, gespeicherter Key wird gelöscht"
            : $"secrets.{d.Name}: Adresse geändert ({d.Target}), gespeicherter Key wird gelöscht"));
        return new ProvisioningPlan(errors.Count == 0 ? preview.Settings : null, secrets, policy, preview.Changes, errors, warnings) { DroppedKeys = drops };
    }

    /// <summary>
    /// Wie beim Import (<see cref="KeyRoutes.Dropped"/>), aber nur für Keys eigener Anbieter: Host-Wechsel oder entfernter
    /// Eintrag → Key löschen, außer die Datei liefert ihn selbst (<c>secrets."custom:&lt;id&gt;"</c>).
    /// </summary>
    private static List<KeyDrop> ProviderKeyDrops(
        WhisperLoomSettings current, WhisperLoomSettings after, SyncDocument file, IReadOnlyDictionary<string, string> secrets, IReadOnlyCollection<string>? storedKeys)
    {
        var candidates = storedKeys?.Where(SecretNames.IsProviderKey).ToList()
            ?? [.. current.Providers.Custom.Select(c => ProvidersSettings.CustomPrefix + c.Id).Concat(SyncImport.ProviderKeyNames(file))
                .Where(SecretNames.IsProviderKey).Distinct(StringComparer.Ordinal)];
        return [.. KeyRoutes.Dropped(current, after, candidates).Where(d => SecretNames.IsProviderKey(d.Name) && !secrets.ContainsKey(d.Name))];
    }

    private static Dictionary<string, string> MapSecrets(WhisperLoomSettings s, IReadOnlyDictionary<string, string> secrets, List<string> errors, List<string> warnings)
    {
        var byAccess = new Dictionary<string, string>(StringComparer.Ordinal);
        var accesses = new (string Name, string ProviderId)[]
        {
            (SecretNames.Stt, s.Stt.ProviderId),
            (SecretNames.SttFallback, s.SttFallback.ProviderId),
            (SecretNames.Llm, s.Llm.ProviderId), // "same" passt zu keinem Anbieter: nutzt den Key der Erkennung.
            (SecretNames.LlmFallback, s.LlmFallback.ProviderId),
        };
        // W2: Key eines eigenen Anbieters („custom:<id>“) gehört zum Eintrag, nicht zu einem Zugang.
        foreach (var (name, key) in secrets.Where(e => SecretNames.IsProviderKey(e.Key)))
        {
            if (s.Providers.Custom.Any(c => ProvidersSettings.CustomPrefix + c.Id == name))
            {
                byAccess[name] = key;
            }
            else
            {
                errors.Add($"secrets.{name}: kein Eintrag in providers.custom");
            }
        }
        foreach (var (name, key) in secrets.Where(e => !SecretNames.All.Contains(e.Key) && !SecretNames.IsProviderKey(e.Key)))
        {
            if (ProviderCatalog.Find(name) is null)
            {
                errors.Add($"secrets.{name}: weder Anbieter-ID noch Zugang ({string.Join(", ", SecretNames.All)})");
                continue;
            }
            var targets = accesses.Where(a => a.ProviderId == name).Select(a => a.Name).ToList();
            if (targets.Count == 0)
            {
                warnings.Add($"secrets.{name}: kein Zugang nutzt diesen Anbieter, Key nicht gespeichert");
            }
            foreach (var target in targets)
            {
                byAccess[target] = key;
            }
        }
        foreach (var (name, key) in secrets.Where(e => SecretNames.All.Contains(e.Key)))
        {
            byAccess[name] = key;
        }
        return byAccess;
    }

    /// <summary>Felder unter <c>management</c>, die eine Datei bzw. der Server setzen darf; der Rest ist Gerätezustand.</summary>
    private static readonly string[] ManagementInputs = ["serverUrl", "code", "allowLeave"];

    /// <summary>Entfernt Gerätezustand (<c>management.deviceId</c>, <c>state</c>, <c>overrides</c>, …) aus der Datei, mit Warnung.</summary>
    private static JsonObject ManagementInput(JsonObject settings, List<string> warnings)
    {
        var copy = (JsonObject)settings.DeepClone();
        foreach (var section in copy.Where(p => p.Key.Equals(ManagedPaths.ManagementSection, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).OfType<JsonObject>())
        {
            foreach (var name in section.Select(p => p.Key).Where(k => !ManagementInputs.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                section.Remove(name);
                warnings.Add($"management.{name}: Gerätezustand, von der Datei nicht setzbar; ignoriert");
            }
        }
        return copy;
    }

    /// <summary>Pfad der Einstellungen (Abschnitt oder Feld), ein Key-Pfad oder etwas in desktop/android.</summary>
    private static bool KnownPath(string path)
    {
        if (ManagedPaths.IsAtOrBelow(path, SyncDocument.DesktopSection) || ManagedPaths.IsAtOrBelow(path, SyncDocument.AndroidSection)
            || ManagedPaths.IsAtOrBelow(path, "local") || path.StartsWith("llm.stageModels.", StringComparison.Ordinal))
        {
            return true;
        }
        if (path.StartsWith("secrets.", StringComparison.Ordinal))
        {
            return SecretNames.All.Contains(path["secrets.".Length..]) || SecretNames.IsProviderKey(path["secrets.".Length..]);
        }
        var leaves = SyncTree.Leaves(SyncImport.Tree(new WhisperLoomSettings())).Keys;
        return leaves.Any(l => l == path || l.StartsWith(path + ".", StringComparison.Ordinal));
    }
}
