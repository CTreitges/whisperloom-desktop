using WhisperLoom.App.Services;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Updates;

/// <summary>
/// CLI <c>update [--feed &lt;pfad|url&gt;]</c>: der Aufruf selbst ist die Bestätigung. Prüft, lädt und installiert ohne
/// Neustart; danach meldet <c>--version</c> die neue Version. Kanäle (W3-CHAN): Stabil plus der eingestellte
/// Update-Kanal (ein ausdrücklicher Aufruf zählt wie „Vorabversionen automatisch installieren“); nur höhere Versionen.
/// </summary>
internal static class UpdateCommand
{
    /// <summary>Exit-Code, wenn die Richtlinie Updates abschaltet.</summary>
    public const int DisabledExitCode = 4;

    public static async Task<int> RunAsync(IUpdateClient client, PolicySettings policy, AppLog log, TextWriter output, TextWriter error, CancellationToken ct,
        UpdatePreferences? preferences = null)
    {
        if (policy.UpdatesEnabled == false)
        {
            log.Event("update.check", ("status", "disabled-by-policy"), ("source", "cli"));
            error.WriteLine("update: von der IT abgeschaltet (policy updatesEnabled=false).");
            return DisabledExitCode;
        }
        if (!client.IsInstalled)
        {
            error.WriteLine("update: nicht installiert; nur eine Setup- bzw. AppImage-Installation aktualisiert sich.");
            return 1;
        }
        try
        {
            var channels = UpdateChannels.AutoChannels((preferences ?? UpdatePreferences.Default).Channel, prereleaseAuto: true);
            var found = await UpdatePreferences.NewestAsync(client, channels, UpdateChannels.CurrentOs, ct);
            if (found is null)
            {
                output.WriteLine($"update: aktuell ({client.CurrentVersion}).");
                return 0;
            }
            await client.DownloadAsync(found, downgrade: false, ct);
            log.Event("update.apply", ("version", found.Version), ("channel", found.Channel), ("source", "cli"));
            output.WriteLine($"update: {client.CurrentVersion} -> {found.Version} ({found.Channel}), wird installiert.");
            output.Flush();
            client.ApplyAndExit(found);
            return 0;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.Event("update.check", ("status", "failed"), ("errorClass", e.GetType().Name), ("source", "cli"));
            error.WriteLine($"update: {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }
}
