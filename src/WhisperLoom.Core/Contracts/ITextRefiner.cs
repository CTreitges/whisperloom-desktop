namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Textverbesserung über einen Online-Zugang (Port von <c>api/TextRefiner</c>): OpenAI-kompatibles
/// /chat/completions oder native Ollama-API. Der Prompt kommt aus <see cref="IRefinePromptBuilder"/>.
/// </summary>
public interface ITextRefiner
{
    /// <summary>Womit verbessert wird, als Anzeige (Modellname).</summary>
    string ModelLabel { get; }

    /// <summary>
    /// Liefert die KI-Fassung (noch ohne <see cref="ITextPolisher"/>). Leere Eingabe oder
    /// <see cref="RefineMode.Off"/> → Rohtext unverändert, ohne Anfrage.
    /// </summary>
    /// <exception cref="ApiNotConfiguredException">Base-URL leer oder der Zugang kann keinen Text verbessern.</exception>
    /// <exception cref="RefineRejectedException">Antwort statt Bearbeitung, abgeschnitten oder leer.</exception>
    /// <exception cref="ApiHttpException">Fehlerstatus (nach einem zweiten Versuch ohne <c>temperature</c> bei 400).</exception>
    /// <exception cref="ApiNetworkException">Server nicht erreicht oder Zeitlimit.</exception>
    /// <exception cref="OperationCanceledException">Abgebrochen.</exception>
    Task<string> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct);
}
