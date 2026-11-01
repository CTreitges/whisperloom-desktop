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

    /// <summary>TD-A4: Pfade, deren abgeschaltetes OpenAI-Modell auf dem zusammengeführten Stand zum Nachfolger wurde.</summary>
    public IReadOnlyList<string> RetiredModels { get; init; } = [];

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
public sealed record KeyDrop(string Name, string Target)
{
    /// <summary>Ziel, wenn es den eigenen Anbieter danach nicht mehr gibt (W2-LOCAL Prüfrunde 1 K-1).</summary>
    public const string EntryRemovedTarget = "(Eintrag entfernt)";

    /// <summary>Der Key gehörte zu einem eigenen Anbieter, den es danach nicht mehr gibt.</summary>
    public bool EntryRemoved => Target == EntryRemovedTarget;
}

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
        // W2: eigene Whisper-Dateien und lokale Server sind gerätebezogen (Pfad bzw. Ziel für Diktattext).
        (settings["offline"] as JsonObject)?.Remove("customModels");
        if (settings[LocalSection] is JsonObject local)
        {
            RemoveIgnoringCase(local, "ollamaUrl");
            RemoveIgnoringCase(local, "servers");
            RemoveIgnoringCase(local, "textServer");
        }
        // management.* ist Gerätezustand: nie importiert.
        if (RemoveIgnoringCase(settings, ManagedPaths.ManagementSection))
        {
            warnings.Add("management (Firmen-Anbindung) übernimmt kein Import; hier ignoriert.");
        }
        var currentTree = Tree(current);
        var enterDisabled = ImportProfiles.DisableNewEnter(settings, currentTree);
        if (ImportProfiles.DisableNewGlobalEnter(settings, currentTree))
        {
            enterDisabled.Add("output.pressEnter");
        }
        var preview = Merge(current, file with { Settings = settings }, policy, policy.IsLocked, warnings) with { EnterDisabled = enterDisabled };
        if (preview.Settings is null)
        {
            return preview;
        }
        ManagedDefaults.TrackOverrides(preview.Settings, policy);
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
    /// <remarks>Weicht ein Vorgabe-Pfad danach von der Firmenvorgabe ab, steht er in <c>management.overrides</c>.</remarks>
    public static ImportPreview ApplyPolicy(WhisperLoomSettings current, WhisperLoomSettings edited, PolicySettings policy)
    {
        var preview = Merge(current, SyncDocument.Parse(SettingsJson.Serialize(edited)), policy, policy.IsLocked, []);
        if (preview.Settings is not null)
        {
            ManagedDefaults.TrackOverrides(preview.Settings, policy);
        }
        return preview;
    }

    /// <summary>
    /// Key-Namen (<c>custom:&lt;id&gt;</c>) der eigenen Anbieter in einer Datei: Der Aufrufer prüft sie zusätzlich zu den
    /// aktuellen Einträgen auf gespeicherte Keys, damit ein verwaister Key nie an einen neu importierten Eintrag gleicher
    /// ID geht (Prüfrunde 1 K-1).
    /// </summary>
    public static IReadOnlyList<string> ProviderKeyNames(SyncDocument file) =>
        file.Settings["providers"] is JsonObject providers && providers["custom"] is JsonArray custom
            ? [.. custom.OfType<JsonObject>().Select(e => e["id"] is JsonValue v && v.TryGetValue<string>(out var id) ? ProvidersSettings.CustomPrefix + id : "")
                .Where(Contracts.SecretNames.IsProviderKey).Distinct(StringComparer.Ordinal)]
            : [];

    /// <summary>Entfernt alle Schreibweisen von <paramref name="name"/>; true, wenn es eine gab.</summary>
    internal static bool RemoveIgnoringCase(JsonObject o, string name)
    {
        var names = o.Select(p => p.Key).Where(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var n in names)
        {
            o.Remove(n);
        }
        return names.Count > 0;
    }

    /// <summary>Gemeinsamer Teil von Import und Provisioning.</summary>
    internal static ImportPreview Merge(WhisperLoomSettings current, SyncDocument file, PolicySettings policy, Func<string, bool> locked, List<string> warnings)
    {
        var before = Tree(current);
        var after = (JsonObject)before.DeepClone();
        var skipped = new List<string>();
        var incoming = (JsonObject)file.Settings.DeepClone();
        incoming.Remove("schemaVersion");
        SyncTree.Merge(after, incoming, "", locked, skipped);
        // TD-A4: erst hier ist der wirksame Anbieter bekannt (Teil-Dateien ohne providerId → Anbieter des Geräts).
        var retired = RetiredModels.ApplyFull(after);

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
        return new ImportPreview(errors.Count == 0 ? merged : null, changes, errors, warnings) { RetiredModels = retired };
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

/// <summary>
/// Export im Format v1: die ganzen Einstellungen samt <c>desktop</c>/<c>android</c>, nie Keys, Richtlinie, Vokabular-Pfad,
/// <c>management</c> (Firmen-Anbindung) und gerätebezogene Listen (<c>offline.customModels</c>, <c>local.servers</c>).
/// </summary>
public static class SyncExport
{
    public static string ToJson(WhisperLoomSettings settings)
    {
        var tree = SyncImport.Tree(settings);
        // Keys stehen nie in settings.json; falls doch jemand den Abschnitt von Hand eingetragen hat: nicht exportieren.
        tree.Remove(SyncDocument.SecretsSection);
        tree.Remove(SyncDocument.PolicySection);
        (tree["stt"] as JsonObject)?.Remove("vocabularyFile");
        SyncImport.RemoveIgnoringCase(tree, ManagedPaths.ManagementSection);
        (tree["offline"] as JsonObject)?.Remove("customModels");
        foreach (var local in tree.Where(p => p.Key.Equals("local", StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).OfType<JsonObject>())
        {
            SyncImport.RemoveIgnoringCase(local, "servers");
            SyncImport.RemoveIgnoringCase(local, "textServer");
        }
        return tree.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
