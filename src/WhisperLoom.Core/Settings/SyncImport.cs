using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>Eine Zeile der Import-Vorschau: Pfad, alter und neuer Wert als JSON-Text (null = fehlt).</summary>
/// <param name="Locked">Von IT gesperrt: wird nicht übernommen, <see cref="Old"/> bleibt.</param>
public sealed record SettingsChange(string Path, string? Old, string? New, bool Locked = false);

/// <summary>Ergebnis der Vorschau. Übernehmen nur, wenn <see cref="Errors"/> leer ist.</summary>
/// <param name="Settings">Die Einstellungen nach dem Import (Richtlinie schon angewandt); null bei Fehlern.</param>
public sealed record ImportPreview(
    WhisperLoomSettings? Settings,
    IReadOnlyList<SettingsChange> Changes,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool CanApply => Settings is not null && Errors.Count == 0;

    /// <summary>Gespeicherte Keys, die beim Übernehmen gelöscht werden: Der Import lenkte sie an ein neues Ziel.</summary>
    public IReadOnlyList<KeyDrop> DroppedKeys { get; init; } = [];

    /// <summary>
    /// App-Profile (<c>desktop.profiles</c>, Anzeige: Name bzw. Prozess), bei denen der Import „Enter danach“ ausgeschaltet
    /// hat: Enter in eine Konsole ist ein Befehl — das bestätigt der Nutzer im Profil selbst.
    /// </summary>
    public IReadOnlyList<string> EnterDisabled { get; init; } = [];

    /// <summary>Neue Ziele (Origin) für Diktattext — Erkennung, Textverbesserung, eingeschaltete Fallbacks —, auch ohne Key.</summary>
    public IReadOnlyList<string> DictationTargets { get; init; } = [];
}

/// <summary>Ein gespeicherter Key (<see cref="Contracts.SecretNames"/>), den ein Import an <paramref name="Target"/> lenken würde.</summary>
/// <param name="Target">Neues Ziel als Origin (<c>https://host</c>) bzw. <c>Proxy http://host:port</c>.</param>
public sealed record KeyDrop(string Name, string Target);

/// <summary>
/// Import einer Datei im Format v1 in die aktuellen Einstellungen: Felder der Datei ersetzen die aktuellen, fehlende
/// bleiben (Teil-Dateien sind erlaubt). Der Abschnitt <c>android</c> wird nur durchgereicht (für den Re-Export) und
/// nicht angezeigt. Gesperrte Pfade der Richtlinie bleiben unverändert. <c>secrets</c> und <c>policy</c> übernimmt nur
/// das Provisioning (<see cref="Provisioning"/>), hier gibt es dafür eine Warnung.
/// </summary>
public static class SyncImport
{
    /// <summary>Abschnitt der lokalen Verarbeitung (Desktop, <c>LocalSettings</c> der App).</summary>
    private const string LocalSection = "local";

