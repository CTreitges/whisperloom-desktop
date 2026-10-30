namespace WhisperLoom.Core.Audio;

/// <summary>
/// PCM-Umrechnungen. Port von <c>AudioConvert.kt</c>. Audio kommt in beliebiger Abtastrate und
/// Kanalzahl vom Gerät; die Erkennung will 16 kHz Mono.
/// </summary>
public static class AudioConvert
{
    public const int TargetRate = AudioUtils.SampleRate;

    /// <summary>
    /// Mischt verschachtelte Mehrkanal-Samples zu Mono. <paramref name="valueCount"/> ist die Anzahl der
    /// belegten Werte in <paramref name="interleaved"/> (nicht der Frames).
    /// </summary>
    public static short[] DownmixToMono(short[] interleaved, int valueCount, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        if (channels == 1)
        {
            return CopyOf(interleaved, valueCount);
        }
        var frames = valueCount / channels;
        var output = new short[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += interleaved[f * channels + c];
            }
            output[f] = (short)(sum / channels);
        }
        return output;
    }

    /// <summary>Lineare Interpolation auf eine andere Abtastrate — für Sprache völlig ausreichend.</summary>
    public static short[] ResampleLinear(short[] input, int count, int fromRate, int toRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toRate);
        if (count <= 0)
        {
            return [];
        }
        if (fromRate == toRate)
        {
            return CopyOf(input, count);
        }
        // long, damit lange Aufnahmen nicht überlaufen.
        var outCount = Math.Max((int)((long)count * toRate / fromRate), 1);
        var output = new short[outCount];
        var step = (double)fromRate / toRate;
        for (var i = 0; i < outCount; i++)
        {
            var pos = i * step;
            var idx = (int)pos;
            if (idx >= count - 1)
            {
                output[i] = input[count - 1];
            }
            else
            {
                var frac = pos - idx;
                double a = input[idx];
                double b = input[idx + 1];
                output[i] = (short)(int)(a + (b - a) * frac);
            }
        }
        return output;
    }

    /// <summary>Effektivwert (0..1) eines Bereichs — Grundlage für das Finden leiser Stellen.</summary>
    public static float Rms(short[] samples, int from, int to)
    {
        var end = Math.Min(to, samples.Length);
        if (from >= end)
        {
            return 0f;
        }
        var sum = 0.0;
        for (var i = from; i < end; i++)
        {
            var v = samples[i] / 32768.0;
            sum += v * v;
        }
        return (float)Math.Sqrt(sum / (end - from));
    }

    /// <summary>Kotlins <c>copyOf(n)</c>: die ersten n Werte, mit Nullen aufgefüllt.</summary>
    private static short[] CopyOf(short[] source, int length)
    {
        var copy = new short[length];
        Array.Copy(source, copy, Math.Min(length, source.Length));
        return copy;
    }
}
