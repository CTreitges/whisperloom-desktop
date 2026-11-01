using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Management;

/// <summary>Ergebnis einer Server-Konfiguration: neue Werte, Richtlinie und die Firmen-Keys (nur Verteilt-Modus).</summary>
/// <param name="CompanyKeys">Zugangsname → Key; leer im Proxy-Modus. Erscheint nie in Meldungen.</param>
/// <param name="DroppedKeys">Eigene Keys des Nutzers, die nach der Konfiguration an ein anderes Ziel gingen (eigener Anbieter mit
/// neuer Adresse bzw. ohne Eintrag): vor dem Übernehmen löschen (W2-LOCAL R-K1-1).</param>
public sealed record ConfigPlan(WhisperLoomSettings? Settings, PolicySettings Policy, string KeyMode,
    IReadOnlyDictionary<string, string> CompanyKeys, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool CanApply => Settings is not null && Errors.Count == 0;

    public IReadOnlyList<KeyDrop> DroppedKeys { get; init; } = [];

    public override string ToString() => $"ConfigPlan(keyMode={KeyMode}, keys={CompanyKeys.Count}, drops={DroppedKeys.Count}, errors={Errors.Count})";
}

/// <summary>
/// Antwort von <c>GET /api/v1/config</c> (<c>docs/server-api.md</c> §3.3) anwenden wie eine Provisioning-Datei
/// (<see cref="Provisioning.Plan"/>): Werte, drei Stufen, Keys. Zusätzlich:
/// <list type="bullet">
/// <item>Proxy-Modus: Zugänge, die der Server als <c>custom</c> mit der Proxy-Adresse setzt, werden zum Eintrag
/// <see cref="CompanyProvider.EntryId"/> (Key = Geräte-Token, nur an diesen Eintrag); <c>secrets.stt</c> und
/// <c>secrets.llm</c> sind Fest (kein Key-Feld).</item>
/// <item>Verteilt-Modus: jeder verteilte Key ist Fest (<c>secrets.&lt;zugang&gt;</c>); Zugänge auf den Firmen-Proxy fallen
/// zurück auf <c>custom</c> mit Proxy-Adresse (<see cref="Detach"/>).</item>
/// <item>Gerätezustand bleibt (<c>management.serverUrl</c>, <c>deviceId</c>); Zustand <c>approved</c>, Firmenname und
/// Key-Modus kommen vom Server.</item>
/// </list>
/// </summary>
public static class ConfigApply
{
    private static readonly string[] Sections = ["stt", "sttFallback", "llm", "llmFallback"];

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
            // Ohne storedKeys gelten alle eigenen Anbieter als „Key womöglich gespeichert“: lieber einmal zu viel löschen.
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

        var warnings = plan.Warnings.ToList();
        var allowed = plan.Policy.AllowedProviders;
        if (keyMode == "proxy")
        {
            var proxy = config["proxy"]?["baseUrl"] is JsonValue u && u.TryGetValue<string>(out var url) ? url : "";
            if (CompanyProvider.Targets(CompanyProvider.ProxyBaseUrl(m.ServerUrl), proxy))
            {
                Attach(settings, proxy);
                if (allowed is not null && allowed.Contains(ProviderCatalog.CustomId))
                {
                    allowed = [.. allowed, CompanyProvider.EntryId];
                }
            }
            else
            {
                // Der Server meldet eine andere Basis-Adresse (WL_BASE_URL) als die, mit der das Gerät verbunden ist.
                warnings.Add("proxy.baseUrl: passt nicht zur Server-Adresse des Geräts; Firmen-Proxy nicht genutzt");
            }
        }
        else
        {
            Detach(settings);
        }

        var keys = keyMode == "distribute" ? plan.Secrets : new Dictionary<string, string>();
        IEnumerable<string> keyPaths = keyMode == "proxy" ? [SecretNames.Stt, SecretNames.Llm] : keys.Keys;
        var policy = plan.Policy with
        {
            Locked = [.. plan.Policy.Locked.Concat(keyPaths.Select(n => "secrets." + n)).Distinct(StringComparer.OrdinalIgnoreCase)],
            AllowedProviders = allowed,
        };
        return new ConfigPlan(settings, policy, keyMode, keys, [], warnings) { DroppedKeys = plan.DroppedKeys };
    }

    /// <summary>Zugänge <c>custom</c> + Proxy-Adresse → Eintrag „Firma“ (Adresse kommt dann aus dem Eintrag).</summary>
    private static void Attach(WhisperLoomSettings s, string proxy)
    {
        foreach (var section in Sections)
        {
            var (providerId, baseUrl, set) = Access(s, section);
            if (providerId == ProviderCatalog.CustomId && CompanyProvider.Targets(proxy, baseUrl))
            {
                set(CompanyProvider.EntryId, "");
            }
        }
    }

    /// <summary>
    /// Zugänge auf den Eintrag „Firma“ wieder auf <c>custom</c> mit Proxy-Adresse (Verlassen, Wechsel auf Verteilt-Modus):
    /// ohne Firmen-Anbindung gäbe es den Eintrag nicht mehr, die Einstellungen wären sonst ungültig. Ein Key geht dorthin
    /// nur, wenn der Nutzer selbst einen einträgt.
    /// </summary>
    public static void Detach(WhisperLoomSettings s)
    {
        var proxy = s.Management.ServerUrl.Length > 0 ? CompanyProvider.ProxyBaseUrl(s.Management.ServerUrl) : "";
        foreach (var section in Sections)
        {
            var (providerId, _, set) = Access(s, section);
            if (providerId == CompanyProvider.EntryId)
            {
                set(ProviderCatalog.CustomId, proxy);
            }
        }
    }

    private static (string ProviderId, string BaseUrl, Action<string, string> Set) Access(WhisperLoomSettings s, string section) => section switch
    {
        "stt" => (s.Stt.ProviderId, s.Stt.BaseUrl, (id, url) => { s.Stt.ProviderId = id; s.Stt.BaseUrl = url; }),
        "sttFallback" => (s.SttFallback.ProviderId, s.SttFallback.BaseUrl, (id, url) => { s.SttFallback.ProviderId = id; s.SttFallback.BaseUrl = url; }),
        "llm" => (s.Llm.ProviderId, s.Llm.BaseUrl, (id, url) => { s.Llm.ProviderId = id; s.Llm.BaseUrl = url; }),
        _ => (s.LlmFallback.ProviderId, s.LlmFallback.BaseUrl, (id, url) => { s.LlmFallback.ProviderId = id; s.LlmFallback.BaseUrl = url; }),
    };

    /// <summary>Darf der Nutzer die Anbindung lösen? Nur wenn <c>management.allowLeave</c> nicht Fest auf <c>false</c> steht.</summary>
    public static bool CanLeave(WhisperLoomSettings settings, PolicySettings policy) =>
        !policy.IsLocked(ManagedPaths.AllowLeave) || settings.Management.AllowLeave;
}
