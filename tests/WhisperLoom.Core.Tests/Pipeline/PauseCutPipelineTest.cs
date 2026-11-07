using System.Buffers.Binary;
using WhisperLoom.Core.Audio;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>
/// Die echte Pipeline schneidet lange Pausen vor dem Upload (<see cref="PauseCut"/>): gemessen an der WAV, die beim
/// Fake-Server „eigener Server“ ankommt.
/// </summary>
public sealed class PauseCutPipelineTest : IAsyncLifetime
{
    private const int Rate = AudioBuffer.DefaultSampleRate;

    private readonly PipelineHarness _h = new();
    private FakeRequest? _stt;

    public ValueTask InitializeAsync()
    {
        _h.Server.Map("/v1/audio/transcriptions", r =>
        {
            _stt = r;
            return new FakeResponse(200, """{"text":"hallo welt"}""");
        });
        _h.UseLocalServer();
        _h.Stage(RefineMode.Off);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static short[] Speech(double seconds) =>
        [.. Enumerable.Range(0, (int)(seconds * Rate)).Select(i => (short)(((i * 7919) % 16000) - 8000 | 1))];

    private static short[] Silence(double seconds) => new short[(int)(seconds * Rate)];

    private Task<DictationResult> Run(params short[][] parts) =>
        _h.Pipeline.RunAsync(
            new DictationRequest(new AudioBuffer(parts.SelectMany(p => p).ToArray()), "de",
                SettingsAccess.RefinementFor(_h.Settings, RefineWay.Dictation)),
            TestContext.Current.CancellationToken);

    /// <summary>Dauer der hochgeladenen WAV (Datenblock nach dem 44-Byte-Kopf, PCM16 Mono).</summary>
    private long SentMs()
    {
        var body = _stt!.Body;
        var riff = _stt.Latin1.IndexOf("RIFF", StringComparison.Ordinal);
        Assert.True(riff >= 0, "keine WAV im Upload");
        var dataLen = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(riff + 40, 4));
        return dataLen / 2 * 1000L / Rate;
    }

    [Fact]
    public async Task LangePauseWirdVorDemUploadGeschnitten()
    {
        var result = await Run(Speech(1.0), Silence(6.0), Speech(1.0));

        Assert.Equal("hallo welt", result.Raw);
        Assert.Equal(8_000, result.DurationMs); // aufgenommene Dauer bleibt für den Verlauf
        Assert.Equal(2_000 + 2 * PauseCut.KeepMs, SentMs());
        Assert.True(SentMs() < result.DurationMs);
    }

    [Fact]
    public async Task KurzePauseGehtUnveraendertRaus()
    {
        await Run(Speech(1.0), Silence(4.0), Speech(1.0));
        Assert.Equal(6_000, SentMs());
    }

    [Fact]
    public async Task NurStilleSendetNichtsUndLiefertLeerenText()
    {
        var result = await Run(Silence(6.0));

        Assert.Null(_stt);
        Assert.Equal("", result.Raw);
        Assert.Equal("", result.Result.Text);
        Assert.Equal(6_000, result.DurationMs);
    }
}
