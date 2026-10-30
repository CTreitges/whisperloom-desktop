using System.Net.Http;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/ApiErrorsTest.kt</c>: Wiederholbarkeit und lesbare Meldungen. Java-Netzfehler → .NET-Entsprechung.</summary>
public class ApiErrorsTest
{
    private static HttpRequestException Request(HttpRequestError error, string message = "x") => new(error, message);

    [Fact]
    public void NetzfehlerDarfWiederholtWerden() =>
        Assert.True(ApiErrors.Network(new IOException("timeout")).IsRetryable());

    [Fact]
    public void ServerfehlerDarfWiederholtWerden()
    {
        Assert.True(ApiErrors.Http(500, "boom").IsRetryable());
        Assert.True(ApiErrors.Http(503, "unavailable").IsRetryable());
        Assert.True(ApiErrors.Http(429, "rate limit").IsRetryable());
        Assert.True(ApiErrors.Http(408, "timeout").IsRetryable());
    }

    [Fact]
    public void FalscherKeyBleibtFalsch()
    {
        Assert.False(ApiErrors.Http(401, "invalid api key").IsRetryable());
        Assert.False(ApiErrors.Http(403, "forbidden").IsRetryable());
    }

    [Fact]
    public void UnbekanntesModellBleibtUnbekannt()
    {
        Assert.False(ApiErrors.Http(404, "model not found").IsRetryable());
        Assert.False(ApiErrors.Http(400, "bad request").IsRetryable());
    }

    [Fact]
    public void KaputteBaseUrlIstKeinWiederholungsfall()
    {
        var e = ApiErrors.Network(new UriFormatException("no protocol: /chat/completions"));
        Assert.False(e.IsRetryable());
        Assert.Contains("Base-URL", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no protocol", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FehlenderKeyIstKeinWiederholungsfall() => Assert.False(new ApiNotConfiguredException().IsRetryable());

    [Fact]
    public void FehlendesOfflineModellIstKeinWiederholungsfall() =>
        Assert.False(new LocalNotAvailableException("Modell fehlt").IsRetryable());

    [Fact]
    public void NetzfehlerWerdenUebersetzt()
    {
        Assert.Contains("nicht gefunden", ApiErrors.Describe(Request(HttpRequestError.NameResolutionError)), StringComparison.Ordinal);
        Assert.Contains("nicht erreichbar", ApiErrors.Describe(Request(HttpRequestError.ConnectionError, "refused")), StringComparison.Ordinal);
        Assert.Contains("Zeitüberschreitung", ApiErrors.Describe(new TimeoutException("Read timed out")), StringComparison.Ordinal);
        Assert.DoesNotContain("Einstellungen", ApiErrors.Describe(new TimeoutException("Read timed out")), StringComparison.Ordinal);
        Assert.Contains("nicht erreichbar", ApiErrors.Describe(new TimeoutException("connect timed out")), StringComparison.Ordinal);
        Assert.Contains("TLS", ApiErrors.Describe(Request(HttpRequestError.SecureConnectionError, "bad cert")), StringComparison.Ordinal);
        // Android „Cleartext HTTP traffic … not permitted“ gibt es auf dem Desktop nicht (keine Network-Security-Config).
        Assert.Equal("irgendwas", ApiErrors.Describe(new IOException("irgendwas")));
        Assert.Equal("Netzwerkfehler", ApiErrors.Describe(new IOException("")));
    }

    [Fact]
    public void MeldungDerExceptionIstDieUebersetzung()
    {
        var e = ApiErrors.Network(Request(HttpRequestError.NameResolutionError, "api.example"));
        Assert.Contains("nicht gefunden", e.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(e.InnerException);
    }

    [Fact]
    public void StatusHinweiseFuerEigeneServer()
    {
        Assert.Contains("API-Key", ApiErrors.Http(401, "x").Message, StringComparison.Ordinal);
        Assert.Contains("API-Key", ApiErrors.Http(403, "x").Message, StringComparison.Ordinal);
        Assert.Contains("/v1", ApiErrors.Http(404, "x").Message, StringComparison.Ordinal);
        Assert.Contains("/v1", ApiErrors.Http(404, "not here").Message, StringComparison.Ordinal);
        var model404 = ApiErrors.Http(404, "The model `gpt-xyz` does not exist").Message;
        Assert.Contains("Modell-ID", model404, StringComparison.Ordinal);
        Assert.DoesNotContain("/v1", model404, StringComparison.Ordinal);
        Assert.Contains("WAV", ApiErrors.Http(400, "failed to read audio data").Message, StringComparison.Ordinal);
        Assert.Null(ApiErrors.Hint(400, "bad request"));
        Assert.Null(ApiErrors.Hint(500, "boom"));
        Assert.Equal("API-Fehler 500: boom", ApiErrors.Http(500, "boom").Message);
        Assert.Equal(401, ApiErrors.Http(401, "x").StatusCode);
    }

    [Fact]
    public void GuthabenLeerBekommtEinenHinweis()
    {
        Assert.Contains("Guthaben", ApiErrors.Http(402, "insufficient credits").Message, StringComparison.Ordinal);
        Assert.False(ApiErrors.Http(402, "x").IsRetryable());
    }

    [Fact]
    public void ElevenLabsHinweiseNennenDieBerechtigungStattV1()
    {
        var el = ApiStyle.ElevenLabs;
        Assert.Contains("„Speech to Text“", ApiErrors.Http(401, "Invalid API key", el).Message, StringComparison.Ordinal);
        Assert.Contains("Berechtigung", ApiErrors.Http(403, "missing permission", el).Message, StringComparison.Ordinal);
        Assert.Contains("Guthaben", ApiErrors.Http(402, "x", el).Message, StringComparison.Ordinal);
        Assert.Null(ApiErrors.Hint(404, "Not Found", el));
        Assert.Equal("API-Fehler 404: Not Found", ApiErrors.Http(404, "Not Found", el).Message);
        Assert.Contains("API-Key", ApiErrors.Http(401, "x").Message, StringComparison.Ordinal);
        Assert.Contains("/v1", ApiErrors.Http(404, "x").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AufruferKannDenHinweisErsetzen()
    {
        Assert.Equal("API-Fehler 401: nope — Eigener Hinweis", ApiErrors.HttpWithHint(401, "nope", "Eigener Hinweis").Message);
        Assert.Equal("API-Fehler 401: nope", ApiErrors.HttpWithHint(401, "nope", null).Message);
        Assert.Contains("Server verlangt", ApiErrors.Http(401, "nope").Message, StringComparison.Ordinal);
    }
}
