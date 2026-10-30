using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Pipeline;

/// <summary>
/// KI-Stufe für mehrere Stücke einer Aufnahme (Port von <c>SharedRefine.kt</c>; auf dem Desktop ohne Teilen-Weg, aber für
/// den Sync der Share-Stufe und lange Aufnahmen in Stücken). Jedes Stück geht einzeln an das Modell; scheitert eins,
/// gilt die ganze Aufnahme als nicht verbessert.
/// </summary>
public static partial class SharedRefine
{
    /// <param name="Mode">Die wirksame Stufe.</param>
    /// <param name="Paragraphs">KI-Fassung als Absätze; null = keine (Stufe aus oder gescheitert, Grund in <paramref name="Skipped"/>).</param>
    /// <param name="LocalFallback">Online gescheitert, das lokale Textmodell ist eingesprungen — nur zur Info.</param>
    public sealed record Result(RefineMode Mode, IReadOnlyList<string>? Paragraphs, string? Skipped, bool LocalFallback = false)
    {
        /// <summary>Ohne Text.</summary>
        public override string ToString() =>
            $"Result(mode={Mode}, paragraphs={Paragraphs?.Count}, skipped={Skipped}, localFallback={LocalFallback})";
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLine();

    /// <summary>
    /// Je Stück eine Verbesserung, danach die Regeln. Die KI darf eine erkannte Aufnahme nie verschlucken: jeder Fehler
    /// (auch ein fehlender Zugang) wird zum Hinweis. Nur ein Abbruch geht durch.
    /// </summary>
    /// <param name="onStart">Vor der ersten Anfrage; bei „Aus“ nie.</param>
    internal static async Task<Result> RunAsync(
        Refinement refinement,
        IReadOnlyList<string> parts,
        PolishOptions options,
        ITextPolisher polisher,
        Func<string, CancellationToken, Task<string>> refine,
        Func<bool> isCancelled,
        Action onStart,
        CancellationToken ct)
    {
        var mode = refinement.Mode;
        if (mode == RefineMode.Off) return new Result(mode, null, null);
        onStart();
        try
        {
            return new Result(mode, await ParagraphsAsync(parts, options, polisher, isCancelled, refine, ct).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || isCancelled())
        {
            throw;
        }
        catch (Exception e)
        {
            return new Result(mode, null, string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message);
        }
    }

    /// <summary>Je nicht leerem Stück eine Verbesserung und die Regeln; Leerzeilen trennen die Absätze. Nichts erkannt = leer.</summary>
    internal static async Task<List<string>> ParagraphsAsync(
        IReadOnlyList<string> parts,
        PolishOptions options,
        ITextPolisher polisher,
        Func<bool> isCancelled,
        Func<string, CancellationToken, Task<string>> refine,
        CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var part in parts.Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (isCancelled()) throw new OperationCanceledException("Abgebrochen");
            var text = polisher.Polish(await refine(part, ct).ConfigureAwait(false), options);
            result.AddRange(BlankLine().Split(text).Select(p => p.Trim()).Where(p => p.Length > 0));
        }
        return result;
    }
}
