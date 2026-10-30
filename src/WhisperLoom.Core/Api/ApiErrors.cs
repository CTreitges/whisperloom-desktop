using System.Security.Authentication;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Die Modell-Liste des Anbieters ließ sich nicht lesen (kein JSON, keine Liste). Der Aufrufer behält die eingebauten
/// Empfehlungen (Port von <c>ModelListException</c>).
/// </summary>
public class ModelListException(Exception? inner = null) : Exception("Antwort des Servers ist keine Modell-Liste", inner);

/// <summary>
/// Meldungen und Hinweise der Fehlertypen aus den Verträgen (Port von <c>api/ApiErrors.kt</c>). Die Typen selbst stehen
/// in <c>Contracts/Errors.cs</c>; hier entstehen sie mit lesbarer Meldung.
/// </summary>
public static class ApiErrors
{
    /// <summary>Hinweis zu den typischen Stolperfallen eines Status; null = keiner.</summary>
    public static string? Hint(int code, string detail, ApiStyle style = ApiStyle.OpenAi) => code switch
    {
        // ElevenLabs-Keys sind auf Endpunkte beschränkt; fehlt die Freigabe, kommt ebenfalls 401.
        401 or 403 when style == ApiStyle.ElevenLabs => "Key oder Berechtigung „Speech to Text“ prüfen",
        401 or 403 => "Server verlangt einen (anderen) API-Key",
        402 => "Guthaben aufgebraucht — beim Anbieter aufladen",
        // Unbekannte Modell-IDs liefern ebenfalls 404 — dann liegt es nicht an der Base-URL.
        404 when detail.Contains("model", StringComparison.OrdinalIgnoreCase) => "Modell-ID prüfen",
        404 when style != ApiStyle.ElevenLabs => "Endpunkt nicht gefunden — Base-URL muss auf /v1 enden",
        400 when detail.Contains("failed to read audio data", StringComparison.OrdinalIgnoreCase) => "Server konnte das WAV nicht lesen",
        _ => null,
    };

    /// <summary>Fehlerstatus mit dem Hinweis zum Status (<see cref="Hint"/>).</summary>
    public static ApiHttpException Http(int code, string detail, ApiStyle style = ApiStyle.OpenAi) =>
        HttpWithHint(code, detail, Hint(code, detail, style));

    /// <summary>Fehlerstatus mit eigenem Hinweis (null = keiner). Meldung: „API-Fehler &lt;code&gt;: &lt;detail&gt; — &lt;hinweis&gt;“.</summary>
    public static ApiHttpException HttpWithHint(int code, string detail, string? hint)
    {
        var message = $"API-Fehler {code}: {detail}";
        return new ApiHttpException(code, hint is null ? message : $"{message} — {hint}");
    }

    /// <summary>Netzfehler mit lesbarer Meldung statt der rohen Ausnahme; die Ursache hängt als InnerException dran.</summary>
    public static ApiNetworkException Network(Exception cause) => new(Describe(cause), cause);

    /// <summary>Lesbare Meldung für einen Netzfehler (Port von <c>ApiNetworkException.describe</c>).</summary>
    public static string Describe(Exception e)
    {
        const string unreachable = "Server nicht erreichbar — läuft er, stimmt der Port, gleiches WLAN/VPN?";
        switch (e)
        {
            case UriFormatException:
                return "Base-URL fehlt oder ist ungültig — in den Einstellungen prüfen";
            case TimeoutException when e.Message.Contains("connect", StringComparison.OrdinalIgnoreCase):
                return unreachable;
            case TimeoutException:
                return "Zeitüberschreitung — Server zu langsam oder Verbindung schlecht";
            case AuthenticationException:
                return "TLS-Fehler — Zertifikat des Servers ungültig";
            case HttpRequestException { InnerException: (TimeoutException or AuthenticationException) and var inner }:
                return Describe(inner);
            case HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }:
                return "Server nicht gefunden — Hostname/IP prüfen";
            case HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError }:
                return unreachable;
            case HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }:
                return "TLS-Fehler — Zertifikat des Servers ungültig";
            default:
                return string.IsNullOrEmpty(e.Message) ? "Netzwerkfehler" : e.Message;
        }
    }

    /// <summary>
    /// Ob ein erneuter Versuch Sinn hat: Netzprobleme und Aussetzer des Servers ja; falscher Key, unbekanntes Modell oder
    /// eine kaputte Base-URL bleiben auch beim zehnten Versuch falsch.
    /// </summary>
    public static bool IsRetryable(this Exception e) => e switch
    {
        ApiNetworkException n => n.InnerException is not UriFormatException,
        ApiHttpException h => h.StatusCode is 408 or 429 or >= 500,
        _ => false,
    };
}
