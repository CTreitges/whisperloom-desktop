using WhisperLoom.App.Services;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Updates;

/// <summary>Ergebnis einer Update-Prüfung (auch als <c>status</c> im Log).</summary>
public enum UpdateOutcome
{
    DisabledByPolicy,
    NotInstalled,
    Current,
    Declined,
    Applied,
    Failed,
}

/// <summary>
/// Auto-Update der laufenden App: prüft beim Start und danach täglich, installiert erst nach Bestätigung. Nur höhere
/// Versionen; Downgrades und Kanalwechsel gehen ausschließlich über die Versionsliste (mit Rückfrage).
/// <c>policy.updatesEnabled=false</c> (IT-Verteilung) schaltet jede Prüfung ab; die Richtlinie wird je Runde neu
/// gelesen, ein späteres <c>provision</c> wirkt also ohne Neustart.
/// </summary>
/// <param name="confirm">Fragt den Nutzer mit der neuen Version; true = jetzt installieren und neu starten.</param>
/// <param name="delay">Wartezeit zwischen den Runden; Tests ersetzen sie.</param>
/// <param name="preferences">Update-Kanal und Vorab-Schalter (W3-CHAN), je Runde neu gelesen; null = nur Stabil.</param>
public sealed class UpdateService(
    IUpdateClient client,
    AppLog log,
    Func<PolicySettings> policy,
    Func<string, Task<bool>> confirm,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<UpdatePreferences>? preferences = null)
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    /// <summary>Prüft sofort und dann alle <see cref="Interval"/>, bis <paramref name="ct"/> abbricht.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (true)
        {
            await CheckOnceAsync(ct);
            await _delay(Interval, ct);
        }
    }

    /// <summary>Eine Runde: Richtlinie, Installation, Feed, Nachfrage, Installation.</summary>
    public async Task<UpdateOutcome> CheckOnceAsync(CancellationToken ct)
    {
        if (policy().UpdatesEnabled == false)
        {
            log.Event("update.check", ("status", "disabled-by-policy"));
            return UpdateOutcome.DisabledByPolicy;
        }
        if (!client.IsInstalled)
        {
            log.Event("update.check", ("status", "not-installed"));
            return UpdateOutcome.NotInstalled;
        }
        try
        {
            var prefs = preferences?.Invoke() ?? UpdatePreferences.Default;
            var found = await UpdatePreferences.NewestAsync(client, UpdateChannels.AutoChannels(prefs.Channel, prefs.PrereleaseAuto), UpdateChannels.CurrentOs, ct);
            if (found is null)
            {
                log.Event("update.check", ("status", "current"), ("version", client.CurrentVersion));
                return UpdateOutcome.Current;
            }
            var next = found.Version;
            log.Event("update.check", ("status", "available"), ("version", next), ("channel", found.Channel));
            // Erst fragen, dann laden: ein geladenes, nicht bestätigtes Update bliebe sonst liegen.
            if (!await confirm(next))
            {
                log.Event("update.check", ("status", "declined"), ("version", next));
                return UpdateOutcome.Declined;
            }
            await client.DownloadAsync(found, downgrade: false, ct);
            log.Event("update.apply", ("version", next), ("channel", found.Channel));
            client.ApplyAndRestart(found);
            return UpdateOutcome.Applied;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Velopack wirft je nach Quelle verschiedene Typen; ein Update darf die App nie anhalten.
            // Nur die Fehlerklasse loggen, die Meldung kann URLs oder Pfade enthalten.
            log.Event("update.check", ("status", "failed"), ("errorClass", e.GetType().Name));
            return UpdateOutcome.Failed;
        }
    }
}
