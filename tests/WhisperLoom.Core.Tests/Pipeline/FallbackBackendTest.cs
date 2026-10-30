using System.Net.Http;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>Port von <c>FallbackBackendTest.kt</c>: der Fallback der Erkennung als Hülle um zwei Backends.</summary>
public class FallbackBackendTest
{
    private sealed class Fake(string label, Exception? fail = null, string? text = null) : ITranscriptionBackend
    {
        public int Calls { get; private set; }

        public Exception? Fail { get; set; } = fail;

        public string Label => label;

        public Task<TranscriptResult> TranscribeAsync(AudioBuffer audio, string language, CancellationToken ct)
        {
            Calls++;
            if (Fail is not null) throw Fail;
            return Task.FromResult(new TranscriptResult(text ?? label));
        }
    }

    private static readonly AudioBuffer Audio = new(new short[AudioBuffer.DefaultSampleRate]);

    private static Task<TranscriptResult> Run(ITranscriptionBackend b, CancellationToken? ct = null) =>
        b.TranscribeAsync(Audio, "de", ct ?? TestContext.Current.CancellationToken);

    [Fact]
    public async Task DerErsteErkenntDerFallbackSchweigt()
    {
        var second = new Fake("Groq");
        var backend = new FallbackBackend(new Fake("OpenAI"), second);
        Assert.Equal("OpenAI", (await Run(backend)).Text);
        Assert.Equal(0, second.Calls);
        Assert.False(backend.UsedFallback);
        Assert.Equal("OpenAI", backend.Label);
        Assert.Null(DictationPipeline.SttFallbackNote(backend));
    }

    [Fact]
    public async Task ScheitertDerErsteErkenntDerFallbackDieselbeAufnahme()
    {
        var backend = new FallbackBackend(new Fake("OpenAI", ApiErrors.Http(429, "Rate limit")), new Fake("Groq"));
        Assert.Equal("Groq", (await Run(backend)).Text);
        Assert.True(backend.UsedFallback);
        Assert.Equal("Groq", backend.Label);
        Assert.Equal("Spracherkennung fehlgeschlagen — mit Fallback Groq erkannt", DictationPipeline.SttFallbackNote(backend));
    }

    [Fact]
    public async Task NachDemAusfallBleibtEsBeimFallback()
    {
        var first = new Fake("OpenAI", ApiErrors.Network(new TimeoutException("Read timed out")));
        var second = new Fake("Groq");
        var backend = new FallbackBackend(first, second);
        for (var i = 0; i < 3; i++) await Run(backend);
        Assert.Equal(1, first.Calls);
        Assert.Equal(3, second.Calls);
    }

    [Fact]
    public async Task ScheiternBeideGehtDerFehlerDesErstenRaus()
    {
        var firstError = ApiErrors.Http(401, "Invalid key");
        var secondError = ApiErrors.Network(new HttpRequestException(HttpRequestError.NameResolutionError, "api.groq.com"));
        var backend = new FallbackBackend(new Fake("OpenAI", firstError), new Fake("Groq", secondError));
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => Run(backend));
        Assert.Same(firstError, e);
        Assert.Same(secondError, FallbackBackend.Suppressed(e));
        Assert.False(backend.UsedFallback);
    }

    /// <summary>Kotlin: Thread unterbrochen. C#: der Token ist abgebrochen — dann kein zweiter Upload.</summary>
    [Fact]
    public async Task NachAbbruchKeinZweiterUpload()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var second = new Fake("Groq");
        var backend = new FallbackBackend(new Fake("OpenAI", new OperationCanceledException(cts.Token)), second);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(backend, cts.Token));
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task HinfaelligerAuftragSchicktNichtsAnDenFallback()
    {
        var second = new Fake("Groq");
        var backend = new FallbackBackend(new Fake("OpenAI", ApiErrors.Network(new TimeoutException("Read timed out"))), second)
        {
            Cancelled = () => true,
        };
        await Assert.ThrowsAsync<ApiNetworkException>(() => Run(backend));
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public void OhneFallbackKeinHinweis() => Assert.Null(DictationPipeline.SttFallbackNote(new Fake("OpenAI")));

    /// <summary>Desktop: auch ein Netzfehler NACH einem Abbruch des Tokens schickt nichts an den Fallback.</summary>
    [Fact]
    public async Task NetzfehlerNachAbbruchSchicktNichtsAnDenFallback()
    {
        using var cts = new CancellationTokenSource();
        var first = new Fake("OpenAI", ApiErrors.Network(new IOException("abgebrochen")));
        var second = new Fake("Groq");
        await cts.CancelAsync();
        await Assert.ThrowsAsync<ApiNetworkException>(() => Run(new FallbackBackend(first, second), cts.Token));
        Assert.Equal(0, second.Calls);
    }
}
