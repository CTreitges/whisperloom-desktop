using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Secrets;

/// <summary>Gemeinsame Prüfungen aller <see cref="ISecretStore"/>-Implementierungen. Meldungen nennen nie den Wert.</summary>
internal static class SecretStoreNames
{
    public static void Check(string name)
    {
        // W2-ENR: Key des Firmen-Proxys (company:proxy) ist kein gespeicherter Wert – CompanySecretStore liefert das Geräte-Token;
        // erlaubt, damit ein Speicher ohne Firmen-Anbindung einfach „nichts gespeichert“ antwortet.
        if (!SecretNames.All.Contains(name) && name != SecretNames.Device && !SecretNames.IsProviderKey(name)
            && name != Core.Api.CompanyProvider.EntryId)
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
