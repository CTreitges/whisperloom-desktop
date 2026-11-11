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

    /// <summary>
    /// Ist ein Wert gespeichert (TS-WLINUX, für <c>status</c>)? Ablagen, die das ohne den Wert prüfen können, überschreiben
    /// das; der Standard liest den Wert und wirft ihn weg.
    /// </summary>
    async Task<bool> ExistsAsync(string name, CancellationToken ct) => await GetAsync(name, ct).ConfigureAwait(false) is not null;
}

/// <summary>Die Namen der Keys (je Zugang einer, wie die Key-Felder in <c>Prefs.kt</c>).</summary>
public static class SecretNames
{
    public const string Stt = "stt";
    public const string SttFallback = "stt_fallback";
    public const string Llm = "llm";
    public const string LlmFallback = "llm_fallback";

    /// <summary>Die Keys der Zugänge (ohne <see cref="Device"/>).</summary>
    public static readonly IReadOnlyList<string> All = [Stt, SttFallback, Llm, LlmFallback];

    /// <summary>Geräte-Token des Firmen-Servers (<c>docs/server-api.md</c>, W2). Kein Zugang, nie per Provisioning setzbar.</summary>
    public const string Device = "device";

    /// <summary>
    /// W2: Key eines eigenen Anbieters (<c>providers.custom</c>). Der Name ist die Anbieter-ID <c>custom:&lt;id&gt;</c>; der
    /// Key gehört zum Eintrag, nicht zum Zugang, und geht nur an dessen Adresse.
    /// </summary>
    public static string ForProvider(string providerId) => providerId;

    /// <summary><c>custom:&lt;id&gt;</c> mit <c>[a-z0-9][a-z0-9_-]{0,31}</c>.</summary>
    public static bool IsProviderKey(string name)
    {
        const string prefix = "custom:";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var id = name[prefix.Length..];
        return id.Length is > 0 and <= 32 && (char.IsAsciiLetterLower(id[0]) || char.IsAsciiDigit(id[0])) &&
            id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-');
    }
}
