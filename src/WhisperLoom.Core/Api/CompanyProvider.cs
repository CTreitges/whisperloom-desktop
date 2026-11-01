using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Anbieter-Typ „Firma“ (W2-ENR, <c>docs/server-api.md</c> §3.6/§3.7): Im Key-Modus <c>proxy</c> laufen Erkennung und
/// Textmodell über den Proxy des Firmen-Servers — Eintrag <see cref="EntryId"/> der Quelle <see cref="CompanyProviderSource"/>
/// im <see cref="ProviderDirectory"/>. Adresse = <c>&lt;management.serverUrl&gt;/api/v1/proxy</c>, Key = Geräte-Token (nur
/// dieser Eintrag bekommt ihn, über <see cref="ProviderDirectory.KeyOf"/>), Modellliste = <c>/proxy/models</c>.
/// Der Server schreibt die Zugänge als <c>custom</c> mit der Proxy-Adresse; der Abgleich der App stellt sie auf
/// <see cref="EntryId"/> um (<see cref="Targets"/>).
/// </summary>
public static class CompanyProvider
{
    public const string Id = "company";

    /// <summary>Präfix der Quelle im <see cref="ProviderDirectory"/>.</summary>
    public const string Prefix = Id + ":";

    /// <summary>Der eine Eintrag: der Proxy des Firmen-Servers. Auch Name des Keys (= Geräte-Token).</summary>
    public const string EntryId = Prefix + "proxy";

    /// <summary>Fehlercode des Proxys, wenn das Budget des Geräts bzw. der Gruppe aufgebraucht ist (§1.2, HTTP 403).</summary>
    public const string BudgetError = "budget_exhausted";

    /// <summary>Proxy-Adresse des Servers: <c>&lt;serverUrl&gt;/api/v1/proxy</c>.</summary>
    public static string ProxyBaseUrl(string serverUrl) => serverUrl.Trim().TrimEnd('/') + "/api/v1/proxy";

    /// <summary>
    /// Proxy-Modus mit Firmen-Server (auch wartend, gesperrt, widerrufen): Der Eintrag existiert, damit die festen Zugänge
    /// gültig bleiben; einen Key (Token) gibt es nur im Zustand <c>approved</c>.
    /// </summary>
    public static bool IsActive(ManagementSettings m) =>
        m.ServerUrl.Length > 0 && m.KeyMode == "proxy" && m.State is "pending" or "approved" or "blocked" or "revoked";

    /// <summary>
    /// Geht ein Aufruf mit Base-URL <paramref name="accessBaseUrl"/> an den Proxy <paramref name="proxyBaseUrl"/>? Gleich
    /// bis auf abschließenden <c>/</c> und Groß-/Kleinschreibung von Schema und Host; Port und Pfad exakt. Leere Werte nie.
    /// </summary>
    public static bool Targets(string? proxyBaseUrl, string? accessBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyBaseUrl) || string.IsNullOrWhiteSpace(accessBaseUrl)
            || !Uri.TryCreate(proxyBaseUrl.Trim(), UriKind.Absolute, out var proxy)
            || !Uri.TryCreate(accessBaseUrl.Trim(), UriKind.Absolute, out var access))
        {
            return false;
        }
        return proxy.Scheme == access.Scheme
            && string.Equals(proxy.IdnHost, access.IdnHost, StringComparison.OrdinalIgnoreCase)
            && proxy.Port == access.Port
            && string.IsNullOrEmpty(proxy.Query) && string.IsNullOrEmpty(access.Query)
            && proxy.AbsolutePath.TrimEnd('/') == access.AbsolutePath.TrimEnd('/');
    }

    /// <summary>Fehlerklasse <c>budget</c>: der Proxy meldet <c>403 budget_exhausted</c> (Text „Firmenbudget für diesen Monat erreicht – bitte IT fragen“; kein Ersatz-Anbieter).</summary>
    public static bool IsBudgetError(Exception e) =>
        e is ApiHttpException { StatusCode: 403 } h && h.Message.Contains(BudgetError, StringComparison.Ordinal);
}

/// <summary>
/// Quelle <c>company:</c> im <see cref="ProviderDirectory"/>: im Proxy-Modus genau ein Eintrag <see cref="CompanyProvider.EntryId"/>
/// mit fester Adresse des Proxys (OpenAI-kompatibel, Erkennung + Text, Modelle vom Server), sonst leer.
/// </summary>
public sealed class CompanyProviderSource(ManagementSettings management) : IProviderSource
{
    public string Prefix => CompanyProvider.Prefix;

    public IReadOnlyList<ProviderInfo> Providers { get; } = CompanyProvider.IsActive(management) ? [Entry(management)] : [];

    private static ProviderInfo Entry(ManagementSettings m) => new(
        Id: CompanyProvider.EntryId,
        Name: m.TenantName.Length > 0 ? m.TenantName : "Firma",
        BaseUrl: CompanyProvider.ProxyBaseUrl(m.ServerUrl),
        NeedsKey: true,
        AllowsHttp: true,
        DefaultReadTimeoutSec: 600,
        SttModels: [],
        LlmModels: [],
        Family: ProviderCatalog.CustomId);
}
