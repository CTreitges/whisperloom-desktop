namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Ein Diktat-Auftrag an die Pipeline: Aufnahme, Erkennungssprache und die wirksame Verarbeitung
/// (<see cref="Refinement.Of"/> auf dem Weg <see cref="RefineWay.Dictation"/>).
/// </summary>
/// <param name="Language">"auto" oder ISO-Code ("de", "en", …). Die Prompt-Sprache folgt ihr (de → DE, sonst EN).</param>
public sealed record DictationRequest(AudioBuffer Audio, string Language, Refinement Refinement)
{
    public override string ToString() =>
        $"DictationRequest(audio={Audio}, language={Language}, refinement={Refinement})";
}
