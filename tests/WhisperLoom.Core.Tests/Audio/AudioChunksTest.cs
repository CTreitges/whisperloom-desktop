using WhisperLoom.Core.Audio;
using Chunk = WhisperLoom.Core.Audio.AudioChunks.Chunk;

namespace WhisperLoom.Core.Tests.Audio;

/// <summary>
/// Port von <c>AudioChunksTest.kt</c>: Stückelung langer Aufnahmen. Ein Schnitt an der falschen Stelle
/// kostet an jeder Naht Silben — deshalb wird das Finden der leisen Stelle genau festgenagelt.
/// </summary>
public class AudioChunksTest
{
    private static float[] Profile(int size, float value = 0.5f) => Enumerable.Repeat(value, size).ToArray();

    [Fact]
    public void KurzeAufnahmeBleibtEinStueck()
    {
        Assert.Equal([new Chunk(0, 1000)], AudioChunks.Plan(totalFrames: 1000, maxFrames: 5000));
    }

    [Fact]
    public void GenauAufDerGrenzeBleibtEinStueck()
    {
        Assert.Equal([new Chunk(0, 5000)], AudioChunks.Plan(5000, 5000));
    }

    [Fact]
    public void LeereAufnahmeErgibtNichts()
    {
        Assert.Empty(AudioChunks.Plan(0, 5000));
    }

    [Fact]
    public void OhneProfilWirdSturGeschnitten()
    {
        Assert.Equal(
            [new Chunk(0, 100), new Chunk(100, 200), new Chunk(200, 250)],
            AudioChunks.Plan(totalFrames: 250, maxFrames: 100));
    }

    [Fact]
    public void StueckeSindLueckenlosUndUeberlappungsfrei()
    {
        var c = AudioChunks.Plan(totalFrames: 1_234, maxFrames: 100);
        Assert.Equal(0, c[0].StartFrame);
        Assert.Equal(1_234, c[^1].EndFrame);
        for (var i = 1; i < c.Count; i++)
        {
            Assert.Equal(c[i - 1].EndFrame, c[i].StartFrame);
        }
        Assert.Equal(1_234, c.Sum(x => x.FrameCount));
    }

    [Fact]
    public void SchneidetAnDerLeisestenStelle()
    {
        // 10 Frames je Profil-Eintrag; Eintrag 7 (Frames 70..79) ist die Sprechpause.
        var profile = Profile(20);
        profile[7] = 0.01f;
        var c = AudioChunks.Plan(totalFrames: 200, maxFrames: 100, profile: profile, framesPerEntry: 10, searchFrames: 50);
        // Mitte des leisesten Eintrags = 7*10 + 5 = 75
        Assert.Equal(75, c[0].EndFrame);
        Assert.Equal(75, c[1].StartFrame);
    }

    [Fact]
    public void LeiseStelleAusserhalbDesFenstersWirdIgnoriert()
    {
        // Pause bei Eintrag 1 (Frames 10..19) liegt weit vor dem Suchfenster [80,100].
        var profile = Profile(20);
        profile[1] = 0.01f;
        var c = AudioChunks.Plan(totalFrames: 200, maxFrames: 100, profile: profile, framesPerEntry: 10, searchFrames: 20);
        Assert.Equal(100, c[0].EndFrame);
    }

    [Fact]
    public void OhneMerklichLeisereStelleWirdDieVolleLaengeGenutzt()
    {
        // Durchgehendes Reden: es gibt keine gute Naht -> nicht willkürlich früh schneiden.
        var c = AudioChunks.Plan(totalFrames: 250, maxFrames: 100, profile: Profile(40), framesPerEntry: 10, searchFrames: 50);
        Assert.Equal(100, c[0].EndFrame);
        Assert.Equal(200, c[1].EndFrame);
    }

    [Fact]
    public void FruehePauseErzeugtKeineWinzStuecke()
    {
        // Regression: ohne Untergrenze schnitt eine Pause am Stückanfang bei Frame 5,
        // dann bei 6, dann bei 7 ... — hunderte Stücke, jedes eine eigene API-Anfrage.
        var profile = Profile(40);
        profile[0] = 0.0f;
        var c = AudioChunks.Plan(totalFrames: 300, maxFrames: 100, profile: profile, framesPerEntry: 10, searchFrames: 500);
        Assert.Equal(3, c.Count);
        foreach (var chunk in c)
        {
            Assert.True(chunk.FrameCount >= 50, $"Stück zu kurz: {chunk}");
        }
        Assert.Equal(300, c.Sum(x => x.FrameCount));
    }

    [Fact]
    public void SchnittNiemalsVorDerHalbenHoechstlaenge()
    {
        var profile = Profile(40);
        profile[1] = 0.0f; // Pause bei Frames 10..19, also im ersten Viertel
        var c = AudioChunks.Plan(totalFrames: 300, maxFrames: 100, profile: profile, framesPerEntry: 10, searchFrames: 200);
        Assert.True(c[0].EndFrame >= 50, $"Erstes Stück darf nicht vor Frame 50 enden: {c[0]}");
    }

    // --- Desktop: Grenzen ------------------------------------------------------------------

    [Fact]
    public void HoechstlaengeMussPositivSein()
    {
        Assert.ThrowsAny<ArgumentException>(() => AudioChunks.Plan(100, 0));
    }

    [Fact]
    public void EinStueckBleibtUnter25Megabyte()
    {
        // 16 kHz Mono PCM16 = 2 Byte je Frame, dazu der WAV-Kopf.
        Assert.True(AudioChunks.MaxChunkFrames * 2L + WavEncoder.HeaderSize < 25L * 1024 * 1024);
        Assert.True(AudioChunks.CutSearchFrames < AudioChunks.MaxChunkFrames / 2);
    }
}
