namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Eine Aufnahme im Speicher: Mono PCM16, standardmäßig 16 kHz. Audio kommt nie auf die Platte.
/// <see cref="ToString"/> zeigt nur die Länge, nie Samples.
/// </summary>
public sealed class AudioBuffer
{
    public const int DefaultSampleRate = 16_000;

    public AudioBuffer(ReadOnlyMemory<short> samples, int sampleRate = DefaultSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        Samples = samples;
        SampleRate = sampleRate;
    }

    public ReadOnlyMemory<short> Samples { get; }

    public int SampleRate { get; }

    public long DurationMs => Samples.Length * 1000L / SampleRate;

    public bool IsEmpty => Samples.IsEmpty;

    public override string ToString() => $"AudioBuffer(samples={Samples.Length}, sampleRate={SampleRate})";
}
