namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Spracherkennung über einen Anbieter oder lokal (Port von <c>TranscriptionBackend</c>). Implementierungen:
/// Online (OpenAI-kompatibel, ElevenLabs), Fallback-Kombination, lokaler Begleitprozess (<see cref="ILocalStt"/>).
/// </summary>
public interface ITranscriptionBackend
{
    /// <summary>Kurzname für die Anzeige ("OpenAI", "Lokal").</summary>
    string Label { get; }

    /// <param name="audio">Mono PCM16, 16 kHz.</param>
    /// <param name="language">ISO-Code ("de", "en", …) oder "auto".</param>
    /// <exception cref="ApiNotConfiguredException">Zugang unvollständig, ohne Anfrage.</exception>
    /// <exception cref="ApiHttpException">Fehlerstatus des Servers.</exception>
    /// <exception cref="ApiNetworkException">Server nicht erreicht oder Zeitlimit (auch ein HttpClient-Timeout).</exception>
    /// <exception cref="LocalNotAvailableException">Nur lokal (<see cref="ILocalStt"/>): Modell oder Begleitprozess fehlt.</exception>
    /// <exception cref="OperationCanceledException">Nur bei abgebrochenem <paramref name="ct"/>; dann kein zweiter Upload im Fallback.</exception>
    Task<TranscriptResult> TranscribeAsync(AudioBuffer audio, string language, CancellationToken ct);
}
