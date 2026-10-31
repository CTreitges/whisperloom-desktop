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
        foreach (var path in Policy.Locked)
        {
            yield return $"locked {path}";
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
    public static ProvisioningPlan Plan(WhisperLoomSettings current, SyncDocument file)
    {
        var errors = new List<string>();
        PolicySettings policy;
        var provisioned = SyncTree.Leaves(file.Settings).Keys.Where(p => p != "schemaVersion").ToList();
        try
        {
            // Die Key-Pfade kommen erst nach der Zuordnung dazu (unten).
            policy = file.Policy is null ? PolicySettings.None : PolicySettings.FromProvisioning(file.Policy, provisioned);
        }
        catch (SyncFormatException e)
        {
            return new ProvisioningPlan(null, new Dictionary<string, string>(), PolicySettings.None, [], [e.Message], []);
        }

        var warnings = new List<string>();
        var preview = SyncImport.Merge(current, file, policy, _ => false, warnings);
        errors.AddRange(preview.Errors);
        foreach (var path in policy.Locked.Where(p => !KnownPath(p)))
        {
            errors.Add($"policy.lockSettings: Pfad unbekannt: {path}");
        }
        foreach (var id in policy.AllowedProviders ?? [])
        {
            if (ProviderCatalog.Find(id) is null)
            {
                errors.Add($"policy.allowedProviders: Anbieter unbekannt: {id}");
            }
        }

        var secrets = preview.Settings is null ? new Dictionary<string, string>() : MapSecrets(preview.Settings, file.Secrets, errors, warnings);
        if (file.Policy?["lockSettings"]?.GetValueKind() is System.Text.Json.JsonValueKind.True && secrets.Count > 0)
        {
            policy = policy with { Locked = [.. policy.Locked, .. secrets.Keys.Order(StringComparer.Ordinal).Select(n => "secrets." + n)] };
        }
        return new ProvisioningPlan(errors.Count == 0 ? preview.Settings : null, secrets, policy, preview.Changes, errors, warnings);
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
        foreach (var (name, key) in secrets.Where(e => !SecretNames.All.Contains(e.Key)))
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

    /// <summary>Pfad der Einstellungen (Abschnitt oder Feld), ein Key-Pfad oder etwas in desktop/android.</summary>
    private static bool KnownPath(string path)
    {
        if (path is SyncDocument.DesktopSection or SyncDocument.AndroidSection || path.StartsWith("desktop.", StringComparison.Ordinal)
            || path.StartsWith("android.", StringComparison.Ordinal) || path.StartsWith("llm.stageModels.", StringComparison.Ordinal))
        {
            return true;
        }
        if (path.StartsWith("secrets.", StringComparison.Ordinal))
        {
            return SecretNames.All.Contains(path["secrets.".Length..]);
        }
        var leaves = SyncTree.Leaves(SyncImport.Tree(new WhisperLoomSettings())).Keys;
        return leaves.Any(l => l == path || l.StartsWith(path + ".", StringComparison.Ordinal));
    }
}
