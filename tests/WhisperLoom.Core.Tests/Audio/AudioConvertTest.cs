using WhisperLoom.Core.Audio;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Audio;

/// <summary>
/// <c>AudioConvert</c> und <c>AudioUtils</c> haben auf Android keine eigenen Tests; diese Fälle
/// prüfen den Port gegen den Kotlin-Code (Happy Path und Ränder).
/// </summary>
public class AudioConvertTest
{
    [Fact]
    public void MonoBleibtUndWirdAufDieBelegtenWerteGekuerzt()
    {
        Assert.Equal(new short[] { 1, 2 }, AudioConvert.DownmixToMono([1, 2, 3], 2, 1));
    }

    [Fact]
    public void StereoWirdGemitteltUndRestFrameFaelltWeg()
    {
        // (100+300)/2, (-3+0)/2 = -1 (ganzzahlig zur Null), der halbe Frame am Ende zählt nicht.
        Assert.Equal(new short[] { 200, -1 }, AudioConvert.DownmixToMono([100, 300, -3, 0, 7], 5, 2));
    }

    [Fact]
    public void OhneKanalWirdAbgelehnt()
    {
        Assert.ThrowsAny<ArgumentException>(() => AudioConvert.DownmixToMono([1], 1, 0));
    }

    [Fact]
    public void GleicheRateKopiertNurDieBelegtenWerte()
    {
        Assert.Equal(new short[] { 5, 6 }, AudioConvert.ResampleLinear([5, 6, 7], 2, 16_000, 16_000));
        Assert.Empty(AudioConvert.ResampleLinear([5, 6], 0, 48_000, 16_000));
    }

    [Fact]
    public void HalbeRateNimmtJedenZweitenWert()
    {
        Assert.Equal(new short[] { 0, 200, 400 }, AudioConvert.ResampleLinear([0, 100, 200, 300, 400, 500], 6, 32_000, 16_000));
    }

    [Fact]
    public void DoppelteRateInterpoliertUndHaeltDenLetztenWert()
    {
        Assert.Equal(new short[] { 0, 50, 100, 100 }, AudioConvert.ResampleLinear([0, 100], 2, 8_000, 16_000));
    }

    [Fact]
    public void LangeAufnahmenLaufenNichtUeber()
    {
        // count * toRate passt nicht in int: 200 000 * 16 000 = 3,2e9.
        var input = new short[200_000];
        Assert.Equal(66_666, AudioConvert.ResampleLinear(input, input.Length, 48_000, 16_000).Length);
    }

    [Fact]
    public void RmsIstDerEffektivwert()
    {
        Assert.Equal(0.5f, AudioConvert.Rms([16384, -16384], 0, 2), 1e-6f);
        Assert.Equal(0f, AudioConvert.Rms([16384], 1, 5));
        Assert.Equal(0.5f, AudioConvert.Rms([16384, -16384], 0, 99), 1e-6f);
    }
}

/// <summary>Stille am Rand trimmen (<c>AudioUtils.trimSilence</c>), auch für den Desktop-Puffer.</summary>
public class AudioUtilsTest
{
    private const int Pad = AudioUtils.SampleRate * 100 / 1000;

    [Fact]
    public void StilleAmRandFaelltWegMitRand()
    {
        var samples = new float[10_000];
        samples[5_000] = 0.5f;
        samples[5_100] = -0.5f;
        var trimmed = AudioUtils.TrimSilence(samples);
        Assert.Equal(5_100 - 5_000 + 1 + 2 * Pad, trimmed.Length);
        Assert.Equal(0.5f, trimmed[Pad]);
    }

    [Fact]
    public void NurStilleUndLeeresBleibenUnveraendert()
    {
        var silence = new float[100];
        Assert.Same(silence, AudioUtils.TrimSilence(silence));
        float[] empty = [];
        Assert.Same(empty, AudioUtils.TrimSilence(empty));
    }

    [Fact]
    public void RandWirdAmAnfangUndEndeGekappt()
    {
        float[] samples = [0f, 0.5f, 0f, 0.5f, 0f];
        Assert.Equal(samples, AudioUtils.TrimSilence(samples));
    }

    [Fact]
    public void PufferWirdWieFloatsGetrimmt()
    {
        var samples = new short[10_000];
        samples[4_000] = 16_384;
        samples[4_010] = -16_384;
        var trimmed = AudioUtils.TrimSilence(new AudioBuffer(samples));
        Assert.Equal(4_010 - 4_000 + 1 + 2 * Pad, trimmed.Samples.Length);
        Assert.Equal(16_384, trimmed.Samples.Span[Pad]);
        Assert.Equal(AudioBuffer.DefaultSampleRate, trimmed.SampleRate);
    }

    [Fact]
    public void PufferRandRichtetSichNachDerAbtastrate()
    {
        var samples = new short[4_000];
        samples[2_000] = 16_384;
        samples[2_001] = 16_384;
        var trimmed = AudioUtils.TrimSilence(new AudioBuffer(samples, 8_000));
        Assert.Equal(2 + 2 * 800, trimmed.Samples.Length);
        Assert.Equal(8_000, trimmed.SampleRate);
    }

    [Fact]
    public void PufferNurStilleBleibtDerselbe()
    {
        var silent = new AudioBuffer(new short[100]);
        Assert.Same(silent, AudioUtils.TrimSilence(silent));
    }
}
