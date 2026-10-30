namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Setzt den System-Prompt einer Stufe zusammen (Port von <c>api/RefinePrompt</c>). Die Fragmente liegen in
/// Phase 1 als eingebettete Ressourcen je Stufe und Sprache und werden 1:1 aus Android übernommen.
/// Rein und synchron: kein I/O, keine Ausnahmen außer bei falschen Argumenten.
/// </summary>
public interface IRefinePromptBuilder
{
    /// <param name="mode">Was das Modell tun soll; nicht <see cref="RefineMode.Off"/>.</param>
    /// <param name="german">Anweisung auf Deutsch (deutsche Diktatsprache) statt Englisch.</param>
    /// <param name="smartFillers">Bereinigung „Ohne Füllwörter“, nur beim Glätten.</param>
    /// <param name="paragraphs">Absätze der Stufe; false = durchgehender Text.</param>
    /// <param name="isShort">Kurzes Diktat: Zusammenfassung nur bereinigt, Prompt bleibt Fließtext.</param>
    /// <exception cref="ArgumentException"><paramref name="mode"/> ist <see cref="RefineMode.Off"/>.</exception>
    string Build(RefineMode mode, bool german, bool smartFillers, bool paragraphs = true, bool isShort = false);

    /// <summary>Die Benutzer-Nachricht: das Diktat als abgegrenzter Datenblock.</summary>
    string UserText(string raw, bool german);
}
