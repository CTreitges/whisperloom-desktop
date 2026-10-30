using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Secrets;

/// <summary>Gemeinsame Prüfungen aller <see cref="ISecretStore"/>-Implementierungen. Meldungen nennen nie den Wert.</summary>
internal static class SecretStoreNames
{
    public static void Check(string name)
    {
        if (!SecretNames.All.Contains(name))
        {
            throw new ArgumentException($"Unbekannter Key-Name: {name}", nameof(name));
        }
    }

    public static void CheckValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Leerer Key — zum Entfernen DeleteAsync nutzen.", nameof(value));
        }
    }
}
