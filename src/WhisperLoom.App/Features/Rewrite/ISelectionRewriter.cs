using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Features.Rewrite;

/// <summary>
/// Schreibt eine Markierung nach einer gesprochenen Anweisung um (Prompt „Umschreiben nach Anweisung“). Wie
/// <see cref="IDictationPipeline.RefineAsync"/>: wirft nur bei Abbruch; ohne KI-Ergebnis ist <see cref="RefinedText.Model"/> null.
/// </summary>
public interface ISelectionRewriter
{
    Task<RefinedText> RewriteAsync(string selection, string instruction, string language, CancellationToken ct);
}
