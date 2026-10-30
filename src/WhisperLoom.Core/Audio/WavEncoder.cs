using System.Buffers.Binary;
using System.Text;

namespace WhisperLoom.Core.Audio;

/// <summary>
/// Erzeugt 16-bit-PCM-WAV für den API-Upload. Port von <c>WavEncoder.kt</c>. Der Kopf ist getrennt vom
/// Datenteil abrufbar, damit lange Aufnahmen gestreamt werden können. Nur im Speicher, nie auf die Platte.
/// </summary>
public static class WavEncoder
{
    public const int HeaderSize = 44;

    /// <summary>WAV-Kopf für <paramref name="dataLen"/> Bytes PCM16 Mono.</summary>
    public static byte[] Header(int dataLen, int sampleRate = AudioUtils.SampleRate)
    {
        var h = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("RIFF", h.AsSpan(0));
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), 36 + dataLen);
        Encoding.ASCII.GetBytes("WAVE", h.AsSpan(8));
        Encoding.ASCII.GetBytes("fmt ", h.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(20), 1);              // PCM
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(22), 1);              // 1 Kanal
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(28), sampleRate * 2); // byte rate = sr * 1 * 2
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(32), 2);              // block align
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(34), 16);             // bits/sample
        Encoding.ASCII.GetBytes("data", h.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(40), dataLen);
        return h;
    }

    /// <summary>Float-Samples [-1,1] als little-endian PCM16.</summary>
    public static byte[] PcmBytes(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            // Wie Kotlin: kappen, zur Null hin abschneiden; NaN wird 0 (.NET-Konvertierung sättigt).
            var s = (int)(Math.Clamp(samples[i], -1f, 1f) * 32767f);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)s);
        }
        return pcm;
    }

    /// <summary>Umkehrung von <see cref="PcmBytes"/>: little-endian PCM16 als Float [-1,1]; ein Rest-Byte zählt nicht.</summary>
    public static float[] Samples(byte[] pcm)
    {
        var output = new float[pcm.Length / 2];
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2)) / 32768f;
        }
        return output;
    }

    public static byte[] Encode(float[] samples, int sampleRate = AudioUtils.SampleRate)
    {
        var pcm = PcmBytes(samples);
        return [.. Header(pcm.Length, sampleRate), .. pcm];
    }
}
