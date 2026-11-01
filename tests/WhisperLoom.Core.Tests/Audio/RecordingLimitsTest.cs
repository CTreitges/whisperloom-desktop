using WhisperLoom.Core.Audio;

namespace WhisperLoom.Core.Tests.Audio;

/// <summary>Längen-Regeln (Port der Fälle aus Android <c>DictationSessionTest</c>: Stufen, offline ohne Deckel) + Mindestlänge.</summary>
public class RecordingLimitsTest
{
    [Theory]
    [InlineData(0, true, RecordingLength.Ok)]
    [InlineData(10 * 60_000L - 1, true, RecordingLength.Ok)]
    [InlineData(10 * 60_000L, true, RecordingLength.Long)]
    [InlineData(12 * 60_000L - 1, true, RecordingLength.Long)]
    [InlineData(12 * 60_000L, true, RecordingLength.Max)]
    [InlineData(60 * 60_000L, false, RecordingLength.Ok)] // offline: kein Deckel
    public void Stufen(long ms, bool limited, RecordingLength expected) =>
        Assert.Equal(expected, RecordingLimits.Length(ms, limited));

    [Theory]
    [InlineData(0, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(5000, false)]
    public void ZuKurz(long ms, bool expected) => Assert.Equal(expected, RecordingLimits.TooShort(ms));
}
