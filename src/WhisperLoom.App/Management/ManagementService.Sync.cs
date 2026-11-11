using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Management;

/// <summary>Abgleich: Zustand, Token-Rotation, Konfiguration mit ETag, Sperre und Widerruf.</summary>
public sealed partial class ManagementService
{
    private async Task<SyncOutcome> SyncCoreAsync(CancellationToken ct)
    {
        var m = target.Settings.Management;
        if (m.ServerUrl.Length == 0)
        {
            return SyncOutcome.NotManaged;
        }
        var token = await user.GetAsync(SecretNames.Device, ct).ConfigureAwait(false);
        if (token is null)
        {
            return await WithoutTokenAsync(m, ct).ConfigureAwait(false);
        }
        try
        {
            var device = await api.DeviceAsync(m.ServerUrl, token, ct).ConfigureAwait(false);
            PollAfterSec = device.PollAfterSec;
            if (device.RotateToken)
            {
                token = await RotateAsync(m.ServerUrl, token, ct).ConfigureAwait(false);
            }
            switch (device.State)
            {
                case "blocked":
                    return await BlockAsync(ct).ConfigureAwait(false);
                case "approved":
                    return await FetchConfigAsync(m.ServerUrl, token, ct).ConfigureAwait(false);
                default:
                    await SetStateAsync("pending", device.TenantName).ConfigureAwait(false);
                    LastError = null;
                    return SyncOutcome.Pending;
            }
        }
        catch (ManagementException e)
        {
            LastError = e.Error;
            return e.Error switch
            {
                "device_unknown" => await RevokeAsync(ct).ConfigureAwait(false),
                "device_blocked" or "tenant_blocked" => await BlockAsync(ct).ConfigureAwait(false),
                "device_pending" => SyncOutcome.Pending,
                _ => SyncOutcome.Failed,
            };
        }
        catch (ManagementOfflineException)
        {
            LastError = "offline";
            return SyncOutcome.Offline;
        }
    }

    /// <summary>Ohne Token: Provisioning mit Firmencode → jetzt verbinden; sonst gilt das Gerät als widerrufen.</summary>
    private async Task<SyncOutcome> WithoutTokenAsync(ManagementSettings m, CancellationToken ct)
    {
        // W2-FIX (Prüfer-Befund 4): Der Firmencode verbindet nur ein noch nicht verbundenes bzw. widerrufenes Gerät (M-3).
        if (m.Code.Length > 0 && m.State is "none" or "revoked")
        {
            try
            {
                return await JoinCoreAsync(m.ServerUrl, m.Code, EnrollInput.DefaultDeviceName(), ct).ConfigureAwait(false);
            }
            catch (ArgumentException e)
            {
                LastError = e.Message;
                return SyncOutcome.Failed;
            }
            catch (ManagementException e)
            {
                LastError = e.Error;
                return SyncOutcome.Failed;
            }
            catch (ManagementOfflineException)
            {
                LastError = "offline";
                return SyncOutcome.Offline;
            }
        }
        return m.State is "none" ? SyncOutcome.NotManaged : await RevokeAsync(ct).ConfigureAwait(false);
    }

