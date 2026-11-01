using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhisperLoom.App.Management;

/// <summary>
/// Letzter Abgleich in <c>&lt;Datenordner&gt;/management-cache.json</c>: ETag, Proxy-Adresse, Intervall und die Konfiguration
/// ohne <c>secrets</c> (Keys nie auf die Platte außerhalb des Secret-Stores). Werte und Richtlinie selbst stehen in
/// settings.json/policy.json und gelten offline weiter (fail-closed).
/// </summary>
public sealed class ManagementCache(string dataDir)
{
    public const int DefaultSyncIntervalSec = 900;

    public string FilePath { get; } = Path.Combine(dataDir, "management-cache.json");

    public string? ETag { get; private set; }

    public string? ProxyBaseUrl { get; private set; }

    public int SyncIntervalSec { get; private set; } = DefaultSyncIntervalSec;

    /// <summary>Liest die Datei; fehlt sie oder ist sie kaputt, gilt „kein Abgleich bisher“.</summary>
    public ManagementCache Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                ETag = o["etag"]?.GetValue<string>();
                ProxyBaseUrl = o["proxyBaseUrl"]?.GetValue<string>();
                SyncIntervalSec = o["syncIntervalSec"]?.GetValue<int>() is int n and >= 60 ? n : DefaultSyncIntervalSec;
            }
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or FormatException)
        {
            ETag = null;
        }
        return this;
    }

    /// <summary>Speichert den Stand nach einem Abgleich mit 200.</summary>
    public void Save(string? etag, JsonObject config)
    {
        var copy = (JsonObject)config.DeepClone();
        copy.Remove("secrets");
        ETag = etag;
        ProxyBaseUrl = copy["proxy"]?["baseUrl"] is JsonValue v && v.TryGetValue<string>(out var url) ? url : null;
        SyncIntervalSec = copy["syncIntervalSec"] is JsonValue i && i.TryGetValue<int>(out var sec) && sec >= 60 ? sec : DefaultSyncIntervalSec;
        var root = new JsonObject
        {
            ["etag"] = etag,
            ["proxyBaseUrl"] = ProxyBaseUrl,
            ["syncIntervalSec"] = SyncIntervalSec,
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
    }
}
