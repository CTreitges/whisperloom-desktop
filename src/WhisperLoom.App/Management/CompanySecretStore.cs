using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Management;

/// <summary>
/// Keys der Zugänge unter einer Firmen-Anbindung (W2-ENR). Liest:
/// <list type="bullet">
/// <item>Proxy-Modus, Gerät freigegeben: Zugang mit Adresse = Firmen-Proxy (<see cref="CompanyProvider.Targets"/>) → Geräte-Token.</item>
/// <item>Verteilt-Modus, Gerät freigegeben: verteilter Key aus <paramref name="company"/> (eigenes Präfix).</item>
/// <item>Zugang, dessen Ziel die Firma festlegt (Anbieter bzw. Adresse Fest): nie ein eigener Key des Nutzers — sonst ginge
/// er an ein Ziel, das der Nutzer nicht gewählt hat.</item>
/// <item>sonst: eigener Key des Nutzers aus <paramref name="user"/>.</item>
/// </list>
/// Schreiben und Löschen betreffen immer nur die eigenen Keys des Nutzers; Firmen-Keys verwaltet <see cref="ManagementService"/>.
/// </summary>
public sealed class CompanySecretStore(ISecretStore user, ISecretStore company, Func<WhisperLoomSettings> settings,
    Func<PolicySettings> policy, Func<string?> proxyBaseUrl) : ISecretStore
{
    /// <summary>Präfix-Zusatz der Firmen-Keys: <c>&lt;Präfix&gt;company:&lt;zugang&gt;</c>, z. B. <c>WhisperLoom:company:stt</c>.</summary>
    public const string PrefixSuffix = "company:";

    public string Kind => user.Kind;

    public async Task<string?> GetAsync(string name, CancellationToken ct)
    {
        var s = settings();
        var m = s.Management;
        if (!SecretNames.All.Contains(name) || !IsManaged(m))
        {
            return await user.GetAsync(name, ct).ConfigureAwait(false);
        }
        var (section, baseUrl) = Target(s, name);
        if (m.State == "approved" && m.KeyMode == "proxy" && CompanyProvider.Targets(proxyBaseUrl(), baseUrl))
        {
            return await user.GetAsync(SecretNames.Device, ct).ConfigureAwait(false);
        }
        if (m.State == "approved" && m.KeyMode == "distribute" && await company.GetAsync(name, ct).ConfigureAwait(false) is { } key)
        {
            return key;
        }
        var p = policy();
        if (p.IsLocked(section + ".providerId") || p.IsLocked(section + ".baseUrl") || p.IsLocked("secrets." + name))
        {
            return null;
        }
        return await user.GetAsync(name, ct).ConfigureAwait(false);
    }

    public Task SetAsync(string name, string value, CancellationToken ct) => user.SetAsync(name, value, ct);

    public Task DeleteAsync(string name, CancellationToken ct) => user.DeleteAsync(name, ct);

    /// <summary>An einen Firmen-Server angebunden (auch wartend oder gesperrt).</summary>
    public static bool IsManaged(ManagementSettings m) => m.ServerUrl.Length > 0 && m.State is "pending" or "approved" or "blocked" or "revoked";

    /// <summary>Abschnitt und wirksame Adresse des Zugangs; „wie Erkennung“ nutzt die Adresse der Erkennung.</summary>
    private static (string Section, string BaseUrl) Target(WhisperLoomSettings s, string name) => name switch
    {
        SecretNames.Stt => ("stt", Url(s.Stt.ProviderId, s.Stt.BaseUrl)),
        SecretNames.SttFallback => ("sttFallback", Url(s.SttFallback.ProviderId, s.SttFallback.BaseUrl)),
        SecretNames.Llm when s.Llm.ProviderId == LlmSettings.Same => ("llm", Url(s.Stt.ProviderId, s.Stt.BaseUrl)),
        SecretNames.Llm => ("llm", Url(s.Llm.ProviderId, s.Llm.BaseUrl)),
        _ => ("llmFallback", Url(s.LlmFallback.ProviderId, s.LlmFallback.BaseUrl)),
    };

    private static string Url(string providerId, string baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl) ? ProviderCatalog.Find(providerId)?.BaseUrl ?? "" : baseUrl.Trim();
}
