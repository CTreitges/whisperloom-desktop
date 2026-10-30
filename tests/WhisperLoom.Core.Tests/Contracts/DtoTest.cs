using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Contracts;

/// <summary>DTOs zeigen in ToString nie Diktattext — Logs dürfen kein Diktat enthalten.</summary>
public class DtoTest
{
    private const string Secret = "geheimer diktattext";

    [Fact]
    public void ToStringOhneText()
    {
        var refined = new RefinedText(new Refinement(RefineMode.Polish), Secret, Model: "m");
        var objects = new object[]
        {
            new TranscriptResult(Secret, "de"),
            refined,
            new DictationResult(Secret, "de", 1200, refined, "OpenAI"),
            new HistoryEntry("1", DateTimeOffset.UnixEpoch, "de", 1200, Secret, Secret, refined.Refinement, "OpenAI"),
        };

        foreach (var o in objects)
        {
            Assert.DoesNotContain(Secret, o.ToString());
        }
    }

    [Fact]
    public void AudioBufferRechnetDauerUndPrueftRate()
    {
        var audio = new AudioBuffer(new short[8_000]);

        Assert.Equal(500, audio.DurationMs);
        Assert.False(audio.IsEmpty);
        Assert.True(new AudioBuffer(ReadOnlyMemory<short>.Empty).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioBuffer(new short[1], 0));
    }

    [Fact]
    public void SecretNamesSindFest()
    {
        Assert.Equal(["stt", "stt_fallback", "llm", "llm_fallback"], SecretNames.All);
    }
}
