namespace WhisperLoom.Core.Contracts;

/// <summary>Rohtext einer Erkennung plus — wenn der Erkenner sie meldet — die erkannte Sprache.</summary>
public sealed record TranscriptResult(string Text, string? DetectedLanguage = null)
{
    public override string ToString() => $"TranscriptResult(text={Text.Length} Zeichen, detectedLanguage={DetectedLanguage})";
}

/// <summary>
/// Ergebnis von KI und Regeln (Port von <c>Refined</c>, <c>Dictation.kt</c>) — für ein Diktat wie für das
/// Neu-Verarbeiten aus dem Rohtext.
/// </summary>
/// <param name="Text">Fertiger Text nach den Regeln — mit KI-Fassung oder, ohne KI, aus dem Rohtext.</param>
/// <param name="Model">Womit verbessert wurde, als Anzeige; null = ohne KI.</param>
/// <param name="Skipped">Warum ohne KI, obwohl eine Stufe gewählt war; null = nicht gescheitert.</param>
/// <param name="Note">Hinweis, der den Text nicht betrifft (z. B. online gescheitert, lokal verbessert).</param>
public sealed record RefinedText(
    Refinement Refinement,
    string Text,
    string? Model = null,
    string? Skipped = null,
    string? Note = null)
{
    /// <summary>Mit KI bearbeitet.</summary>
    public bool Refined => Model is not null;

    /// <summary>Ohne Text: ein Log darf nie ein Diktat zeigen.</summary>
    public override string ToString() =>
        $"RefinedText(refinement={Refinement}, text={Text.Length} Zeichen, model={Model}, skipped={Skipped}, note={Note})";
}

/// <summary>Ein fertiges Diktat (Port von <c>Dictation</c>): Rohtext der Erkennung und was daraus wurde.</summary>
/// <param name="Raw">Rohtext der Erkennung, vor KI und Regeln; leer = nichts erkannt.</param>
/// <param name="Language">Wirksame Sprache (bei "auto" die erkannte).</param>
/// <param name="SttLabel">Womit erkannt wurde, als Anzeige (nach einem Ausfall der Fallback-Anbieter).</param>
/// <param name="UsedSttFallback">Der erste Erkenner ist gescheitert, der Fallback hat geliefert.</param>
public sealed record DictationResult(
    string Raw,
    string Language,
    long DurationMs,
    RefinedText Result,
    string SttLabel,
    bool UsedSttFallback = false)
{
    /// <summary>Was ausgegeben wird.</summary>
    public string Text => Result.Text;

    /// <summary>Ohne Text, wie <see cref="RefinedText.ToString"/>.</summary>
    public override string ToString() =>
        $"DictationResult(raw={Raw.Length} Zeichen, language={Language}, durationMs={DurationMs}, " +
        $"result={Result}, sttLabel={SttLabel}, usedSttFallback={UsedSttFallback})";
}
