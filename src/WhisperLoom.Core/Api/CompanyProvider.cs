using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Anbieter-Typ „Firma“ (W2-ENR, <c>docs/server-api.md</c> §3.6/§3.7): Im Key-Modus <c>proxy</c> laufen Erkennung und
/// Textmodell über den Proxy des Firmen-Servers. Base-URL = Proxy, Key = Geräte-Token, Modellliste = <c>/proxy/models</c>.
/// Der Server schreibt die Zugänge als <c>custom</c> mit der Proxy-Adresse (Fest); erst die App macht daraus diesen
/// Typ, indem sie das Geräte-Token nur Zugängen gibt, deren Adresse genau der Proxy ist (<see cref="Targets"/>).
/// Schmale Naht bis zur Zugangs-Auflösung aus W2-LOCAL (<c>ProviderDirectory</c>/<c>IProviderSource</c>).
/// </summary>
public static class CompanyProvider
{
    public const string Id = "company";

    /// <summary>Fehlercode des Proxys, wenn das Budget des Geräts bzw. der Gruppe aufgebraucht ist.</summary>
    public const string BudgetError = "budget_exhausted";

    /// <summary>Proxy-Adresse, wenn die Konfiguration keine nennt: <c>&lt;serverUrl&gt;/api/v1/proxy</c>.</summary>
    public static string ProxyBaseUrl(string serverUrl) => serverUrl.Trim().TrimEnd('/') + "/api/v1/proxy";

    /// <summary>
    /// Geht ein Aufruf mit Base-URL <paramref name="accessBaseUrl"/> an den Proxy <paramref name="proxyBaseUrl"/>? Gleich
    /// bis auf abschließenden <c>/</c> und Groß-/Kleinschreibung von Schema und Host; Pfad exakt. Leere Werte nie.
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

    /// <summary>Fehlerklasse <c>budget</c>: der Proxy meldet <c>403 budget_exhausted</c> (Text „Monatslimit der Firma erreicht“).</summary>
    public static bool IsBudgetError(Exception e) =>
        e is ApiHttpException { StatusCode: 403 } h && h.Message.Contains(BudgetError, StringComparison.Ordinal);
}
