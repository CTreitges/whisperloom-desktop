using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.Core.Tests.Contracts;

public class FakeDictationPipelineTest
{
    private static readonly AudioBuffer OneSecond = new(new short[16_000]);

    private static DictationRequest Request(RefineMode mode, string language = "de") =>
        new(OneSecond, language, new Refinement(mode));

    [Fact]
    public async Task LiefertRohUndErgebnisJeStufe()
    {
        var fake = new FakeDictationPipeline { RawText = "hallo welt" };

        var result = await fake.RunAsync(Request(RefineMode.Prompt), TestContext.Current.CancellationToken);

        Assert.Equal("hallo welt", result.Raw);
        Assert.Equal("[prompt] hallo welt", result.Text);
        Assert.Equal(FakeDictationPipeline.ModelLabel, result.Result.Model);
        Assert.True(result.Result.Refined);
        Assert.Equal(1000, result.DurationMs);
        Assert.Equal(1, fake.Runs);
    }

    [Fact]
    public async Task StufeAusGibtRohtextOhneModell()
    {
        var fake = new FakeDictationPipeline { RawText = "roh" };

        var result = await fake.RunAsync(Request(RefineMode.Off, "auto"), TestContext.Current.CancellationToken);

        Assert.Equal("roh", result.Text);
        Assert.False(result.Result.Refined);
        Assert.Equal("de", result.Language);
    }

    [Theory]
    [InlineData(FakeFailure.SttHttp, typeof(ApiHttpException))]
    [InlineData(FakeFailure.SttNetwork, typeof(ApiNetworkException))]
    [InlineData(FakeFailure.SttNotConfigured, typeof(ApiNotConfiguredException))]
    [InlineData(FakeFailure.SttLocalNotAvailable, typeof(LocalNotAvailableException))]
    public async Task ErkennungsfehlerWerdenGeworfen(FakeFailure failure, Type expected)
    {
        var fake = new FakeDictationPipeline { Failure = failure };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => fake.RunAsync(Request(RefineMode.Polish), TestContext.Current.CancellationToken));

        Assert.IsType(expected, ex);
    }

    [Fact]
    public async Task VerbesserungsfehlerVerschlucktDasDiktatNicht()
    {
        var fake = new FakeDictationPipeline { RawText = "roh", Failure = FakeFailure.Refine };

        var result = await fake.RunAsync(Request(RefineMode.Beautify), TestContext.Current.CancellationToken);

        Assert.Equal("roh", result.Text);
        Assert.NotNull(result.Result.Skipped);
        Assert.False(result.Result.Refined);
    }

    [Fact]
    public async Task AbbruchWirdBeachtet()
    {
        var fake = new FakeDictationPipeline { Delay = TimeSpan.FromSeconds(30) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.RunAsync(Request(RefineMode.Polish), cts.Token));
    }

    [Fact]
    public async Task NeuVerarbeitenAusDemRohtext()
    {
        var fake = new FakeDictationPipeline();

        var refined = await fake.RefineAsync("alt", "en", new Refinement(RefineMode.Summarize), TestContext.Current.CancellationToken);

        Assert.Equal("[summarize] alt", refined.Text);
    }
}
