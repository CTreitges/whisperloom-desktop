using System.Text.Json;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Cli;

/// <summary>
/// Die lokalen Verben <c>provision</c>, <c>import [--dry-run]</c> und <c>export</c>. Sie laufen im aufrufenden Prozess
/// (Keys nie über die IPC); eine laufende Instanz lädt danach neu (<see cref="Program"/>). Ausgaben nennen Pfade und
/// Zugangsnamen, nie Key-Werte. Die Eingabedatei wird nur gelesen — löschen muss sie das IT-Skript.
/// </summary>
internal sealed class SyncCommands(string dataDir, ISecretStore secrets, TextWriter output, TextWriter errors)
{
    private readonly SettingsStore _settings = new(dataDir);
    private readonly PolicyStore _policy = new(dataDir);

    /// <returns>Exit-Code: 0 ok, 1 Datei ungültig oder nicht schreibbar.</returns>
    public async Task<int> RunAsync(string verb, string path, bool dryRun, CancellationToken ct)
    {
        try
        {
            return verb switch
            {
                "provision" => await ProvisionAsync(path, ct),
                "import" => await ImportAsync(path, dryRun, ct),
                "export" => Export(path),
                _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
            };
        }
        catch (Exception e) when (e is SyncFormatException or NotSupportedException or IOException or UnauthorizedAccessException or JsonException)
        {
            errors.WriteLine($"error {e.Message}");
            return 1;
        }
    }

    private async Task<int> ProvisionAsync(string path, CancellationToken ct)
    {
        var plan = Provisioning.Plan(_settings.Load(), SyncDocument.Parse(File.ReadAllText(path)));
        Report(plan.Warnings, plan.Errors);
        if (!plan.CanApply)
        {
            return 1;
        }
        // Erst die Keys: scheitert die Ablage, bleiben Einstellungen und Richtlinie wie vorher.
        foreach (var (name, key) in plan.Secrets)
        {
            await secrets.SetAsync(name, key, ct);
        }
        _settings.Save(plan.Settings!);
        _policy.Save(plan.Policy);
        foreach (var line in plan.Summary())
        {
            output.WriteLine(line);
        }
        output.WriteLine($"ok provision ({secrets.Kind})");
        return 0;
    }

    private async Task<int> ImportAsync(string path, bool dryRun, CancellationToken ct)
    {
        var stored = await StoredKeysAsync(secrets, ct);
        var preview = SyncImport.Preview(_settings.Load(), SyncDocument.Parse(File.ReadAllText(path)), _policy.Load(), stored);
        Report([.. preview.Warnings, .. preview.DroppedKeys.Select(DropWarning),
            .. preview.EnterDisabled.Select(p => $"Profil {p}: Enter danach ausgeschaltet (fremde Datei); bei Bedarf im Profil selbst einschalten")], preview.Errors);
        foreach (var c in preview.Changes)
        {
            output.WriteLine($"{(c.Locked ? "locked" : "change")} {c.Path}: {c.Old ?? "-"} -> {c.New ?? "-"}");
        }
        foreach (var d in preview.DroppedKeys)
        {
            output.WriteLine($"change secrets.{d.Name}: stored -> deleted ({d.Target})");
        }
        if (!preview.CanApply)
        {
            return 1;
        }
        if (!dryRun)
        {
            // Erst die Keys: scheitert das Löschen, bleiben die Einstellungen wie vorher (kein Key an ein neues Ziel).
            await DropKeysAsync(secrets, preview, ct);
            _settings.Save(preview.Settings!);
        }
        output.WriteLine(dryRun ? "ok import dry-run" : $"ok import {preview.Changes.Count(c => !c.Locked)} changes");
        return 0;
    }

    private int Export(string path)
    {
        WhisperLoomSettings settings = _settings.Load();
        File.WriteAllText(path, SyncExport.ToJson(settings));
        output.WriteLine($"ok export {path}");
        return 0;
    }

    /// <summary>Namen der Keys, die im Speicher liegen (für die Schutzregel des Imports).</summary>
    internal static async Task<IReadOnlyCollection<string>> StoredKeysAsync(ISecretStore store, CancellationToken ct)
    {
        var stored = new List<string>();
        foreach (var name in SecretNames.All)
        {
            if (!string.IsNullOrWhiteSpace(await store.GetAsync(name, ct)))
            {
                stored.Add(name);
            }
        }
        return stored;
    }

    internal static async Task DropKeysAsync(ISecretStore store, ImportPreview preview, CancellationToken ct)
    {
        foreach (var drop in preview.DroppedKeys)
        {
            await store.DeleteAsync(drop.Name, ct);
        }
    }

    private static string DropWarning(KeyDrop d) => $"Key {d.Name} wird gelöscht: der Import würde ihn an {d.Target} schicken; danach neu eingeben";

    private void Report(IReadOnlyList<string> warnings, IReadOnlyList<string> errorList)
    {
        foreach (var w in warnings)
        {
            errors.WriteLine($"warn {w}");
        }
        foreach (var e in errorList)
        {
            errors.WriteLine($"error {e}");
        }
    }
}
