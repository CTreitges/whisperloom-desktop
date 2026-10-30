using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Audio;

/// <summary>Audio-Hilfen. Port von <c>AudioUtils.kt</c>.</summary>
public static class AudioUtils
{
    public const int SampleRate = AudioBuffer.DefaultSampleRate;

    /// <summary>
    /// Schneidet führende/abschließende Stille weg (kürzeres Audio -> schnellere Transkription), lässt
    /// <paramref name="padMs"/> ms Rand stehen, damit keine Silben abgeschnitten werden. Nur-Stille
    /// bleibt unverändert.
    /// </summary>
    public static float[] TrimSilence(float[] samples, float threshold = 0.008f, int padMs = 100)
    {
        var range = SpeechRange(samples.Length, i => Math.Abs(samples[i]), threshold, SampleRate * padMs / 1000);
        return range is { } r ? samples[r.Start..r.End] : samples;
    }

    /// <summary>
    /// Wie <see cref="TrimSilence(float[], float, int)"/> für den Aufnahme-Puffer (PCM16; Pegel = Sample / 32768).
    /// Der Rand richtet sich nach der Abtastrate des Puffers.
    /// </summary>
    public static AudioBuffer TrimSilence(AudioBuffer audio, float threshold = 0.008f, int padMs = 100)
    {
        var samples = audio.Samples;
        var range = SpeechRange(
            samples.Length, i => Math.Abs(samples.Span[i] / 32768f), threshold, audio.SampleRate * padMs / 1000);
        return range is { } r ? new AudioBuffer(samples[r.Start..r.End], audio.SampleRate) : audio;
    }

    /// <summary>Bereich [start, end) mit Rand, oder null bei leerem Audio und Nur-Stille.</summary>
    private static (int Start, int End)? SpeechRange(int length, Func<int, float> level, float threshold, int pad)
    {
        if (length == 0)
        {
            return null;
        }
        var start = 0;
        var end = length - 1;
        while (start < end && level(start) < threshold)
        {
            start++;
        }
        while (end > start && level(end) < threshold)
        {
            end--;
        }
        if (end <= start)
        {
            return null; // nur Stille -> unverändert
        }
        return (Math.Max(start - pad, 0), Math.Min(end + pad, length - 1) + 1);
    }
}
