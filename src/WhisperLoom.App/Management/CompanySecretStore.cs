using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Management;

/// <summary>
/// Keys unter einer Firmen-Anbindung (W2-ENR). Liest:
/// <list type="bullet">
/// <item><see cref="CompanyProvider.EntryId"/> (Eintrag „Firma“ im <see cref="ProviderDirectory"/>): Geräte-Token, nur im Proxy-Modus
/// und nur im Zustand <c>approved</c>. Das Token geht damit nur an die Adresse dieses Eintrags (<see cref="ProviderDirectory.KeyOf"/>).</item>
/// <item>Verteilt-Modus, Gerät freigegeben: verteilter Key aus <paramref name="company"/> (eigenes Präfix), aber nur, solange
/// Anbieter und Adresse des Zugangs bzw. die Adresse des eigenen Anbieters dem Abgleich entsprechen (<see cref="KeyTarget"/>);
/// sonst gar kein Key.</item>
/// <item>Zugang, dessen Ziel die Firma festlegt (Anbieter bzw. Adresse Fest): nie ein eigener Key des Nutzers — sonst ginge
/// er an ein Ziel, das der Nutzer nicht gewählt hat.</item>
/// <item>sonst: eigener Key des Nutzers aus <paramref name="user"/>.</item>
/// </list>
/// Schreiben und Löschen betreffen immer nur die eigenen Keys des Nutzers; Firmen-Keys verwaltet <see cref="ManagementService"/>.
/// </summary>
/// <param name="targets">Ziele der verteilten Keys aus dem letzten Abgleich (<see cref="ManagementService.KeyTargets"/>).</param>
public sealed class CompanySecretStore(ISecretStore user, ISecretStore company, Func<WhisperLoomSettings> settings,
    Func<PolicySettings> policy, Func<IReadOnlyDictionary<string, KeyTarget>> targets) : ISecretStore
{
    /// <summary>Präfix-Zusatz der Firmen-Keys: <c>&lt;Präfix&gt;company:&lt;zugang&gt;</c>, z. B. <c>WhisperLoom:company:stt</c>.</summary>
    public const string PrefixSuffix = "company:";

    public string Kind => user.Kind;

    public async Task<string?> GetAsync(string name, CancellationToken ct)
    {
        var s = settings();
        var m = s.Management;
        if (name == CompanyProvider.EntryId)
        {
            return CompanyProvider.IsActive(m) && m.State == "approved"
                ? await user.GetAsync(SecretNames.Device, ct).ConfigureAwait(false)
                : null;
        }
        if (IsManaged(m) && m.State == "approved" && m.KeyMode == "distribute"
            && (SecretNames.All.Contains(name) || SecretNames.IsProviderKey(name))
            && await company.GetAsync(name, ct).ConfigureAwait(false) is { } key)
        {
            return OnTarget(s, name) ? key : null;
        }
        if (!SecretNames.All.Contains(name) || !IsManaged(m))
        {
            return await user.GetAsync(name, ct).ConfigureAwait(false);
        }
        var section = Section(name);
        var p = policy();
        if (p.IsLocked(section + ".providerId") || p.IsLocked(section + ".baseUrl") || p.IsLocked("secrets." + name))
        {
            return null;
        }
        return await user.GetAsync(name, ct).ConfigureAwait(false);
    }

    public Task SetAsync(string name, string value, CancellationToken ct) => user.SetAsync(name, value, ct);

    public Task DeleteAsync(string name, CancellationToken ct) => user.DeleteAsync(name, ct);

    /// <summary>An einen Firmen-Server angebunden (auch wartend, gesperrt oder widerrufen).</summary>
    public static bool IsManaged(ManagementSettings m) => m.ServerUrl.Length > 0 && m.State is "pending" or "approved" or "blocked" or "revoked";

    private static string Section(string name) => ConfigApply.Section(name);

    /// <summary>Zeigt der Zugang bzw. der eigene Anbieter noch auf das Ziel, für das die Firma den Key verteilt hat?</summary>
    private bool OnTarget(WhisperLoomSettings s, string name)
    {
        if (!targets().TryGetValue(name, out var target))
        {
            return false;
        }
        if (SecretNames.All.Contains(name))
        {
            var (providerId, baseUrl, _) = ConfigApply.Access(s, Section(name));
            return target.Matches(providerId, baseUrl);
        }
        return s.Providers.Custom.FirstOrDefault(c => ProvidersSettings.CustomPrefix + c.Id == name) is { } entry
            && target.Matches(name, entry.BaseUrl ?? "");
    }
}