    /// <summary>
    /// Vorschau eines Imports aus einer fremden Datei. Schutzregeln: <c>stt.vocabularyFile</c> wird nie übernommen;
    /// lenkt die Datei einen gespeicherten Key an ein neues Ziel (Adresse, Anbieter oder manueller Proxy), steht er in
    /// <see cref="ImportPreview.DroppedKeys"/> und der Aufrufer löscht ihn beim Übernehmen.
    /// </summary>
    /// <param name="storedKeys">Namen der gespeicherten Keys (<see cref="Contracts.SecretNames"/>).</param>
    public static ImportPreview Preview(WhisperLoomSettings current, SyncDocument file, PolicySettings policy, IReadOnlyCollection<string> storedKeys)
    {
        var warnings = new List<string>();
        if (file.Secrets.Count > 0 || file.Policy is not null)
        {
            warnings.Add("Keys und Richtlinie (secrets, policy) übernimmt nur „WhisperLoom provision“; hier ignoriert.");
        }
        // Gerätebezogene Ziele, die keine fremde Datei setzen darf: Vokabular-Datei und die Ollama-Adresse (lokale
        // Textverbesserung, ohne Key, aber mit Diktattext). „local“ liest die App ohne Rücksicht auf Groß-/Kleinschreibung.
        var settings = (JsonObject)file.Settings.DeepClone();
        (settings["stt"] as JsonObject)?.Remove("vocabularyFile");
        if (settings[LocalSection] is JsonObject local)
        {
            foreach (var name in local.Select(p => p.Key).Where(k => k.Equals("ollamaUrl", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                local.Remove(name);
            }
        }
        var enterDisabled = ImportProfiles.DisableNewEnter(settings, Tree(current));
        var preview = Merge(current, file with { Settings = settings }, policy, policy.IsLocked, warnings) with { EnterDisabled = enterDisabled };
        if (preview.Settings is null)
        {
            return preview;
        }
        var drops = KeyRoutes.Dropped(current, preview.Settings, storedKeys);
        // Einen von IT festgelegten Key löscht kein Import (wie „key delete“): dann lieber gar nicht übernehmen.
        var locked = drops.Where(d => policy.IsLocked("secrets." + d.Name))
            .Select(d => $"secrets.{d.Name}: von IT festgelegter Key ginge an {d.Target}").ToList();
        return locked.Count > 0
            ? preview with { Settings = null, Errors = [.. preview.Errors, .. locked] }
            : preview with { DroppedKeys = drops, DictationTargets = KeyRoutes.NewDictationTargets(current, preview.Settings) };
    }

    /// <summary>
    /// Eigene Änderungen aus dem Einstellungsfenster unter der Richtlinie: gesperrte Felder bleiben, fester Verlauf gilt,
    /// Verstöße sind Fehler. Ohne die Schutzregeln von <see cref="Preview"/> für fremde Dateien.
    /// </summary>
    public static ImportPreview ApplyPolicy(WhisperLoomSettings current, WhisperLoomSettings edited, PolicySettings policy) =>
        Merge(current, SyncDocument.Parse(SettingsJson.Serialize(edited)), policy, policy.IsLocked, []);

    /// <summary>Gemeinsamer Teil von Import und Provisioning.</summary>
    internal static ImportPreview Merge(WhisperLoomSettings current, SyncDocument file, PolicySettings policy, Func<string, bool> locked, List<string> warnings)
    {
        var before = Tree(current);
        var after = (JsonObject)before.DeepClone();
        var skipped = new List<string>();
        var incoming = (JsonObject)file.Settings.DeepClone();
        incoming.Remove("schemaVersion");
        SyncTree.Merge(after, incoming, "", locked, skipped);

        WhisperLoomSettings merged;
        try
        {
            merged = SettingsJson.Deserialize(after.ToJsonString());
        }
        catch (JsonException e)
        {
            // Pfad aus der Meldung von System.Text.Json (z. B. "$.history.size"), ohne den Wert.
            return new ImportPreview(null, [], [$"Falscher Typ bei {e.Path ?? "?"}"], warnings);
        }
        policy.Enforce(merged);
        var errors = new List<string>(SyncValidator.Validate(merged));
        errors.AddRange(policy.Violations(merged));

        var changes = Diff(before, Tree(merged));
        var oldLeaves = SyncTree.Leaves(before);
        var newLeaves = SyncTree.Leaves(incoming);
        changes.AddRange(skipped.Where(p => !IsAndroid(p)).Select(p =>
            new SettingsChange(p, SyncTree.Show(oldLeaves.GetValueOrDefault(p)), SyncTree.Show(newLeaves.GetValueOrDefault(p)), Locked: true)));
        changes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return new ImportPreview(errors.Count == 0 ? merged : null, changes, errors, warnings);
    }

    /// <summary>Geänderte Blätter (ohne den Abschnitt <c>android</c>), sortiert nach Pfad.</summary>
    internal static List<SettingsChange> Diff(JsonObject before, JsonObject after)
    {
        var a = SyncTree.Leaves(before);
        var b = SyncTree.Leaves(after);
        var changes = new List<SettingsChange>();
        foreach (var path in a.Keys.Union(b.Keys).Where(p => !IsAndroid(p)))
        {
            var hasOld = a.TryGetValue(path, out var oldValue);
            var hasNew = b.TryGetValue(path, out var newValue);
            // Leeres Objekt ↔ Objekt mit Inhalt erscheint nur über die neuen Blätter.
            if ((!hasOld && newValue is JsonObject { Count: 0 }) || (!hasNew && oldValue is JsonObject { Count: 0 }))
            {
                continue;
            }
            if (!JsonNode.DeepEquals(oldValue, newValue))
            {
                changes.Add(new SettingsChange(path, SyncTree.Show(oldValue), SyncTree.Show(newValue)));
            }
        }
        return changes;
    }

    internal static JsonObject Tree(WhisperLoomSettings s) => JsonNode.Parse(SettingsJson.Serialize(s))!.AsObject();

    private static bool IsAndroid(string path) =>
        path == SyncDocument.AndroidSection || path.StartsWith(SyncDocument.AndroidSection + ".", StringComparison.Ordinal);
}

/// <summary>Export im Format v1: die ganzen Einstellungen samt <c>desktop</c>/<c>android</c>, nie Keys, Richtlinie oder Vokabular-Pfad.</summary>
public static class SyncExport
{
    public static string ToJson(WhisperLoomSettings settings)
    {
        var tree = SyncImport.Tree(settings);
        // Keys stehen nie in settings.json; falls doch jemand den Abschnitt von Hand eingetragen hat: nicht exportieren.
        tree.Remove(SyncDocument.SecretsSection);
        tree.Remove(SyncDocument.PolicySection);
        (tree["stt"] as JsonObject)?.Remove("vocabularyFile");
        return tree.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
