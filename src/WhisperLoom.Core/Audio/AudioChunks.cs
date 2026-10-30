namespace WhisperLoom.Core.Audio;

/// <summary>
/// Zerlegt lange Aufnahmen in Stücke, die einzeln zur Erkennung passen. Port von <c>AudioChunks.kt</c>.
/// Die API nimmt höchstens 25 MB pro Datei; 16 kHz Mono PCM16 sind 32 kB/s, also gut 13 Minuten. Wir
/// schneiden deutlich früher und vor allem an einer LEISEN Stelle — ein Schnitt mitten im Wort kostet
/// sonst an jeder Naht ein paar Silben.
/// </summary>
public static class AudioChunks
{
    /// <summary>Höchstlänge eines Stücks: 5 Min = 9,6 MB WAV, deutlich unter 25 MB (Android: <c>SharedAudioTranscriber</c>).</summary>
    public const int MaxChunkFrames = AudioUtils.SampleRate * 300;

    /// <summary>So weit vor der Grenze wird nach einer Sprechpause zum Schneiden gesucht.</summary>
    public const int CutSearchFrames = AudioUtils.SampleRate * 20;

    /// <summary>So viel leiser als der Fenster-Durchschnitt muss eine Stelle sein, um als Naht zu taugen.</summary>
    private const double QuietFactor = 0.5;

    /// <summary>Halboffene Grenzen eines Stücks in Frames (16 kHz Mono).</summary>
    public readonly record struct Chunk(int StartFrame, int EndFrame)
    {
        public int FrameCount => EndFrame - StartFrame;
    }

    /// <param name="totalFrames">Gesamtlänge der Aufnahme.</param>
    /// <param name="maxFrames">Höchstlänge eines Stücks.</param>
    /// <param name="profile">
    /// Lautstärke-Profil (RMS je <paramref name="framesPerEntry"/> Frames). Leer = es wird stur bei
    /// <paramref name="maxFrames"/> geschnitten.
    /// </param>
    /// <param name="framesPerEntry">wie viele Frames ein Profil-Eintrag abdeckt.</param>
    /// <param name="searchFrames">wie weit vor der Höchstgrenze nach einer leisen Stelle gesucht wird.</param>
    public static IReadOnlyList<Chunk> Plan(
        int totalFrames,
        int maxFrames,
        float[]? profile = null,
        int framesPerEntry = 1,
        int searchFrames = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);
        if (totalFrames <= 0)
        {
            return [];
        }
        if (totalFrames <= maxFrames)
        {
            return [new Chunk(0, totalFrames)];
        }

        var chunks = new List<Chunk>();
        var start = 0;
        while (start < totalFrames)
        {
            var hardEnd = start + maxFrames;
            if (hardEnd >= totalFrames)
            {
                chunks.Add(new Chunk(start, totalFrames));
                break;
            }
            var cut = QuietestCut(hardEnd, start, maxFrames, profile ?? [], framesPerEntry, searchFrames);
            chunks.Add(new Chunk(start, cut));
            start = cut;
        }
        return chunks;
    }

    /// <summary>
    /// Sucht kurz vor der Höchstgrenze die leiseste Stelle. Zwei Sicherungen: (1) nie vor der halben
    /// Höchstlänge schneiden — sonst erzeugt eine Pause am Stückanfang eine Kette von Winz-Stücken;
    /// (2) nur eine deutlich leisere Stelle als der Fenster-Durchschnitt taugt — bei durchgehendem Reden
    /// lieber die volle Länge ausnutzen.
    /// </summary>
    private static int QuietestCut(int hardEnd, int start, int maxFrames, float[] profile, int framesPerEntry, int searchFrames)
    {
        if (profile.Length == 0 || searchFrames <= 0 || framesPerEntry <= 0)
        {
            return hardEnd;
        }
        var earliest = Math.Max(hardEnd - searchFrames, start + maxFrames / 2);
        if (earliest >= hardEnd)
        {
            return hardEnd;
        }

        // Aufgerundet, damit der Eintrag vollständig im Fenster liegt.
        var firstEntry = (earliest + framesPerEntry - 1) / framesPerEntry;
        var lastEntry = Math.Min(hardEnd / framesPerEntry - 1, profile.Length - 1);
        if (firstEntry > lastEntry)
        {
            return hardEnd;
        }

        var bestEntry = firstEntry;
        var bestRms = float.MaxValue;
        var sum = 0.0;
        for (var e = firstEntry; e <= lastEntry; e++)
        {
            sum += profile[e];
            if (profile[e] < bestRms)
            {
                bestRms = profile[e];
                bestEntry = e;
            }
        }
        var average = sum / (lastEntry - firstEntry + 1);
        if (bestRms >= average * QuietFactor)
        {
            return hardEnd;
        }

        // Mitte des leisesten Eintrags — dort ist der Abstand zu beiden Wörtern am größten.
        var cut = bestEntry * framesPerEntry + framesPerEntry / 2;
        return Math.Clamp(cut, earliest, hardEnd);
    }
}
