using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Wohin ein gespeicherter Key geht (Security-Review K1). Die Ziele kommen aus derselben Auflösung wie beim Diktat
/// (<see cref="SettingsAccess"/>): Statt der Werte stehen Marken in den Keys, und der aufgelöste Zugang verrät, welcher
/// Key an welchem Origin landet — auch geerbte (Fallback oder KI ohne eigenen Key nutzt den der Erkennung).
/// </summary>
internal static class KeyRoutes
{
    private const string Marker = "\u0001key:";

    /// <summary>
    /// Gespeicherte Keys, die <paramref name="after"/> an ein Ziel schickt, das sie in <paramref name="before"/> nicht
    /// erreichten, oder die neu über einen manuellen Proxy gehen.
    /// </summary>
    public static IReadOnlyList<KeyDrop> Dropped(WhisperLoomSettings before, WhisperLoomSettings after, IReadOnlyCollection<string> storedKeys)
    {
        var stored = SecretNames.All.Where(storedKeys.Contains).ToList();
        var old = Targets(before, stored);
        var neu = Targets(after, stored);
        var proxy = ManualProxy(after);
        var proxyChanged = proxy is not null && proxy != ManualProxy(before);
        var drops = new List<KeyDrop>();
        foreach (var name in stored)
        {
            var added = neu[name].Except(old[name]).Order(StringComparer.Ordinal).ToList();
            if (added.Count > 0)
            {
                drops.Add(new KeyDrop(name, string.Join(", ", added)));
            }
            else if (proxyChanged && neu[name].Count > 0)
            {
                drops.Add(new KeyDrop(name, "Proxy " + proxy));
            }
        }
        return drops;
    }

    /// <summary>Je Key-Name die Origins, an die ihn die vier Zugänge schicken. Abgeschaltete Fallbacks zählen mit.</summary>
    private static Dictionary<string, HashSet<string>> Targets(WhisperLoomSettings settings, IReadOnlyList<string> stored)
    {
        var s = SettingsJson.Deserialize(SettingsJson.Serialize(settings));
        s.SttFallback.Enabled = true;
        s.LlmFallback.Enabled = true;
        string Key(string name) => stored.Contains(name) ? Marker + name : "";
        var keys = new AccessKeys(Key(SecretNames.Stt), Key(SecretNames.SttFallback), Key(SecretNames.Llm), Key(SecretNames.LlmFallback));
        ApiAccess?[] accesses = [SettingsAccess.Stt(s, keys), SettingsAccess.Llm(s, keys), SettingsAccess.SttFallback(s, keys), SettingsAccess.LlmFallback(s, keys)];

        var targets = SecretNames.All.ToDictionary(n => n, _ => new HashSet<string>(StringComparer.Ordinal));
        foreach (var a in accesses)
        {
            if (a is not null && a.ApiKey.StartsWith(Marker, StringComparison.Ordinal) && a.BaseUrl.Trim().Length > 0)
            {
                targets[a.ApiKey[Marker.Length..]].Add(Origin(a.BaseUrl));
            }
        }
        return targets;
    }

    /// <summary>Schema, Host und Port in Kleinbuchstaben; keine gültige URL bleibt, wie sie ist.</summary>
    internal static string Origin(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) ? $"{u.Scheme}://{u.Authority}".ToLowerInvariant() : url.Trim();

    /// <summary>Adresse des manuellen Proxys aus <c>desktop</c> (wie <c>HttpClients.Proxy</c> der App), sonst null.</summary>
    private static string? ManualProxy(WhisperLoomSettings s)
    {
        var desktop = SyncImport.Tree(s)[SyncDocument.DesktopSection] as JsonObject;
        var mode = (desktop?["proxyMode"] as JsonValue)?.TryGetValue<string>(out var m) == true ? m : "";
        var url = (desktop?["proxyUrl"] as JsonValue)?.TryGetValue<string>(out var p) == true ? p.Trim() : "";
        return mode == "manual" && Uri.TryCreate(url, UriKind.Absolute, out _) ? url : null;
    }
}
