using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>
/// W2-ENR, Christof-Entscheidung (hinweise-srv2.md 8): Firmenbudget erreicht (<c>403 budget_exhausted</c>) = klar ablehnen,
/// kein Ersatz-Anbieter. Andere Fehler schalten weiter auf den Fallback.
/// </summary>
public sealed class BudgetNoFallbackTest
{
    private sealed class Backend(string label, Exception? fail = null) : ITranscriptionBackend
    {
        public int Calls { get; private set; }

        public string Label => label;

        public Task<TranscriptResult> TranscribeAsync(AudioBuffer audio, string language, CancellationToken ct)
        {
            Calls++;
            return fail is null ? Task.FromResult(new TranscriptResult(label)) : Task.FromException<TranscriptResult>(fail);
        }
    }

    private static readonly AudioBuffer Audio = new(new short[AudioBuffer.DefaultSampleRate]);

    [Fact]
    public async Task Budget_erschoepft_kein_Fallback_der_Erkennung()
    {
        var second = new Backend("Ersatz");
        var backend = new FallbackBackend(new Backend("Firma", ApiErrors.Http(403, CompanyProvider.BudgetError)), second);
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => backend.TranscribeAsync(Audio, "de", TestContext.Current.CancellationToken));
        Assert.True(CompanyProvider.IsBudgetError(e));
        Assert.Equal(0, second.Calls);
        Assert.False(backend.UsedFallback);
    }

    [Fact]
    public async Task Anderer_403_schaltet_weiter_auf_den_Fallback()
    {
        var second = new Backend("Ersatz");
        var backend = new FallbackBackend(new Backend("Firma", ApiErrors.Http(403, "forbidden")), second);
        Assert.Equal("Ersatz", (await backend.TranscribeAsync(Audio, "de", TestContext.Current.CancellationToken)).Text);
        Assert.Equal(1, second.Calls);
    }
}
