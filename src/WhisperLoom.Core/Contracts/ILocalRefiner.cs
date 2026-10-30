namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Lokale Textverbesserung über Ollama auf 127.0.0.1:11434 (Implementierung in Phase 4; Port der Rolle von
/// <c>llm/LocalRefiner</c>). Gleicher Ablauf wie <see cref="ITextRefiner"/>; fehlt Ollama oder das Modell,
/// wirft <see cref="ITextRefiner.RefineAsync"/> eine <see cref="LocalNotAvailableException"/>.
/// </summary>
public interface ILocalRefiner : ITextRefiner
{
    /// <summary>Ollama erreichbar und Modell vorhanden (<c>/api/tags</c>). Wirft nur bei Abbruch.</summary>
    Task<bool> IsReadyAsync(CancellationToken ct);
}
