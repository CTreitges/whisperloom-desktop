using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Management;

/// <summary>
/// Ziele der eigenen Zugangs-Keys unter Firmen-Verwaltung (W2-FIX K-1, Verteidigung in der Tiefe) in
/// <c>&lt;Datenordner&gt;/own-key-targets.json</c>: Key-Name → Origins, an die der Key ging, als der Nutzer ihn gespeichert hat
/// bzw. beim Beitritt. Nur Namen und Origins, nie Werte. Der <see cref="CompanySecretStore"/> gibt einen eigenen Key unter
/// Verwaltung nur heraus, wenn sein aktuelles Ziel darin liegt. Wird bei jedem Zugriff frisch gelesen: CLI (<c>join</c>,
/// <c>key set</c>, <c>provision</c>) und laufende App teilen die Datei.
/// </summary>
public sealed class OwnKeyTargets(string dataDir)
{
    public string FilePath { get; } = Path.Combine(dataDir, "own-key-targets.json");

    /// <summary>Gespeicherte Ziele; fehlt die Datei oder ist sie kaputt: keine (fail-closed: kein eigener Key unter Verwaltung).</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Load()
    {
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                foreach (var (name, node) in o)
                {
                    if (node is JsonArray origins)
                    {
                        result[name] = origins.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null)
                            .OfType<string>().ToHashSet(StringComparer.Ordinal);
                    }
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>Darf der eigene Key <paramref name="name"/> mit <paramref name="settings"/> hinaus? Ohne Ziel immer (geht nirgends hin).</summary>
    public bool Allows(WhisperLoomSettings settings, string name)
    {
        var origins = KeyRoutes.OriginsOf(settings, name);
        return origins.Count == 0 || (Load().TryGetValue(name, out var recorded) && origins.All(recorded.Contains));
    }

    /// <summary>Ziel von <paramref name="name"/> unter <paramref name="settings"/> festhalten (Nutzer hat den Key gerade gespeichert).</summary>
    public void Record(WhisperLoomSettings settings, string name)
    {
        var all = Load().ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        all[name] = KeyRoutes.OriginsOf(settings, name);
        Write(all);
    }

    /// <summary>Alle Ziele ersetzen: Stand der gespeicherten Zugangs-Keys <paramref name="names"/> unter <paramref name="settings"/> (Beitritt).</summary>
    public void Snapshot(WhisperLoomSettings settings, IEnumerable<string> names) =>
        Write(names.Where(SecretNames.All.Contains).ToDictionary(n => n, n => KeyRoutes.OriginsOf(settings, n), StringComparer.Ordinal));

    /// <summary>Firma verlassen: ohne Verwaltung gibt es nichts zurückzuhalten.</summary>
    public void Clear() => File.Delete(FilePath);

    private void Write(IReadOnlyDictionary<string, IReadOnlySet<string>> all)
    {
        var o = new JsonObject();
        foreach (var (name, origins) in all.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            o[name] = new JsonArray([.. origins.Order(StringComparer.Ordinal).Select(s => (JsonNode)s)]);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
