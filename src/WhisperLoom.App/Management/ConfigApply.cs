using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Management;

/// <summary>Ergebnis einer Server-Konfiguration: neue Werte, Richtlinie und die Firmen-Keys (nur Verteilt-Modus).</summary>
/// <param name="CompanyKeys">Zugangsname → Key; leer im Proxy-Modus. Erscheint nie in Meldungen.</param>
public sealed record ConfigPlan(WhisperLoomSettings? Settings, PolicySettings Policy, string KeyMode,
    IReadOnlyDictionary<string, string> CompanyKeys, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool CanApply => Settings is not null && Errors.Count == 0;

    public override string ToString() => $"ConfigPlan(keyMode={KeyMode}, keys={CompanyKeys.Count}, errors={Errors.Count})";
}

/// <summary>
/// Antwort von <c>GET /api/v1/config</c> (<c>docs/server-api.md</c> §3.3) anwenden wie eine Provisioning-Datei
/// (<see cref="Provisioning.Plan"/>): Werte, drei Stufen, Keys. Zusätzlich:
/// <list type="bullet">
/// <item>Proxy-Modus: <c>secrets.stt</c> und <c>secrets.llm</c> sind Fest (das Geräte-Token ersetzt sie, kein Key-Feld).</item>
/// <item>Verteilt-Modus: jeder verteilte Key ist Fest (<c>secrets.&lt;zugang&gt;</c>).</item>
/// <item>Gerätezustand bleibt (<c>management.serverUrl</c>, <c>deviceId</c>); Zustand <c>approved</c>, Firmenname und
/// Key-Modus kommen vom Server.</item>
/// </list>
/// </summary>
public static class ConfigApply
{
    public static ConfigPlan Plan(WhisperLoomSettings current, JsonObject config, long nowMs)
    {
        var keyMode = config["keyMode"] is JsonValue k && k.TryGetValue<string>(out var mode) && mode == "distribute" ? "distribute" : "proxy";
        var root = config["settings"] is JsonObject s ? (JsonObject)s.DeepClone() : new JsonObject { ["schemaVersion"] = 1 };
        root.Remove(SyncDocument.SecretsSection);
        root[SyncDocument.PolicySection] = config["policy"] is JsonObject p ? p.DeepClone() : new JsonObject();
        if (keyMode == "distribute" && config["secrets"] is JsonObject secrets)
        {
            root[SyncDocument.SecretsSection] = secrets.DeepClone();
        }

        ProvisioningPlan plan;
        try
        {
            plan = Provisioning.Plan(current, SyncDocument.Parse(root.ToJsonString()));
        }
        catch (Exception e) when (e is SyncFormatException or NotSupportedException)
        {
            return new ConfigPlan(null, PolicySettings.None, keyMode, new Dictionary<string, string>(), [e.Message], []);
        }
        if (!plan.CanApply)
        {
            return new ConfigPlan(null, PolicySettings.None, keyMode, new Dictionary<string, string>(), plan.Errors, plan.Warnings);
        }

        var keys = keyMode == "distribute" ? plan.Secrets : new Dictionary<string, string>();
        IEnumerable<string> keyPaths = keyMode == "proxy" ? [SecretNames.Stt, SecretNames.Llm] : keys.Keys;
        var policy = plan.Policy with
        {
            Locked = [.. plan.Policy.Locked.Concat(keyPaths.Select(n => "secrets." + n)).Distinct(StringComparer.OrdinalIgnoreCase)],
        };

        var settings = plan.Settings!;
        var m = settings.Management;
        m.ServerUrl = current.Management.ServerUrl;
        m.DeviceId = current.Management.DeviceId;
        m.Code = "";
        m.State = "approved";
        m.KeyMode = keyMode;
        m.LastSync = nowMs;
        if (config["tenantName"] is JsonValue t && t.TryGetValue<string>(out var tenant) && tenant.Length > 0)
        {
            m.TenantName = tenant;
        }
        return new ConfigPlan(settings, policy, keyMode, keys, [], plan.Warnings);
    }

    /// <summary>Darf der Nutzer die Anbindung lösen? Nur wenn <c>management.allowLeave</c> nicht Fest auf <c>false</c> steht.</summary>
    public static bool CanLeave(WhisperLoomSettings settings, PolicySettings policy) =>
        !policy.IsLocked(ManagedPaths.AllowLeave) || settings.Management.AllowLeave;
}
