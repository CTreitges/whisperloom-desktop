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
                "import" => Import(path, dryRun),
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

    private int Import(string path, bool dryRun)
    {
        var preview = SyncImport.Preview(_settings.Load(), SyncDocument.Parse(File.ReadAllText(path)), _policy.Load());
        Report(preview.Warnings, preview.Errors);
        foreach (var c in preview.Changes)
        {
            output.WriteLine($"{(c.Locked ? "locked" : "change")} {c.Path}: {c.Old ?? "-"} -> {c.New ?? "-"}");
        }
        if (!preview.CanApply)
        {
            return 1;
        }
        if (!dryRun)
        {
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
