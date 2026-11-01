using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Services;

/// <summary>
/// policy.json im Datenordner: die Richtlinie der IT aus <c>WhisperLoom provision</c> bzw. vom Firmen-Server. Getrennt von
/// settings.json, damit Import, Export und das Einstellungsfenster sie nie überschreiben. Fehlt die Datei, gilt keine
/// Richtlinie. Dazu <c>policy-fixed.json</c> (W2-ENR): die Werte der festen Pfade zum Zeitpunkt des Provisionings bzw.
/// Abgleichs. <see cref="EnforceFixed"/> setzt sie beim Start und Neuladen wieder ein, damit eine lokal geänderte
/// settings.json Fest nicht aushebelt (fail-closed, auch offline).
/// </summary>
public sealed class PolicyStore(string dataDir)
{
    public string FilePath { get; } = Path.Combine(dataDir, "policy.json");

    /// <summary>Werte der festen Pfade: Blatt-Pfad → Wert.</summary>
    public string FixedPath { get; } = Path.Combine(dataDir, "policy-fixed.json");

    /// <exception cref="JsonException">Datei ist kein gültiges JSON.</exception>
    public PolicySettings Load() => File.Exists(FilePath) ? PolicyJson.Deserialize(File.ReadAllText(FilePath)) : PolicySettings.None;

    public void Save(PolicySettings policy) => Write(FilePath, PolicyJson.Serialize(policy));

    /// <summary>Richtlinie speichern und die Werte ihrer festen Pfade aus <paramref name="settings"/> festhalten.</summary>
    public void Save(PolicySettings policy, WhisperLoomSettings settings)
    {
        Save(policy);
        var fixedValues = new JsonObject();
        foreach (var (path, value) in SettingsMerge.Leaves(SettingsMerge.ToJson(settings)))
        {
            if (policy.IsLocked(path))
            {
                fixedValues[path] = value?.DeepClone();
            }
        }
        Write(FixedPath, fixedValues.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Richtlinie und festgehaltene Werte entfernen (Firma verlassen); fehlende Dateien sind kein Fehler.</summary>
    public void Clear()
    {
        File.Delete(FilePath);
        File.Delete(FixedPath);
    }

    /// <summary>
    /// Setzt die festgehaltenen Werte der Pfade, die <paramref name="policy"/> noch sperrt, in eine Kopie von
    /// <paramref name="settings"/>. Ohne Datei bleibt alles, wie es ist.
    /// </summary>
    /// <returns>Einstellungen (dieselbe Instanz, wenn nichts zu korrigieren war) und Anzahl korrigierter Pfade; -1 = Datei unlesbar.</returns>
    public (WhisperLoomSettings Settings, int Changed) EnforceFixed(WhisperLoomSettings settings, PolicySettings policy)
    {
        if (!policy.IsActive || !File.Exists(FixedPath))
        {
            return (settings, 0);
        }
        JsonObject? stored;
        try
        {
            stored = JsonNode.Parse(File.ReadAllText(FixedPath)) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            stored = null;
        }
        if (stored is null)
        {
            return (settings, -1);
        }
        var tree = SettingsMerge.ToJson(settings);
        var changed = 0;
        foreach (var (path, value) in stored)
        {
            if (policy.IsLocked(path) && !JsonNode.DeepEquals(ManagedPaths.Find(tree, path), value))
            {
                ManagedPaths.Set(tree, path, value);
                changed++;
            }
        }
        return changed == 0 ? (settings, 0) : (SettingsMerge.FromJson(tree), changed);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
