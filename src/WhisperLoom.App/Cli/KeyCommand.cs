using System.Text.Json;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Cli;

/// <summary>
/// <c>key set &lt;name&gt;</c> (Wert von stdin) und <c>key delete &lt;name&gt;</c>. Der Wert wird nie ausgegeben.
/// Von IT gesperrte Keys (<c>secrets.&lt;name&gt;</c> in der Richtlinie) lehnt der Befehl ab, wie das Einstellungsfenster.
/// </summary>
internal static class KeyCommand
{
    /// <summary>Mit der Richtlinie aus <paramref name="dataDir"/>; eine kaputte policy.json lehnt den Befehl ab (wie <c>update</c>).</summary>
    public static async Task<int> RunAsync(string action, string name, Func<string> readValue, ISecretStore store, string dataDir,
        TextWriter output, TextWriter errors, CancellationToken ct)
    {
        PolicySettings policy;
        try
        {
            policy = new PolicyStore(dataDir).Load();
        }
        catch (JsonException)
        {
            errors.WriteLine("error policy.json ist unlesbar; key set|delete abgebrochen (Datei reparieren oder neu provisionieren).");
            return 1;
        }
        var code = await RunAsync(action, name, readValue, store, policy, output, errors, ct);
        if (code == 0 && action == "set" && SecretNames.All.Contains(name) && TryLoad(dataDir) is { } settings
            && Management.CompanySecretStore.IsManaged(settings.Management))
        {
            // Unter Firmen-Verwaltung gilt der Key für das Ziel, das gerade eingestellt ist (W2-FIX K-1). Ohne lesbare
            // settings.json bleibt er unter Verwaltung zurückgehalten (fail-closed).
            new Management.OwnKeyTargets(dataDir).Record(settings, name, policy);
            if (Management.CompanySecretStore.CompanyChoseTarget(settings, policy, name))
            {
                errors.WriteLine($"warn Ziel von {name} legt die Firma fest: der eigene Key wird unter Verwaltung nicht genutzt und beim Verlassen gelöscht.");
            }
        }
        return code;
    }

    private static Core.Contracts.Settings.WhisperLoomSettings? TryLoad(string dataDir)
    {
        try
        {
            return new SettingsStore(dataDir).Load();
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <returns>Exit-Code: 0 ok, 1 Fehler oder gesperrt.</returns>
    public static async Task<int> RunAsync(string action, string name, Func<string> readValue, ISecretStore store, PolicySettings policy,
        TextWriter output, TextWriter errors, CancellationToken ct)
    {
        if (policy.IsLocked("secrets." + name))
        {
            errors.WriteLine($"error Key {name} ist von IT festgelegt (Richtlinie) und lässt sich nicht ändern.");
            return 1;
        }
        try
        {
            if (action == "set")
            {
                await store.SetAsync(name, readValue().Trim(), ct);
            }
            else
            {
                await store.DeleteAsync(name, ct);
            }
            output.WriteLine($"ok {action} {name} ({store.Kind})");
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            errors.WriteLine(e.Message);
            return 1;
        }
    }
}
