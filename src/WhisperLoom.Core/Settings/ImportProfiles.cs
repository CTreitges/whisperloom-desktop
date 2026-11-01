using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// App-Profile (<c>desktop.profiles</c>) aus einer fremden Datei: „Enter danach“ (<c>pressEnter</c>) übernimmt ein Import
/// nur für ein Profil, das es mit Enter schon gab (gleicher Prozess und Titel-Regex). Sonst kommt das Profil ohne Enter
/// an — eine fremde Datei könnte sonst Text samt Enter in eine Konsole schicken (Security-Review K1). Felder liest die App
/// ohne Rücksicht auf Groß-/Kleinschreibung, deshalb hier ebenso (<see cref="KeyRoutes.Field"/>).
/// </summary>
internal static class ImportProfiles
{
    /// <summary>Schaltet in <paramref name="incoming"/> (Wurzel der Datei) Enter bei neuen Profilen aus.</summary>
    /// <returns>Name (bzw. Prozess) der betroffenen Profile.</returns>
    public static List<string> DisableNewEnter(JsonObject incoming, JsonObject current)
    {
        var disabled = new List<string>();
        if (Profiles(incoming) is not { } profiles)
        {
            return disabled;
        }
        var known = (Profiles(current) ?? []).OfType<JsonObject>().Where(p => PressesEnter(p) && Enabled(p)).Select(Identity).ToHashSet();
        foreach (var profile in profiles.OfType<JsonObject>().Where(p => PressesEnter(p) && !known.Contains(Identity(p))))
        {
            foreach (var name in Names(profile, "pressEnter"))
            {
                profile[name] = false;
            }
            var label = KeyRoutes.Field(profile, "name");
            disabled.Add(label.Length > 0 ? label : KeyRoutes.Field(profile, "process"));
        }
        return disabled;
    }

    /// <summary>
    /// <c>output.pressEnter</c> (W2, gilt für alle Profile mit „Erben“): <c>true</c> übernimmt ein Import nur, wenn es schon
    /// <c>true</c> war; sonst wird es in <paramref name="incoming"/> auf <c>false</c> gesetzt.
    /// </summary>
    /// <returns>true, wenn ausgeschaltet wurde.</returns>
    public static bool DisableNewGlobalEnter(JsonObject incoming, JsonObject current)
    {
        if (incoming["output"] is not JsonObject output || !PressesEnter(output)
            || (current["output"] is JsonObject now && PressesEnter(now)))
        {
            return false;
        }
        foreach (var name in Names(output, "pressEnter"))
        {
            output[name] = false;
        }
        return true;
    }

    private static JsonArray? Profiles(JsonObject root) =>
        root[SyncDocument.DesktopSection] is JsonObject desktop
            ? desktop.LastOrDefault(p => p.Key.Equals("profiles", StringComparison.OrdinalIgnoreCase)).Value as JsonArray
            : null;

    private static bool PressesEnter(JsonObject profile) =>
        profile.LastOrDefault(p => p.Key.Equals("pressEnter", StringComparison.OrdinalIgnoreCase)).Value is JsonValue v &&
        v.GetValueKind() == JsonValueKind.True;

    /// <summary>
    /// Profil aktiv (fehlt das Feld: ja). Ein abgeschaltetes Enter-Profil zählt nicht als bestätigt — der Import schaltete
    /// es sonst samt Enter wieder ein.
    /// </summary>
    private static bool Enabled(JsonObject profile) =>
        profile.LastOrDefault(p => p.Key.Equals("enabled", StringComparison.OrdinalIgnoreCase)).Value is not JsonValue v ||
        v.GetValueKind() != JsonValueKind.False;

    /// <summary>Wie <c>AppProfiles</c> vergleicht: Prozess ohne „.exe“ und ohne Groß-/Kleinschreibung, Titel-Regex wörtlich.</summary>
    private static (string Process, string Title) Identity(JsonObject profile)
    {
        var process = KeyRoutes.Field(profile, "process").Trim().ToLowerInvariant();
        return (process.EndsWith(".exe", StringComparison.Ordinal) ? process[..^4] : process, KeyRoutes.Field(profile, "titlePattern"));
    }

    private static List<string> Names(JsonObject o, string name) =>
        [.. o.Select(p => p.Key).Where(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))];
}
