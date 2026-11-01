using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhisperLoom.App.Management;

/// <summary>
/// Ziel, für das ein verteilter Firmen-Key bestimmt ist: Anbieter und Adresse des Zugangs bzw. Adresse des eigenen
/// Anbieters (<c>custom:&lt;id&gt;</c>) zum Zeitpunkt des Abgleichs. Weicht der Zugang davon ab, gibt es den Key nicht.
/// </summary>
public sealed record KeyTarget(string ProviderId, string BaseUrl)
{
    public bool Matches(string providerId, string baseUrl) =>
        ProviderId == providerId && Normalized(BaseUrl) == Normalized(baseUrl);

    private static string Normalized(string url) => url.Trim().TrimEnd('/').ToLowerInvariant();
}

/// <summary>
/// Letzter Abgleich in <c>&lt;Datenordner&gt;/management-cache.json</c>: ETag, Proxy-Adresse, Intervall, die Ziele der
/// verteilten Keys (nur Namen und Adressen, nie Werte) und die Konfiguration ohne <c>secrets</c> (Keys nie auf die Platte
/// außerhalb des Secret-Stores). Werte und Richtlinie selbst stehen in settings.json/policy.json und gelten offline weiter
/// (fail-closed).
/// </summary>
public sealed class ManagementCache(string dataDir)
{
    public const int DefaultSyncIntervalSec = 900;

    public string FilePath { get; } = Path.Combine(dataDir, "management-cache.json");

    public string? ETag { get; private set; }

    public string? ProxyBaseUrl { get; private set; }

    public int SyncIntervalSec { get; private set; } = DefaultSyncIntervalSec;

    /// <summary>Key-Name (Zugang bzw. <c>custom:&lt;id&gt;</c>) → geplantes Ziel; leer ohne verteilte Keys.</summary>
    public IReadOnlyDictionary<string, KeyTarget> KeyTargets { get; private set; } = new Dictionary<string, KeyTarget>();

    /// <summary>Liest die Datei; fehlt sie oder ist sie kaputt, gilt „kein Abgleich bisher“ (also auch kein Key-Ziel).</summary>
    public ManagementCache Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                ETag = o["etag"]?.GetValue<string>();
                ProxyBaseUrl = o["proxyBaseUrl"]?.GetValue<string>();
                SyncIntervalSec = o["syncIntervalSec"]?.GetValue<int>() is int n and >= 60 ? n : DefaultSyncIntervalSec;
                KeyTargets = ReadTargets(o["keyTargets"] as JsonObject);
            }
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or FormatException)
        {
            ETag = null;
            KeyTargets = new Dictionary<string, KeyTarget>();
        }
        return this;
    }

    /// <summary>Speichert den Stand nach einem Abgleich mit 200.</summary>
    public void Save(string? etag, JsonObject config, IReadOnlyDictionary<string, KeyTarget>? keyTargets = null)
    {
        var copy = (JsonObject)config.DeepClone();
        copy.Remove("secrets");
        ETag = etag;
        ProxyBaseUrl = copy["proxy"]?["baseUrl"] is JsonValue v && v.TryGetValue<string>(out var url) ? url : null;
        SyncIntervalSec = copy["syncIntervalSec"] is JsonValue i && i.TryGetValue<int>(out var sec) && sec >= 60 ? sec : DefaultSyncIntervalSec;
        KeyTargets = keyTargets ?? new Dictionary<string, KeyTarget>();
        var targets = new JsonObject();
        foreach (var (name, t) in KeyTargets)
        {
            targets[name] = new JsonObject { ["providerId"] = t.ProviderId, ["baseUrl"] = t.BaseUrl };
        }
        var root = new JsonObject
        {
            ["etag"] = etag,
            ["proxyBaseUrl"] = ProxyBaseUrl,
            ["syncIntervalSec"] = SyncIntervalSec,
            ["keyTargets"] = targets,
            ["config"] = copy,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public void Clear()
    {
        File.Delete(FilePath);
        ETag = null;
        ProxyBaseUrl = null;
        SyncIntervalSec = DefaultSyncIntervalSec;
        KeyTargets = new Dictionary<string, KeyTarget>();
    }

    private static Dictionary<string, KeyTarget> ReadTargets(JsonObject? o)
    {
        var targets = new Dictionary<string, KeyTarget>(StringComparer.Ordinal);
        foreach (var (name, node) in o ?? [])
        {
            if (node?["providerId"] is JsonValue p && p.TryGetValue<string>(out var id) && node["baseUrl"] is JsonValue b && b.TryGetValue<string>(out var url))
            {
                targets[name] = new KeyTarget(id, url);
            }
        }
        return targets;
    }
}
