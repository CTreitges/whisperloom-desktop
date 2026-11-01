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
public static class KeyRoutes
{
    private const string Marker = "\u0001key:";

    /// <summary>Ziel eines Drops, wenn es den eigenen Anbieter danach nicht mehr gibt (Prüfrunde 1 K-1).</summary>
    public const string RemovedEntry = KeyDrop.EntryRemovedTarget;

    /// <summary>
    /// Gespeicherte Keys, die <paramref name="after"/> an ein Ziel schickt, das sie in <paramref name="before"/> nicht
    /// erreichten, oder die neu über einen manuellen Proxy gehen.
    /// </summary>
    public static IReadOnlyList<KeyDrop> Dropped(WhisperLoomSettings before, WhisperLoomSettings after, IReadOnlyCollection<string> storedKeys)
    {
        var stored = SecretNames.All.Where(storedKeys.Contains).Concat(storedKeys.Where(SecretNames.IsProviderKey).Order(StringComparer.Ordinal)).ToList();
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
            else if (SecretNames.IsProviderKey(name) && ProviderDirectory.For(after).Find(name) is null)
            {
                // Eintrag entfernt (oder Key schon verwaist): Key verwerfen, sonst erbte ihn ein späterer Eintrag
                // gleicher ID mit anderem Host (Prüfrunde 1 K-1).
                drops.Add(new KeyDrop(name, RemovedEntry));
            }
        }
        return drops;
    }

    /// <summary>
    /// Origins, an die <paramref name="settings"/> den Key <paramref name="name"/> schicken würde, wenn nur er gespeichert
    /// wäre (W2-FIX K-1: Ziel, für das ein eigener Key unter Firmen-Verwaltung gespeichert wurde). Dieselbe Auflösung wie
    /// <see cref="Dropped"/>; Erben gibt es nur bei gleichem Server, die Menge ist also nie kleiner als im Diktat.
    /// </summary>
    public static IReadOnlySet<string> OriginsOf(WhisperLoomSettings settings, string name) => Targets(settings, [name])[name];

    /// <summary>
    /// Je Key-Name die Origins, an die ihn die vier Zugänge (und „Zugang prüfen“) schicken. Abgeschaltete Fallbacks zählen mit.
    /// </summary>
    private static Dictionary<string, HashSet<string>> Targets(WhisperLoomSettings settings, IReadOnlyList<string> stored)
    {
        var s = SettingsJson.Deserialize(SettingsJson.Serialize(settings));
        s.SttFallback.Enabled = true;
        s.LlmFallback.Enabled = true;
        string Key(string name) => stored.Contains(name) ? Marker + name : "";
        // Keys eigener Anbieter (W2) gehören zum Eintrag: Ziel ist dessen Adresse.
        var providerKeys = stored.Where(SecretNames.IsProviderKey).ToDictionary(n => n, n => Marker + n, StringComparer.Ordinal);
        var keys = new AccessKeys(Key(SecretNames.Stt), Key(SecretNames.SttFallback), Key(SecretNames.Llm), Key(SecretNames.LlmFallback), providerKeys);
        ApiAccess?[] accesses = [SettingsAccess.Stt(s, keys), SettingsAccess.Llm(s, keys), SettingsAccess.SttFallback(s, keys), SettingsAccess.LlmFallback(s, keys)];

        var targets = SecretNames.All.Concat(providerKeys.Keys).ToDictionary(n => n, _ => new HashSet<string>(StringComparer.Ordinal));
        foreach (var a in accesses)
        {
            if (a is not null && a.ApiKey.StartsWith(Marker, StringComparison.Ordinal) && a.BaseUrl.Trim().Length > 0)
            {
                targets[a.ApiKey[Marker.Length..]].Add(Origin(a.BaseUrl));
            }
        }
        // „Zugang prüfen“ auf der Seite „Anbieter“ schickt den Key eines Eintrags an dessen Adresse, auch ungenutzt.
        var directory = ProviderDirectory.For(s);
        foreach (var name in providerKeys.Keys)
        {
            if (directory.Find(name) is { BaseUrl.Length: > 0 } entry)
            {
                targets[name].Add(Origin(entry.BaseUrl));
            }
        }
        // „wie Erkennung“: Das Diktat nutzt den llm-Key nicht, „Zugang prüfen“ schickt ihn aber an die Erkennung.
        var stt = accesses[0]!;
        if (stored.Contains(SecretNames.Llm) && (string.IsNullOrWhiteSpace(s.Llm.ProviderId) || s.Llm.ProviderId == LlmSettings.Same) &&
            stt.BaseUrl.Trim().Length > 0)
        {
            targets[SecretNames.Llm].Add(Origin(stt.BaseUrl));
        }
        return targets;
    }

    /// <summary>
    /// Origins, an die <paramref name="after"/> Diktattext schickt und <paramref name="before"/> nicht — Erkennung,
    /// Textverbesserung und eingeschaltete Fallbacks, unabhängig von Keys (Review WLD-FIX R-1: die Antwort eines fremden
    /// Textmodells wird getippt).
    /// </summary>
    public static IReadOnlyList<string> NewDictationTargets(WhisperLoomSettings before, WhisperLoomSettings after) =>
        [.. DictationTargets(after).Except(DictationTargets(before)).Order(StringComparer.Ordinal)];

    private static HashSet<string> DictationTargets(WhisperLoomSettings s)
    {
        var none = new AccessKeys();
        ApiAccess?[] accesses = [SettingsAccess.Stt(s, none), SettingsAccess.Llm(s, none), SettingsAccess.SttFallback(s, none), SettingsAccess.LlmFallback(s, none)];
        return [.. accesses.Where(a => a is not null && a.BaseUrl.Trim().Length > 0).Select(a => Origin(a!.BaseUrl))];
    }

    /// <summary>Schema, Host und Port in Kleinbuchstaben; keine gültige URL bleibt, wie sie ist.</summary>
    internal static string Origin(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) ? $"{u.Scheme}://{u.Authority}".ToLowerInvariant() : url.Trim();

    /// <summary>Origin des manuellen Proxys aus <c>desktop</c> (wie <c>HttpClients.Proxy</c> der App), sonst null.</summary>
    private static string? ManualProxy(WhisperLoomSettings s)
    {
        var desktop = SyncImport.Tree(s)[SyncDocument.DesktopSection] as JsonObject;
        var url = Field(desktop, "proxyUrl").Trim();
        return Field(desktop, "proxyMode") == "manual" && Uri.TryCreate(url, UriKind.Absolute, out _) ? Origin(url) : null;
    }

    /// <summary>
    /// Textfeld so, wie die App den Abschnitt liest (<c>JsonSerializerDefaults.Web</c>): Name ohne Rücksicht auf
    /// Groß-/Kleinschreibung, bei Dopplung gilt das letzte. "" = fehlt oder kein Text.
    /// </summary>
    internal static string Field(JsonObject? section, string name) =>
        section?.LastOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value is JsonValue v &&
        v.TryGetValue<string>(out var text) ? text : "";
}
