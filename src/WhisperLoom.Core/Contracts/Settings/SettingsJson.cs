using System.Text.Json;

namespace WhisperLoom.Core.Contracts.Settings;

/// <summary>
/// Lesen und Schreiben von <see cref="WhisperLoomSettings"/> als JSON (camelCase, eingerückt).
/// Unbekannte Felder werden ignoriert, fehlende bekommen ihren Standardwert. Migration älterer
/// Schemata kommt mit dem ersten Schema-Wechsel (Phase 1).
/// </summary>
public static class SettingsJson
{
    // Doku: learn.microsoft.com/dotnet/standard/serialization/system-text-json/customize-properties (abgerufen 2026-10-30).
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // "stt": null in einer Datei wäre sonst ein null-Abschnitt trotz nicht-nullbarem Typ.
        RespectNullableAnnotations = true,
    };

    public static string Serialize(WhisperLoomSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <exception cref="JsonException">Kein gültiges JSON-Objekt.</exception>
    /// <exception cref="NotSupportedException">Datei einer neueren App-Version (<c>schemaVersion</c> größer als bekannt).</exception>
    public static WhisperLoomSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<WhisperLoomSettings>(json, Options)
            ?? throw new JsonException("Einstellungen sind leer (null).");
        if (settings.SchemaVersion > WhisperLoomSettings.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Einstellungen haben Schema {settings.SchemaVersion}, diese Version kennt nur bis {WhisperLoomSettings.CurrentSchemaVersion}.");
        }
        return settings;
    }
}
