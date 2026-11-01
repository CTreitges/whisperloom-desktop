namespace WhisperLoom.Core.Audio;

/// <summary>Längen-Stufe einer laufenden Aufnahme (Port von <c>DictationSession.Length</c>).</summary>
public enum RecordingLength
{
    Ok,

    /// <summary>Ab <see cref="RecordingLimits.LongMs"/>: Hinweis, dass bald Schluss ist.</summary>
    Long,

    /// <summary>Ab <see cref="RecordingLimits.MaxMs"/>: die Aufnahme hält von selbst an (Weiter gesperrt).</summary>
    Max,
}

/// <summary>
/// Längen-Regeln einer Aufnahme (W2-OUT, Komfort-Parität): zu kurz = Fehlgriff, verwerfen; Deckel nur für die Online-Erkennung
/// (Port von <c>ime/DictationSession.kt:110-132</c>): OpenAI nimmt höchstens 25 MB, bei 16 kHz Mono PCM16 gut 13 min – ein 413
/// gilt als nicht wiederholbar, das Audio wäre weg. Lokal (offline) gibt es keinen Deckel.
/// </summary>
public static class RecordingLimits
{
    /// <summary>Kürzer ist ein Fehlgriff (Kürzel nur angetippt): nichts erkennen, nichts ausgeben.</summary>
    public const long MinMs = 300;

    public const long LongMs = 10 * 60_000L;

    public const long MaxMs = 12 * 60_000L;

    public static bool TooShort(long durationMs) => durationMs < MinMs;

    /// <param name="limited">Online-Erkennung (nur die hat eine Obergrenze).</param>
    public static RecordingLength Length(long recordedMs, bool limited) =>
        !limited ? RecordingLength.Ok
        : recordedMs >= MaxMs ? RecordingLength.Max
        : recordedMs >= LongMs ? RecordingLength.Long
        : RecordingLength.Ok;
}
