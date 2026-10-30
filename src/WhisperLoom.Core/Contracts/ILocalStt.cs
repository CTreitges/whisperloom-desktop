namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Lokale Erkennung über den whisper.cpp-Begleitprozess auf 127.0.0.1 (Implementierung in Phase 4).
/// Erkennt wie jedes <see cref="ITranscriptionBackend"/>; fehlt Modell oder Prozess, wirft
/// <see cref="ITranscriptionBackend.TranscribeAsync"/> eine <see cref="LocalNotAvailableException"/>.
/// </summary>
public interface ILocalStt : ITranscriptionBackend
{
    /// <summary>Modell vorhanden und Begleitprozess startbar. Startet ihn nicht; wirft nur bei Abbruch.</summary>
    Task<bool> IsReadyAsync(CancellationToken ct);
}
