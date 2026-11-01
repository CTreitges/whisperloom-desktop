using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Features.Selection;

/// <summary>
/// Das frühere „Markierung umschreiben“ (<c>desktop.hotkeyRewrite</c>, WLD-P5b) geht in „Markierung verarbeiten“ auf (W2-SEL):
/// eine gespeicherte Kombi wandert nach <c>selection.hotkeyCombo</c>, der alte Schlüssel fällt weg. Eine leere oder ungültige
/// alte Kombi galt als Standard – dann bleibt der neue Standard. Hat der Nutzer <c>selection.hotkeyCombo</c> schon geändert,
/// gewinnt seine Wahl.
/// </summary>
public static class SelectionMigration
{
    public const string OldKey = "hotkeyRewrite";

    /// <returns>true, wenn migriert wurde (der alte Schlüssel war da).</returns>
    public static bool Apply(WhisperLoomSettings settings, Func<string, bool> isValidHotkey)
    {
        if (settings.Extra is null || !settings.Extra.TryGetValue(Services.DesktopSettings.SectionName, out var element)
            || element.ValueKind != JsonValueKind.Object || JsonNode.Parse(element.GetRawText()) is not JsonObject desktop)
        {
            return false;
        }
        var keys = desktop.Select(p => p.Key).Where(k => k.Equals(OldKey, StringComparison.OrdinalIgnoreCase)).ToList();
        if (keys.Count == 0)
        {
            return false;
        }
        var old = desktop[keys[^1]] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        foreach (var key in keys)
        {
            desktop.Remove(key);
        }
        if (old is not null && isValidHotkey(old) && settings.Selection.HotkeyCombo == SelectionSettings.DefaultHotkey)
        {
            settings.Selection.HotkeyCombo = old;
        }
        settings.Extra[Services.DesktopSettings.SectionName] = JsonSerializer.SerializeToElement(desktop);
        return true;
    }
}
