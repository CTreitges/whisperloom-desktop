namespace WhisperLoom.Core.Output;

/// <summary>Was das Steuerelement mit Tastaturfokus im Zielfenster ist (Windows: UI Automation; Linux: immer unbekannt).</summary>
public enum FocusKind
{
    /// <summary>Ein Eingabefeld, das Text annimmt.</summary>
    Editable,

    /// <summary>Ein Passwortfeld: dort landet ein Diktat nie, weder eingefügt noch kopiert noch im Verlauf.</summary>
    Password,

    /// <summary>Etwas ohne Texteingabe (Knopf, Liste, Desktop).</summary>
    NotEditable,

    /// <summary>Nicht feststellbar (App meldet nichts brauchbar, Linux, Zeitgrenze).</summary>
    Unknown,
}

/// <summary>Wie das Einfügen ausging.</summary>
public enum InsertAttempt
{
    /// <summary>Nicht versucht: kein Einfügen möglich (Linux, Modus „nur Zwischenablage“) oder vorher ausgeschlossen.</summary>
    NotTried,

    /// <summary>Strg+V bzw. Tippen ging ins Ziel.</summary>
    Inserted,

    /// <summary>Ziel weg, geschützt, nicht nach vorn holbar oder Eingabe abgewiesen.</summary>
    Failed,
}

/// <summary>Was mit dem Text geschieht.</summary>
public enum OutputAction
{
    Insert,
    ClipboardWithHint,
    NothingWithHint,
}

/// <summary>Hinweis nach der Ausgabe.</summary>
public enum OutputHint
{
    None,

    /// <summary>„Kopiert – Strg+V“: der Text liegt in der Zwischenablage, weil er nicht eingefügt werden konnte.</summary>
    Copied,

    /// <summary>„Passwortfeld – nicht eingefügt“.</summary>
    PasswordField,
}

/// <param name="Copy">Text (zusätzlich) in der Zwischenablage lassen.</param>
/// <param name="History">In den Verlauf schreiben (nie aus einem Passwortfeld, wie Android <c>HistoryPolicy</c>).</param>
public sealed record OutputPlan(OutputAction Action, bool Copy, OutputHint Hint, bool History);

/// <summary>
/// Ausgabe-Entscheidung (W2-OUT, Entscheidung Christof 2026-11-01): kein Diktat geht verloren. Fokus-Klasse des Ziels ×
/// Ergebnis des Einfügens × <c>output.alwaysCopy</c> → Aktion. OS-frei; Windows liefert die Fokus-Klasse per UI Automation,
/// Linux immer <see cref="FocusKind.Unknown"/> ohne Einfügen. Tabelle: <c>docs/parity-ux.md</c>.
/// </summary>
public static class OutputDecision
{
    /// <summary>
    /// Einfügen versuchen? Nur in ein Eingabefeld oder bei unbekanntem Fokus (viele Apps melden ihre Felder schlecht; der Text
    /// bleibt dann zur Sicherheit auch in der Zwischenablage, siehe <see cref="Decide"/>).
    /// </summary>
    /// <param name="canInsert">false unter Linux bzw. im Modus „nur Zwischenablage“.</param>
    public static bool ShouldInsert(FocusKind focus, bool canInsert) =>
        canInsert && focus is FocusKind.Editable or FocusKind.Unknown;

    public static OutputPlan Decide(FocusKind focus, InsertAttempt attempt, bool alwaysCopy)
    {
        if (focus == FocusKind.Password)
        {
            return new OutputPlan(OutputAction.NothingWithHint, Copy: false, OutputHint.PasswordField, History: false);
        }
        if (attempt == InsertAttempt.Inserted)
        {
            // Unbekannter Fokus: ob der Text wirklich ankam, weiß niemand – er bleibt still auch in der Zwischenablage.
            return new OutputPlan(OutputAction.Insert, Copy: alwaysCopy || focus == FocusKind.Unknown, OutputHint.None, History: true);
        }
        return new OutputPlan(OutputAction.ClipboardWithHint, Copy: true, OutputHint.Copied, History: true);
    }

    /// <summary>
    /// Leerzeichen nach dem Diktat (<c>polish.trailingSpace</c>, Port von <c>WhisperLoomInputMethodService.commitDictation</c>):
    /// angehängt, wenn eingeschaltet und der Text nicht schon auf Leerraum endet. Leer bleibt leer.
    /// </summary>
    public static string WithTrailingSpace(string text, bool trailingSpace) =>
        trailingSpace && text.Length > 0 && !char.IsWhiteSpace(text[^1]) ? text + " " : text;

    /// <summary>Alle Zeilen der Tabelle (Doku und Tests).</summary>
    public static IEnumerable<(FocusKind Focus, InsertAttempt Attempt, bool AlwaysCopy, OutputPlan Plan)> Table() =>
        from focus in Enum.GetValues<FocusKind>()
        from attempt in Enum.GetValues<InsertAttempt>()
        from alwaysCopy in new[] { false, true }
        select (focus, attempt, alwaysCopy, Decide(focus, attempt, alwaysCopy));
}
