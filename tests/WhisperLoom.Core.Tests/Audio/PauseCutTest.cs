using WhisperLoom.Core.Audio;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Audio;

/// <summary>Pause-Cut mit synthetischem PCM im Speicher (nie Datei).</summary>
public sealed class PauseCutTest
{
    private const int Rate = AudioBuffer.DefaultSampleRate;
    private static readonly int Keep = (int)(Rate * PauseCut.KeepMs / 1000);

    /// <summary>Deterministisches „Sprach“-Signal: jedes 10-ms-Fenster weit über der Schwelle, kein Sample gleich null.</summary>
    private static short[] Speech(double seconds, int seed = 1, int rate = Rate) =>
        [.. Enumerable.Range(0, (int)Math.Round(seconds * rate)).Select(i => (short)((((i + seed) * 7919) % 16000) - 8000 | 1))];

    private static short[] Silence(double seconds, short level = 0, int rate = Rate) =>
        [.. Enumerable.Repeat(level, (int)Math.Round(seconds * rate))];

    private static AudioBuffer Buffer(params short[][] parts) => new(parts.SelectMany(p => p).ToArray());

    private static short[] Cut(AudioBuffer audio) => PauseCut.Apply(audio).Samples.ToArray();

    [Fact]
    public void SechsSekundenStilleZwischenSpracheWerdenHerausgeschnitten()
    {
        var a = Speech(1.0, seed: 1);
        var b = Speech(1.5, seed: 2);
        var input = Buffer(a, Silence(6.0), b);

        var output = Cut(input);

        // 6 s kürzer bis auf den Rand von je KeepMs zur Sprache hin.
        Assert.Equal(input.Samples.Length - (6 * Rate - 2 * Keep), output.Length);
        Assert.Equal(a, output[..a.Length]);
        Assert.Equal(b, output[^b.Length..]);
        Assert.All(output[a.Length..^b.Length], s => Assert.Equal(0, s));
        Assert.Equal(2 * Keep, output.Length - a.Length - b.Length);
    }

    [Fact]
    public void StilleVon4Komma9SekundenBleibt()
    {
        var input = Buffer(Speech(1.0), Silence(4.9), Speech(1.0, seed: 3));
        Assert.Same(input, PauseCut.Apply(input));
    }

    [Fact]
    public void GenauFuenfSekundenWerdenGeschnitten()
    {
        var input = Buffer(Speech(1.0), Silence(5.0), Speech(1.0, seed: 3));
        Assert.Equal(input.Samples.Length - (5 * Rate - 2 * Keep), Cut(input).Length);
    }

    [Fact]
    public void LangeStilleAmAnfangUndEndeWirdBisAufDenRandEntfernt()
    {
        var speech = Speech(2.0);
        var output = Cut(Buffer(Silence(5.0), speech, Silence(7.5)));
        Assert.Equal(Keep + speech.Length + Keep, output.Length);
        Assert.Equal(speech, output[Keep..^Keep]);
    }

    [Fact]
    public void KurzeStilleAmAnfangUndEndeBleibt()
    {
        var input = Buffer(Silence(4.0), Speech(1.0), Silence(3.0));
        Assert.Same(input, PauseCut.Apply(input));
    }

    [Fact]
    public void KomplettStilleAufnahmeAbFuenfSekundenWirdLeer()
    {
        // Definiertes Verhalten: leerer Puffer, die Pipeline sendet dann nichts (PauseCutPipelineTest).
        var output = PauseCut.Apply(Buffer(Silence(6.0)));
        Assert.True(output.IsEmpty);
        Assert.Equal(Rate, output.SampleRate);
    }

    [Fact]
    public void KurzeStilleAufnahmeUndLeererPufferBleibenUnveraendert()
    {
        var quiet = Buffer(Silence(4.0));
        Assert.Same(quiet, PauseCut.Apply(quiet));
        var empty = new AudioBuffer(Array.Empty<short>());
        Assert.Same(empty, PauseCut.Apply(empty));
    }

    [Fact]
    public void RauschenUnterDerSchwelleZaehltAlsStille()
    {
        var below = (short)(PauseCut.Threshold * 32768 * 0.9f);
        var input = Buffer(Speech(1.0), Silence(6.0, below), Speech(1.0, seed: 4));
        Assert.Equal(input.Samples.Length - (6 * Rate - 2 * Keep), Cut(input).Length);
    }

    [Fact]
    public void LeiseSpracheUeberDerSchwelleBleibt()
    {
        var above = (short)(PauseCut.Threshold * 32768 * 1.1f);
        var input = Buffer(Speech(1.0), Silence(6.0, above), Speech(1.0, seed: 4));
        Assert.Same(input, PauseCut.Apply(input));
    }

    [Fact]
    public void MehrerePausenUndUnausgerichteteGrenzenLassenJedeSpracheBitGleich()
    {
        var parts = new[] { Speech(0.7031, 5), Speech(1.2345, 6), Speech(0.0503, 7) };
        var input = Buffer(parts[0], Silence(6.2), parts[1], Silence(2.0), Silence(9.0), parts[2]);

        var output = Cut(input);

        // Fenster-Raster (10 ms): je Pausenseite höchstens ein Fenster weniger geschnitten.
        var ideal = input.Samples.Length - ((int)Math.Round(6.2 * Rate) - 2 * Keep) - (11 * Rate - 2 * Keep);
        Assert.InRange(output.Length, ideal, ideal + 4 * Rate * PauseCut.WindowMs / 1000);
        Assert.Equal(parts[0], output[..parts[0].Length]);
        Assert.Equal(parts[2], output[^parts[2].Length..]);
        var middle = output.AsSpan(parts[0].Length).IndexOf(parts[1]);
        Assert.True(middle >= 0, "zweiter Sprachabschnitt fehlt oder ist verändert");
    }

    [Fact]
    public void AndereAbtastrateRechnetMitIhrerEigenenZeit()
    {
        const int rate = 8_000;
        var a = Speech(1.0, 1, rate);
        var input = new AudioBuffer(a.Concat(Silence(6.0, 0, rate)).Concat(Speech(1.0, 2, rate)).ToArray(), rate);
        var output = PauseCut.Apply(input);
        Assert.Equal(rate, output.SampleRate);
        Assert.Equal(input.Samples.Length - (6 * rate - 2 * rate * PauseCut.KeepMs / 1000), output.Samples.Length);
    }
}
