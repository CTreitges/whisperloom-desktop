namespace WhisperLoom.Core.Contracts;

// Fehlertypen der Verträge (Namen wie api/ApiErrors.kt). Meldungen enthalten nie Diktattext, Keys oder Header.
// Texte und Hinweise portiert Phase 1; hier stehen die Typen, auf die sich Aufrufer verlassen.

/// <summary>Zugang unvollständig (Base-URL leer, Key fehlt) oder der Anbieter kann die Aufgabe nicht.</summary>
public class ApiNotConfiguredException(string message = "Anbieter nicht eingerichtet — Base-URL und API-Key in den Einstellungen prüfen")
    : Exception(message);

/// <summary>Das Sprachmodell hat geantwortet, aber nicht das Verlangte getan. Aufrufer fällt auf den Rohtext zurück.</summary>
public class RefineRejectedException(string message) : Exception(message);

/// <summary>Der Server hat mit einem Fehlerstatus geantwortet. Der Antwort-Körper wird nicht übernommen.</summary>
public class ApiHttpException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Netzfehler (DNS, Verbindung, TLS, Zeitlimit) — der Server wurde nicht oder nicht vollständig erreicht.</summary>
public class ApiNetworkException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Lokale Erkennung bzw. lokales Textmodell nicht bereit (Begleitprozess, Modell oder Ollama fehlt).</summary>
public class LocalNotAvailableException(string message) : Exception(message);
