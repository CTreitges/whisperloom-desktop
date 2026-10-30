using System.Text;
using WhisperLoom.Core.Audio;

namespace WhisperLoom.Core.Tests.Audio;

/// <summary>Port von <c>WavEncoderTest.kt</c> (Klasse WavEncoderTest): der WAV-Encoder für den API-Upload.</summary>
public class WavEncoderTest
{
    internal static int Le16(byte[] b, int o) => b[o] | (b[o + 1] << 8);

    internal static int Le32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

    internal static string Ascii(byte[] b, int o, int n) => Encoding.ASCII.GetString(b, o, n);

    [Fact]
    public void HeaderGroesseUndFelder()
    {
        var wav = WavEncoder.Encode(new float[10], 16_000);
        Assert.Equal(44 + 20, wav.Length);      // 10 Samples * 2 Byte + 44 Header
        Assert.Equal("RIFF", Ascii(wav, 0, 4));
        Assert.Equal("WAVE", Ascii(wav, 8, 4));
        Assert.Equal("fmt ", Ascii(wav, 12, 4));
        Assert.Equal("data", Ascii(wav, 36, 4));
        Assert.Equal(1, Le16(wav, 20));         // PCM
        Assert.Equal(1, Le16(wav, 22));         // mono
        Assert.Equal(16_000, Le32(wav, 24));    // sample rate
        Assert.Equal(16, Le16(wav, 34));        // bits/sample
        Assert.Equal(20, Le32(wav, 40));        // data-Länge
    }

    [Fact]
    public void LeererInputNurHeader()
    {
        Assert.Equal(44, WavEncoder.Encode([], 16_000).Length);
    }

    [Fact]
    public void SampleWertKorrektKodiert()
    {
        var wav = WavEncoder.Encode([1.0f], 16_000);
        // 1.0 -> 32767 -> little-endian 0xFF 0x7F
        Assert.Equal(0xFF, wav[44]);
        Assert.Equal(0x7F, wav[45]);
    }

    // --- Desktop: Grenzwerte ---------------------------------------------------------------

    [Fact]
    public void UebersteuerungWirdGekapptNegativeWerteImZweierkomplement()
    {
        var pcm = WavEncoder.PcmBytes([2f, -2f, -0.5f, float.NaN]);
        Assert.Equal(new byte[] { 0xFF, 0x7F }, pcm[0..2]);            // +1 -> 32767
        Assert.Equal(new byte[] { 0x01, 0x80 }, pcm[2..4]);            // -1 -> -32767
        Assert.Equal(new byte[] { 0x01, 0xC0 }, pcm[4..6]);            // -0,5 -> -16383 (abgeschnitten)
        Assert.Equal(new byte[] { 0x00, 0x00 }, pcm[6..8]);            // NaN -> 0
    }
}

/// <summary>Port von <c>WavEncoderTest.kt</c> (Klasse WavHeaderTest): der getrennt abrufbare WAV-Kopf.</summary>
public class WavHeaderTest
{
    [Fact]
    public void KopfHatDieRichtigeLaenge()
    {
        Assert.Equal(WavEncoder.HeaderSize, WavEncoder.Header(0).Length);
    }

    [Fact]
    public void KopfTraegtRiffUndDatenlaenge()
    {
        var h = WavEncoder.Header(1000, 16_000);
        Assert.Equal("RIFF", WavEncoderTest.Ascii(h, 0, 4));
        Assert.Equal("WAVE", WavEncoderTest.Ascii(h, 8, 4));
        Assert.Equal("data", WavEncoderTest.Ascii(h, 36, 4));
        Assert.Equal(36 + 1000, WavEncoderTest.Le32(h, 4));   // RIFF-Größe
        Assert.Equal(1000, WavEncoderTest.Le32(h, 40));       // data-Größe
        Assert.Equal(16_000, WavEncoderTest.Le32(h, 24));     // Abtastrate
        Assert.Equal(32_000, WavEncoderTest.Le32(h, 28));     // Byte-Rate = 16000 * 1 * 2
    }

    [Fact]
    public void KopfPlusDatenErgibtGenauDieKodierteDatei()
    {
        float[] samples = [0f, 0.5f, -0.5f, 1f];
        var whole = WavEncoder.Encode(samples);
        var parts = WavEncoder.Header(WavEncoder.PcmBytes(samples).Length).Concat(WavEncoder.PcmBytes(samples)).ToArray();
        Assert.Equal(parts, whole);
    }
}

/// <summary>Port von <c>WavEncoderTest.kt</c> (Klasse WavSamplesTest): der Rückweg PCM16 -> Float.</summary>
public class WavSamplesTest
{
    [Fact]
    public void VollausschlagUndNull()
    {
        var s = WavEncoder.Samples([0xFF, 0x7F, 0, 0, 0, 0x80]);
        Assert.Equal(3, s.Length);
        Assert.Equal(32767f / 32768f, s[0], 1e-6f);
        Assert.Equal(0f, s[1]);
        Assert.Equal(-1f, s[2]);
    }

    [Fact]
    public void UngeradeByteanzahlIgnoriertDenRest()
    {
        Assert.Single(WavEncoder.Samples([1, 0, 5]));
    }
}
