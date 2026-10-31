using System.Buffers.Binary;
using System.Runtime.InteropServices;
using WhisperLoom.Core.Audio;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Ein Upload: Mono PCM16 im Speicher, als WAV (OpenAI-kompatibel) oder rohes PCM (ElevenLabs). Port von
/// <c>api/WavUpload.kt</c>; der 44-Byte-Kopf kommt aus <see cref="WavEncoder.Header"/>. Audio kommt nie auf die Platte.
/// </summary>
public sealed class WavUpload(ReadOnlyMemory<short> samples, int sampleRate = AudioBuffer.DefaultSampleRate)
{
    public const int HeaderSize = WavEncoder.HeaderSize;

    public ReadOnlyMemory<short> Samples { get; } = samples;

    public int SampleRate { get; } = sampleRate;

    public int PcmByteCount => Samples.Length * 2;

    public static WavUpload FromBuffer(AudioBuffer audio) => new(audio.Samples, audio.SampleRate);

    /// <summary>PCM16 little-endian.</summary>
    public byte[] PcmBytes()
    {
        var bytes = new byte[PcmByteCount];
        var span = Samples.Span;
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(span).CopyTo(bytes);
        }
        else
        {
            for (var i = 0; i < span.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), span[i]);
        }
        return bytes;
    }

    /// <summary>Kopf (RIFF/WAVE, PCM, mono, 16 bit) + Daten.</summary>
    public byte[] WavBytes()
    {
        var bytes = new byte[HeaderSize + PcmByteCount];
        WavEncoder.Header(PcmByteCount, SampleRate).CopyTo(bytes, 0);
        PcmBytes().CopyTo(bytes, HeaderSize);
        return bytes;
    }
}
