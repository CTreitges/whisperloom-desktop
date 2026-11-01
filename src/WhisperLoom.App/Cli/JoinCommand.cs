using System.Text.Json;
using WhisperLoom.App.Management;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Cli;

/// <summary>
/// <c>WhisperLoom join --server &lt;url&gt; --code &lt;code&gt; [--device-name &lt;n&gt;]</c> (W2-ENR): Gerät ohne Klicks bei der
/// Firma anmelden, z. B. per Software-Verteilung. Exit 0 bei „wartet auf Freigabe“ und „freigegeben“ (dann gleich mit
/// erstem Abgleich), 1 bei Fehler. Ausgaben nennen nie Code oder Token.
/// </summary>
internal static class JoinCommand
{
    public static async Task<int> RunAsync(string dataDir, string server, string code, string deviceName, ISecretStore user,
        ISecretStore company, HttpClient http, TextWriter output, TextWriter errors, CancellationToken ct)
    {
        FileManagedTarget target;
        try
        {
            target = new FileManagedTarget(new SettingsStore(dataDir), new PolicyStore(dataDir));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or IOException)
        {
            errors.WriteLine($"error settings.json bzw. policy.json unlesbar ({e.GetType().Name})");
            return 1;
        }
        var service = new ManagementService(target, user, company, ManagementFactory.Api(http), new ManagementCache(dataDir).Load(),
            new AppLog(dataDir));
        SyncOutcome outcome;
        try
        {
            outcome = await service.JoinAsync(server, code, deviceName.Length > 0 ? deviceName : EnrollInput.DefaultDeviceName(), ct);
        }
        catch (ArgumentException e)
        {
            errors.WriteLine($"error {Localization.Loc.Instance[e.Message]}");
            return 1;
        }
        catch (ManagementException e)
        {
            errors.WriteLine($"error {e.Error}: {ManagementTexts.Error(e.Error)}");
            return 1;
        }
        catch (ManagementOfflineException)
        {
            errors.WriteLine($"error offline: {ManagementTexts.Error("offline")}");
            return 1;
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            errors.WriteLine($"error {e.GetType().Name}");
            return 1;
        }
        var m = target.Settings.Management;
        output.WriteLine($"ok join state={m.State} device={m.DeviceId} tenant={m.TenantName} sync={outcome.ToString().ToLowerInvariant()}");
        return m.State is "pending" or "approved" ? 0 : 1;
    }
}
