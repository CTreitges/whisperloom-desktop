namespace WhisperLoom.Core.Contracts.Settings;

/// <summary>
/// Anbindung an eine Firma (Abschnitt <c>management</c>, W2; Server-API: <c>docs/server-api.md</c>). Gerätezustand:
/// nie exportiert, nie importiert, nie Fest — Ausnahme <see cref="AllowLeave"/>, das die Firma Fest setzen darf.
/// Das Geräte-Token liegt im <see cref="ISecretStore"/> (<see cref="SecretNames.Device"/>), nie hier.
/// </summary>
public sealed class ManagementSettings
{
    public static readonly IReadOnlyList<string> States = ["none", "pending", "approved", "blocked", "revoked"];

    public static readonly IReadOnlyList<string> KeyModes = ["proxy", "distribute"];

    /// <summary>Basis-Adresse des Firmen-Servers ohne <c>/api/v1</c>; "" = keiner (Firma per Datei oder Einzelnutzer).</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>Geräte-ID vom Server (<c>d_…</c>); "" = nicht verbunden.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>Firmenname für „Einige Einstellungen verwaltet &lt;Firma&gt;“.</summary>
    public string TenantName { get; set; } = "";

    /// <summary>Eins aus <see cref="States"/>.</summary>
    public string State { get; set; } = "none";

    /// <summary>Letzter erfolgreicher Abgleich, ms seit 1970 (UTC); 0 = nie.</summary>
    public long LastSync { get; set; }

    /// <summary>Eins aus <see cref="KeyModes"/>; vom Server.</summary>
    public string KeyMode { get; set; } = "proxy";

    /// <summary>Der Nutzer darf die Anbindung lösen. Einziges Feld, das Fest sein darf.</summary>
    public bool AllowLeave { get; set; } = true;

    /// <summary>
    /// Firmencode nur als Eingabe des Provisionings (<c>management.code</c>); die App verwirft ihn nach dem Enrollment.
    /// </summary>
    public string Code { get; set; } = "";

    /// <summary>
    /// Pfade mit Stufe Vorgabe, die der Nutzer geändert hat (User-Override). Eine neue Firmenvorgabe fährt nicht darüber;
    /// „Auf Firmenvorgabe zurücksetzen“ entfernt den Pfad. Pflegt <c>ManagedDefaults</c>.
    /// </summary>
    public List<string> Overrides { get; set; } = [];
}
