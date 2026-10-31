using System.Text.Json;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Services;

/// <summary>
/// policy.json im Datenordner: die Richtlinie der IT aus <c>WhisperLoom provision</c>. Getrennt von settings.json,
/// damit Import, Export und das Einstellungsfenster sie nie überschreiben. Fehlt die Datei, gilt keine Richtlinie.
/// </summary>
public sealed class PolicyStore(string dataDir)
{
    public string FilePath { get; } = Path.Combine(dataDir, "policy.json");

    /// <exception cref="JsonException">Datei ist kein gültiges JSON.</exception>
    public PolicySettings Load() => File.Exists(FilePath) ? PolicyJson.Deserialize(File.ReadAllText(FilePath)) : PolicySettings.None;

    public void Save(PolicySettings policy)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, PolicyJson.Serialize(policy));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
