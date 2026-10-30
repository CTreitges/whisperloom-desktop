namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Ablage der API-Keys außerhalb von settings.json: Windows Credential Manager (Ziel <c>WhisperLoom:&lt;name&gt;</c>),
/// Linux Secret Service (<c>secret-tool</c>), sonst eine Datei mit Rechten 600. Werte erscheinen nie in Logs,
/// Exporten oder Ausnahmemeldungen.
/// </summary>
public interface ISecretStore
{
    /// <summary>Wo die Keys liegen, für den Hinweis in der UI ("Credential Manager", "Secret Service", "Datei").</summary>
    string Kind { get; }

    /// <returns>Der Wert oder null, wenn nichts gespeichert ist.</returns>
    Task<string?> GetAsync(string name, CancellationToken ct);

    /// <summary>Speichert oder ersetzt den Wert. Leerer Wert ist ein Fehler — dafür gibt es <see cref="DeleteAsync"/>.</summary>
    /// <exception cref="ArgumentException">Name unbekannt oder Wert leer.</exception>
    Task SetAsync(string name, string value, CancellationToken ct);

    /// <summary>Löscht den Wert; nichts gespeichert ist kein Fehler.</summary>
    Task DeleteAsync(string name, CancellationToken ct);
}

/// <summary>Die Namen der Keys (je Zugang einer, wie die Key-Felder in <c>Prefs.kt</c>).</summary>
public static class SecretNames
{
    public const string Stt = "stt";
    public const string SttFallback = "stt_fallback";
    public const string Llm = "llm";
    public const string LlmFallback = "llm_fallback";

    public static readonly IReadOnlyList<string> All = [Stt, SttFallback, Llm, LlmFallback];
}
