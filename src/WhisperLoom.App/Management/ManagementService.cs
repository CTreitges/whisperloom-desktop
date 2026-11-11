using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Management;

/// <summary>
/// Firmen-Anbindung (<c>docs/server-api.md</c> §2): Enrollment per Firmencode, Abgleich mit ETag beim Start, im Intervall
/// und auf Knopfdruck, Token-Rotation, Sperre, Widerruf, Abmelden. Das Geräte-Token liegt im Secret-Store unter
/// <see cref="SecretNames.Device"/>, verteilte Keys unter eigenem Präfix (<paramref name="company"/>). Offline bleibt die
/// zuletzt abgeglichene Konfiguration samt Richtlinie stehen. Ein Abgleich zur Zeit.
/// </summary>
public sealed partial class ManagementService(IManagedTarget target, ISecretStore user, ISecretStore company, ManagementApi api,
    ManagementCache cache, AppLog? log = null, Func<DateTimeOffset>? now = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly OwnKeyTargets _own = new(Path.GetDirectoryName(cache.FilePath)!);

    /// <summary>Nach jeder Änderung des Zustands (für Seite „Firma“ und Assistent).</summary>
    public event Action? Changed;

    /// <summary>Fehlercode des letzten gescheiterten Schritts (<c>code_invalid</c>, <c>offline</c>, …); null = keiner.</summary>
    public string? LastError { get; private set; }

    /// <summary>Wartezeit für das Abfragen des Zustands, solange das Gerät auf Freigabe wartet (Sekunden).</summary>
    public int PollAfterSec { get; private set; } = 10;

    public int SyncIntervalSec => cache.SyncIntervalSec;

    public WhisperLoomSettings Settings => target.Settings;

    public bool CanLeave => ConfigApply.CanLeave(target.Settings, target.Policy);

    /// <summary>
    /// Verbinden: Firmencode gegen Geräte-Token tauschen, Token speichern, Zustand setzen; bei sofortiger Freigabe gleich
    /// abgleichen. Fehler kommen als <see cref="ManagementException"/> bzw. <see cref="ManagementOfflineException"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Adresse oder Code ungültig bzw. schon verbunden (Meldung = Lokalisierungs-Schlüssel).</exception>
    public async Task<SyncOutcome> JoinAsync(string server, string code, string deviceName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // W2-FIX M-3: Neu verbinden ersetzte Server, Token und Richtlinie (Fest, „Verlassen verboten“). Nur nach Widerruf
            // oder nach „Firma verlassen“, das CanLeave beachtet.
            var m = target.Settings.Management;
            if (CompanySecretStore.IsManaged(m) && m.State != "revoked")
            {
                throw new ArgumentException("Company_AlreadyJoined");
            }
            return await JoinCoreAsync(server, code, deviceName, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ein Abgleich: Zustand abfragen, ggf. Token erneuern, Konfiguration mit ETag holen und anwenden.</summary>
    public async Task<SyncOutcome> SyncAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var outcome = await SyncCoreAsync(ct).ConfigureAwait(false);
            log?.Event("management.sync", ("outcome", outcome.ToString().ToLowerInvariant()), ("error", LastError));
            return outcome;
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>„Firma verlassen“: beim Server abmelden, Token und Firmen-Keys löschen, Richtlinie entfernen; Werte bleiben als Frei.</summary>
    public async Task<LeaveOutcome> LeaveAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!CanLeave)
            {
                return LeaveOutcome.NotAllowed;
            }
            var m = target.Settings.Management;
            if (await user.GetAsync(SecretNames.Device, ct).ConfigureAwait(false) is { } token && m.ServerUrl.Length > 0)
            {
                try
                {
                    await api.LeaveAsync(m.ServerUrl, token, ct).ConfigureAwait(false);
                }
                catch (ManagementException e) when (e.Error == "device_unknown")
                {
                    // Schon abgemeldet bzw. gelöscht: lokal weiter aufräumen.
                }
                catch (ManagementException e) when (e.Error == "leave_not_allowed")
                {
                    LastError = e.Error;
                    return LeaveOutcome.NotAllowed;
                }
                catch (ManagementException e)
                {
                    LastError = e.Error;
                    return LeaveOutcome.Failed;
                }
                catch (ManagementOfflineException)
                {
                    LastError = "offline";
                    return LeaveOutcome.Offline;
                }
            }
            await user.DeleteAsync(SecretNames.Device, ct).ConfigureAwait(false);
            await DeleteCompanyKeysAsync(ct).ConfigureAwait(false);
            var settings = Copy(target.Settings);
            ConfigApply.Detach(settings); // Zugänge auf den Firmen-Proxy wieder als eigener Server mit Proxy-Adresse
            settings.Management = new ManagementSettings();
            // W2-FIX H-1: ohne Verwaltung hält niemand mehr eigene Keys zurück. Verwerfen, was danach an ein neues Ziel ginge
            // (K1, z. B. Firmen-Proxy nach Detach) oder an ein Ziel, für das der Nutzer ihn nicht gespeichert hat.
            var stored = await StoredAsync(Provisioning.CandidateKeys(target.Settings, []), ct).ConfigureAwait(false);
            var names = KeyRoutes.Dropped(target.Settings, settings, stored).Select(d => d.Name)
                .Concat(stored.Where(n => SecretNames.All.Contains(n) && !_own.Allows(settings, n)
                    && !CompanySecretStore.UserChoseTarget(target.Settings, n)));
            foreach (var name in names.Distinct(StringComparer.Ordinal).ToList())
            {
                await user.DeleteAsync(name, ct).ConfigureAwait(false);
            }
            _own.Clear();
            cache.Clear();
            await target.SaveAsync(settings, PolicySettings.None).ConfigureAwait(false);
            LastError = null;
            log?.Event("management.leave", ("status", "ok"));
            return LeaveOutcome.Left;
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>Abgleich beim Start und danach im Intervall des Servers (Standard 15 min), bis <paramref name="ct"/> endet.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(SyncIntervalSec), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<SyncOutcome> JoinCoreAsync(string server, string code, string deviceName, CancellationToken ct)
    {
        var (url, urlError) = EnrollInput.NormalizeServer(server);
        if (url is null)
        {
            throw new ArgumentException(urlError);
        }
        var normalized = EnrollInput.NormalizeCode(code) ?? throw new ArgumentException("Company_CodeInvalid");
        var result = await api.EnrollAsync(url, normalized, deviceName, ct).ConfigureAwait(false);
        // Erst das Token sichern: ohne es wäre das neue Gerät beim Server verwaist.
        await user.SetAsync(SecretNames.Device, result.DeviceToken, ct).ConfigureAwait(false);
        cache.Clear();
        if (!CompanySecretStore.IsManaged(target.Settings.Management))
        {
            // W2-FIX K-1: Ziele der eigenen Keys vor dem Beitritt; nur dorthin gibt der CompanySecretStore sie unter Verwaltung.
            // Nach Widerruf bleiben die alten Ziele (der Stand ist schon von der Firma geprägt).
            _own.Snapshot(target.Settings, await StoredAsync(SecretNames.All, ct).ConfigureAwait(false));
        }
        var settings = Copy(target.Settings);
        var m = settings.Management;
        m.ServerUrl = url;
        m.DeviceId = result.DeviceId;
        m.TenantName = result.TenantName;
        m.State = result.State is "approved" or "blocked" ? result.State : "pending";
        m.Code = "";
        m.LastSync = 0;
        PollAfterSec = result.PollAfterSec;
        await target.SaveAsync(settings, target.Policy).ConfigureAwait(false);
        LastError = null;
        log?.Event("management.enroll", ("state", m.State));
        return m.State == "approved" ? await SyncCoreAsync(ct).ConfigureAwait(false)
            : m.State == "blocked" ? SyncOutcome.Blocked : SyncOutcome.Pending;
    }


    /// <summary>
    /// N-1: Fest-Werte fehlen bzw. sind unlesbar, die Richtlinie gilt aber. Den ETag vergessen, damit der nächste Abgleich die
    /// volle Konfiguration holt und policy-fixed.json neu schreibt (statt 304 auf dem lokal geänderten Stand).
    /// </summary>
    public void ForgetConfigVersion() => cache.ForgetETag();

    /// <summary>Welche der Namen liegen im rohen Nutzer-Store (nicht leer)?</summary>
    private async Task<IReadOnlyCollection<string>> StoredAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var stored = new List<string>();
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(await user.GetAsync(name, ct).ConfigureAwait(false)))
            {
                stored.Add(name);
            }
        }
        return stored;
    }

    /// <summary>Ziele der verteilten Keys aus dem letzten Abgleich (Prüfung im <see cref="CompanySecretStore"/>).</summary>
    public IReadOnlyDictionary<string, KeyTarget> KeyTargets => cache.KeyTargets;

    /// <summary>Alle Namen, unter denen Firmen-Keys liegen können: die Zugänge und die eigenen Anbieter des letzten Abgleichs.</summary>
    private IEnumerable<string> CompanyKeyNames() => SecretNames.All.Concat(cache.KeyTargets.Keys.Where(SecretNames.IsProviderKey));

    private async Task DeleteCompanyKeysAsync(CancellationToken ct)
    {
        foreach (var name in CompanyKeyNames().ToList())
        {
            await company.DeleteAsync(name, ct).ConfigureAwait(false);
        }
    }

    private static WhisperLoomSettings Copy(WhisperLoomSettings s) => SettingsJson.Deserialize(SettingsJson.Serialize(s));
}
