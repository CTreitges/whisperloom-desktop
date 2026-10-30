namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Der ganze Weg eines Diktats: Stille trimmen → Erkennung (mit Fallback) → Verbesserung (Online →
/// Fallback-Zugang → lokal → Rohtext + Regeln) → Regeln (<see cref="ITextPolisher"/>). Details: docs/contracts.md.
/// </summary>
public interface IDictationPipeline
{
    /// <summary>
    /// Verarbeitet eine Aufnahme. Scheitert nur die Verbesserung, kommt trotzdem ein Ergebnis (Rohtext +
    /// Regeln, <see cref="RefinedText.Skipped"/> gesetzt) — die Verbesserung darf ein Diktat nie verschlucken.
    /// </summary>
    /// <exception cref="ApiNotConfiguredException">Erkennung nicht eingerichtet.</exception>
    /// <exception cref="ApiHttpException">Erkennung (und ggf. Fallback) mit Fehlerstatus gescheitert.</exception>
    /// <exception cref="ApiNetworkException">Erkennung (und ggf. Fallback) nicht erreichbar.</exception>
    /// <exception cref="OperationCanceledException">Abgebrochen über <paramref name="ct"/>.</exception>
    Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct);

    /// <summary>
    /// Neu verarbeiten aus dem Rohtext, ohne Audio (Verlauf, andere Stufe). Wirft nur bei Abbruch.
    /// </summary>
    Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct);
}
