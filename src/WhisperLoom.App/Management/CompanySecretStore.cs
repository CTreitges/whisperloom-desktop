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
/// <item>übrige Zugänge unter Verwaltung: eigener Key nur, wenn das aktuelle Ziel dem beim Speichern bzw. Beitritt entspricht
/// (<paramref name="own"/>) oder der Nutzer Anbieter und Adresse selbst überschrieben hat (W2-FIX K-1).</item>
/// <item>sonst: eigener Key des Nutzers aus <paramref name="user"/>.</item>
/// </list>
/// Schreiben und Löschen betreffen immer nur die eigenen Keys des Nutzers; Firmen-Keys verwaltet <see cref="ManagementService"/>.
/// </summary>
/// <param name="targets">Ziele der verteilten Keys aus dem letzten Abgleich (<see cref="ManagementService.KeyTargets"/>).</param>
public sealed class CompanySecretStore(ISecretStore user, ISecretStore company, Func<WhisperLoomSettings> settings,
    Func<PolicySettings> policy, Func<IReadOnlyDictionary<string, KeyTarget>> targets, OwnKeyTargets own) : ISecretStore
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
        var p = policy();
        if (TargetFixed(p, name) || p.IsLocked("secrets." + name))
        {
            return null;
        }
        // W2-FIX K-1 (Tiefe): Vorgabe oder Frei vom Server zählt nicht als Wahl des Nutzers. Der eigene Key geht nur an das
        // Ziel, für das er gespeichert wurde, oder an ein Ziel, das der Nutzer selbst überschrieben hat.
        if (!own.Allows(s, name) && !UserChoseTarget(s, name))
        {
            return null;
        }
        return await user.GetAsync(name, ct).ConfigureAwait(false);
    }

    /// <summary>Eigener Key unter Verwaltung: Ziel festhalten, das gerade gilt (CLI, Provisioning).</summary>
    public async Task SetAsync(string name, string value, CancellationToken ct)
    {
        await user.SetAsync(name, value, ct).ConfigureAwait(false);
        if (SecretNames.All.Contains(name) && IsManaged(settings().Management))
        {
            own.Record(settings(), name, policy());
        }
    }

    /// <summary>
    /// Key-Feld der Einstellungen: Ziel festhalten, das der Nutzer beim Speichern sieht (<paramref name="target"/>, noch nicht
    /// gespeicherte Auswahl), nicht das gespeicherte.
    /// </summary>
    public async Task SetAsync(string name, string value, (string ProviderId, string BaseUrl) target, CancellationToken ct)
    {
        await user.SetAsync(name, value, ct).ConfigureAwait(false);
        if (SecretNames.All.Contains(name) && IsManaged(settings().Management))
        {
            var shown = SettingsJson.Deserialize(SettingsJson.Serialize(settings()));
            ConfigApply.Access(shown, Section(name)).Set(target.ProviderId, target.BaseUrl);
            own.Record(shown, name, policy());
        }
    }

    /// <summary>Die eigenen Keys des Nutzers ohne Firmen-Regeln (für K1-Prüfungen: was liegt wirklich gespeichert?).</summary>
    public ISecretStore UserStore => user;

    /// <summary>
    /// Anbieter und Adresse des Zugangs <paramref name="access"/> hat der Nutzer selbst gegen die Vorgabe gesetzt (Override).
    /// Nur genau diese Blatt-Pfade zählen: eine Vorgabe auf den ganzen Abschnitt plus geändertes Modell ist keine Wahl des Ziels.
    /// </summary>
    public static bool UserChoseTarget(WhisperLoomSettings s, string access) =>
        s.Management.Overrides.Contains(Section(access) + ".providerId") && s.Management.Overrides.Contains(Section(access) + ".baseUrl");

    public Task DeleteAsync(string name, CancellationToken ct) => user.DeleteAsync(name, ct);

    /// <summary>Legt die Firma Anbieter oder Adresse des Zugangs <paramref name="access"/> fest (Fest)? Dann wählt der Nutzer das Ziel nicht.</summary>
    public static bool TargetFixed(PolicySettings p, string access) =>
        p.IsLocked(Section(access) + ".providerId") || p.IsLocked(Section(access) + ".baseUrl");

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