    private async Task<SyncOutcome> FetchConfigAsync(string serverUrl, string token, CancellationToken ct)
    {
        var response = await api.ConfigAsync(serverUrl, token, cache.ETag, ct).ConfigureAwait(false);
        var settings = Copy(target.Settings);
        if (response.NotModified || response.Body is null)
        {
            settings.Management.State = "approved";
            settings.Management.LastSync = _now().ToUnixTimeMilliseconds();
            // W2-FIX N-1: nur den Zustand speichern; policy-fixed.json bleibt der Stand des letzten 200.
            await target.SaveStateAsync(settings).ConfigureAwait(false);
            LastError = null;
            return SyncOutcome.NotModified;
        }
        // W2-FIX K-1/V-1: welche eigenen Keys wirklich gespeichert sind, steht nur im rohen Nutzer-Store.
        var stored = await StoredAsync(ConfigApply.StoredKeyCandidates(settings, response.Body), ct).ConfigureAwait(false);
        var plan = ConfigApply.Plan(settings, response.Body, _now().ToUnixTimeMilliseconds(), stored);
        if (!plan.CanApply)
        {
            LastError = "config_invalid";
            log?.Event("management.config", ("status", "invalid"), ("errors", plan.Errors.Count));
            return SyncOutcome.Failed;
        }
        foreach (var path in plan.RetiredFixed)
        {
            // TD-A4: festes Modell der Firma abgeschaltet und auf den Nachfolger umgestellt — nur der Pfad, kein Wert.
            log?.Event("management.retired_model", ("path", path), ("level", "fixed"));
        }
        // Erst die Keys: scheitert die Ablage, bleiben Werte und Richtlinie wie vorher.
        foreach (var name in CompanyKeyNames().Concat(plan.CompanyKeys.Keys).Distinct(StringComparer.Ordinal))
        {
            if (plan.CompanyKeys.TryGetValue(name, out var key))
            {
                await company.SetAsync(name, key, ct).ConfigureAwait(false);
                if (SecretNames.IsProviderKey(name))
                {
                    // Eigener Key gleichen Namens ginge sonst (nach Sperre/Austritt) an die Adresse der Firma.
                    await user.DeleteAsync(name, ct).ConfigureAwait(false);
                }
            }
            else
            {
                await company.DeleteAsync(name, ct).ConfigureAwait(false);
            }
        }
        // W2-LOCAL R-K1-1: eigene Keys, die nach der Konfiguration an ein anderes Ziel gingen, vorher löschen.
        foreach (var drop in plan.DroppedKeys)
        {
            await user.DeleteAsync(drop.Name, ct).ConfigureAwait(false);
        }
        cache.Save(response.ETag, response.Body, plan.KeyTargets);
        await target.SaveAsync(plan.Settings!, plan.Policy).ConfigureAwait(false);
        LastError = null;
        log?.Event("management.config", ("status", "applied"), ("keyMode", plan.KeyMode), ("keys", plan.CompanyKeys.Count));
        return SyncOutcome.Synced;
    }

    /// <summary>Neues Token holen und speichern, bevor es benutzt wird (§3.4: das alte gilt, bis das neue benutzt wurde).</summary>
    private async Task<string> RotateAsync(string serverUrl, string token, CancellationToken ct)
    {
        var fresh = await api.RotateTokenAsync(serverUrl, token, ct).ConfigureAwait(false);
        await user.SetAsync(SecretNames.Device, fresh, ct).ConfigureAwait(false);
        log?.Event("management.token", ("status", "rotated"));
        return fresh;
    }

    /// <summary>
    /// Gesperrt: Firmen-Zugänge aus (Keys weg, kein Token an den Proxy), Hinweis; die übrigen Werte bleiben. Mit den Keys geht
    /// der ETag (W2-FIX N-4): sonst lieferte der Server nach dem Entsperren 304 und die Keys kämen nie wieder.
    /// </summary>
    private async Task<SyncOutcome> BlockAsync(CancellationToken ct)
    {
        await DeleteCompanyKeysAsync(ct).ConfigureAwait(false);
        cache.Clear();
        await SetStateAsync("blocked", null).ConfigureAwait(false);
        return SyncOutcome.Blocked;
    }

    /// <summary>Token unbekannt (Gerät gelöscht): Token und Firmen-Keys löschen, „Neu verbinden“ anbieten; Werte bleiben.</summary>
    private async Task<SyncOutcome> RevokeAsync(CancellationToken ct)
    {
        await user.DeleteAsync(SecretNames.Device, ct).ConfigureAwait(false);
        await DeleteCompanyKeysAsync(ct).ConfigureAwait(false);
        cache.Clear();
        await SetStateAsync("revoked", null).ConfigureAwait(false);
        return SyncOutcome.Revoked;
    }

    private async Task SetStateAsync(string state, string? tenantName)
    {
        var current = target.Settings.Management;
        if (current.State == state && (tenantName is null || tenantName.Length == 0 || current.TenantName == tenantName))
        {
            return;
        }
        var settings = Copy(target.Settings);
        settings.Management.State = state;
        if (tenantName is { Length: > 0 })
        {
            settings.Management.TenantName = tenantName;
        }
        await target.SaveStateAsync(settings).ConfigureAwait(false); // N-1: festgehaltene Werte nicht aus dem Ist-Stand
    }
}
