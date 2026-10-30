namespace WhisperLoom.App.Audio;

/// <summary>Umrechnungen im Speicher: Float → PCM16, Kanäle mischen, auf 16 kHz bringen, Pegel.</summary>
internal static class AudioMath
{
    public static short ToPcm16(float sample) => (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);

    /// <summary>Interleaved-Kanäle zu Mono (Mittelwert).</summary>
    public static float[] Downmix(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels == 1)
        {
            return interleaved.ToArray();
        }
        var mono = new float[interleaved.Length / channels];
        for (var i = 0; i < mono.Length; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                sum += interleaved[(i * channels) + c];
            }
            mono[i] = sum / channels;
        }
        return mono;
    }

    /// <summary>
    /// Lineare Interpolation auf <paramref name="toRate"/>; beim Herunterrechnen vorher ein gleitender Mittelwert
    /// über das Verhältnis als einfacher Tiefpass (Sprache, kein HiFi).
    /// </summary>
    public static short[] Resample(ReadOnlySpan<short> input, int fromRate, int toRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toRate);
        if (fromRate == toRate || input.IsEmpty)
        {
            return input.ToArray();
        }
        var source = new float[input.Length];
        var window = Math.Max(1, fromRate / toRate);
        double running = 0;
        for (var i = 0; i < input.Length; i++)
        {
            running += input[i];
            if (i >= window)
            {
                running -= input[i - window];
            }
            source[i] = (float)(running / Math.Min(i + 1, window));
        }
        var length = (int)((long)input.Length * toRate / fromRate);
        var output = new short[length];
        var step = (double)fromRate / toRate;
        for (var i = 0; i < length; i++)
        {
            var pos = i * step;
            var index = (int)pos;
            var frac = pos - index;
            var a = source[index];
            var b = index + 1 < source.Length ? source[index + 1] : a;
            output[i] = (short)Math.Round(a + ((b - a) * frac));
        }
        return output;
    }

    /// <summary>Pegel 0..1 aus dem Effektivwert, für die Anzeige leicht angehoben (Sprache liegt meist bei 0,05–0,2).</summary>
    public static double Level(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }
        double sum = 0;
        foreach (var s in samples)
        {
            var v = s / (double)short.MaxValue;
            sum += v * v;
        }
        var rms = Math.Sqrt(sum / samples.Length);
        return Math.Clamp(Math.Sqrt(rms) * 1.4, 0, 1);
    }
}
