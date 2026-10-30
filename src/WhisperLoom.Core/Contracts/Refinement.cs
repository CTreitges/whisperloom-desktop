namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Woher der Text kommt. Port von <c>RefineWay</c> (<c>Refinement.kt</c>). Desktop nutzt nur
/// <see cref="Dictation"/>; <see cref="Share"/> bleibt für den Einstellungs-Sync mit Android erhalten.
/// </summary>
public enum RefineWay
{
    Dictation,
    Share,
}

/// <summary>Bereinigung beim Glätten — genau die drei Prompt-Varianten der Stufe.</summary>
public enum PolishCleanup
{
    /// <summary>Nur Zeichensetzung: reines Korrektorat, kein Wort fällt weg (Standard).</summary>
    Plain,

    /// <summary>Ohne Füllwörter: die KI lässt Füllwörter, Stotterer und Doppler weg.</summary>
    Clean,

    /// <summary>Lesbar: behutsames Lektorat (<see cref="RefineMode.Readable"/>).</summary>
    Readable,
}

/// <summary>Form beim Zusammenfassen.</summary>
public enum SummarizeForm
{
    /// <summary>Liste ab drei Aufgaben, Terminen oder Fragen, sonst Fließtext (Standard).</summary>
    Auto,

    /// <summary>Wenige Sätze als ein Absatz, ohne Liste.</summary>
    Prose,
}

public static class RefineKeys
{
    public static string Key(this RefineWay way) => way == RefineWay.Dictation ? "dictation" : "share";

    public static RefineWay? WayFromKey(string? key) =>
        Enum.GetValues<RefineWay>().Cast<RefineWay?>().FirstOrDefault(w => w!.Value.Key() == key);

    public static string Key(this PolishCleanup cleanup) => cleanup switch
    {
        PolishCleanup.Plain => "plain",
        PolishCleanup.Clean => "clean",
        PolishCleanup.Readable => "readable",
        _ => throw new ArgumentOutOfRangeException(nameof(cleanup), cleanup, null),
    };

    /// <summary>Ungespeichert und Unbekanntes gilt als <see cref="PolishCleanup.Plain"/>.</summary>
    public static PolishCleanup CleanupFromKey(string? key) =>
        Enum.GetValues<PolishCleanup>().FirstOrDefault(c => c.Key() == key, PolishCleanup.Plain);

    public static string Key(this SummarizeForm form) => form == SummarizeForm.Auto ? "auto" : "prose";

    /// <summary>Ungespeichert und Unbekanntes gilt als <see cref="SummarizeForm.Auto"/>.</summary>
    public static SummarizeForm FormFromKey(string? key) =>
        Enum.GetValues<SummarizeForm>().FirstOrDefault(f => f.Key() == key, SummarizeForm.Auto);
}

/// <summary>
/// Was ein Auftrag wirklich an das Sprachmodell gibt: die Prompt-Variante (<see cref="Mode"/>), ob die KI
/// über Füllwörter entscheidet (<see cref="SmartFillers"/>, nur Glätten · Ohne Füllwörter) und ob Absätze
/// erwünscht sind (<see cref="Paragraphs"/>; bei Zusammenfassen die Form). Port von <c>Refinement.kt</c>.
/// </summary>
public sealed record Refinement(RefineMode Mode, bool SmartFillers = false, bool Paragraphs = true)
{
    public static readonly Refinement Off = new(RefineMode.Off);

    /// <summary>
    /// Die wirksame Verarbeitung der gespeicherten Stufe <paramref name="stage"/> auf dem Weg
    /// <paramref name="way"/>. Die Bereinigung zählt nur beim Glätten, die Form nur beim Zusammenfassen.
    /// Absätze wählt das Diktat je Stufe; Sprachnachrichten sind immer gegliedert. „Prompt“ gliedert selbst.
    /// </summary>
    public static Refinement Of(
        RefineWay way,
        RefineMode stage,
        PolishCleanup cleanup,
        bool paragraphs,
        SummarizeForm form)
    {
        var p = way == RefineWay.Share || paragraphs;
        return stage switch
        {
            RefineMode.Off => Off,
            RefineMode.Polish or RefineMode.Readable => cleanup switch
            {
                PolishCleanup.Clean => new Refinement(RefineMode.Polish, SmartFillers: true, Paragraphs: p),
                PolishCleanup.Readable => new Refinement(RefineMode.Readable, Paragraphs: p),
                _ => new Refinement(RefineMode.Polish, Paragraphs: p),
            },
            RefineMode.Beautify => new Refinement(RefineMode.Beautify, Paragraphs: p),
            RefineMode.Summarize => new Refinement(RefineMode.Summarize, Paragraphs: form == SummarizeForm.Auto),
            RefineMode.Prompt => new Refinement(RefineMode.Prompt),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
    }
}
