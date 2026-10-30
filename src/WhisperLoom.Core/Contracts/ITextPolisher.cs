namespace WhisperLoom.Core.Contracts;

/// <summary>Optionen der Regel-Nachbearbeitung (Port von <c>PolishOptions</c>, <c>TextPolisher.kt</c>).</summary>
/// <param name="Language">"auto" | "de" | "en" | … — steuert die Füllwort-Liste.</param>
/// <param name="CustomFillers">Eigene Füllwörter zusätzlich zur Sprachliste (ganze Wörter).</param>
/// <param name="DisabledFillers">Wörter der eingebauten Liste, die stehen bleiben (klein geschrieben).</param>
/// <param name="KeepLineBreaks">Zeilenumbrüche behalten — für Text, den das Modell gegliedert hat.</param>
public sealed record PolishOptions(
    bool RemoveFillers = true,
    bool AutoCapitalize = true,
    string Language = "auto",
    IReadOnlyCollection<string>? CustomFillers = null,
    IReadOnlySet<string>? DisabledFillers = null,
    bool KeepLineBreaks = false);

/// <summary>
/// Regeln ohne KI: Füllwörter, Großschreibung, Leerzeichen (Port von <c>TextPolisher</c>). Rein und
/// synchron, wirft nie; leere Eingabe ergibt leere Ausgabe.
/// </summary>
public interface ITextPolisher
{
    string Polish(string raw, PolishOptions options);

    /// <summary>Eingebaute Füllwort-Liste der Sprache (für die Einstellungen).</summary>
    IReadOnlyList<string> BuiltinFillers(string language);
}
