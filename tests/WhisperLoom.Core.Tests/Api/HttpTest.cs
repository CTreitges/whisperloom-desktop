using System.Text;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/HttpTest.kt</c> gegen den lokalen <see cref="FakeServer"/>.</summary>
public sealed class HttpTest : IAsyncLifetime
{
    private readonly FakeServer _server = new();
    private readonly HttpClient _http = Http.CreateClient();
    private FakeRequest? _last;

    public ValueTask InitializeAsync()
    {
        _server.Map("/ok", r =>
        {
            _last = r;
            return new FakeResponse(200, """{"text":"hi"}""");
        });
        _server.Map("/unauthorized", _ => new FakeResponse(401, """{"error":{"message":"invalid api key"}}"""));
        _server.Map("/missing", _ => new FakeResponse(404, "not here"));
        _server.MapAsync("/slow", async _ =>
        {
            await Task.Delay(1_500);
            return new FakeResponse(200, "{}");
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private static readonly byte[] X = Encoding.UTF8.GetBytes("x");

    private Task<string> Post(string url, string apiKey, string? authHeader = null, int readTimeoutMs = Http.DefaultReadTimeoutMs) =>
        Http.PostAsync(_http, url, apiKey, X, "text/plain", new HttpCallOptions(ReadTimeoutMs: readTimeoutMs, AuthHeader: authHeader),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task OhneKeyKeinAuthorizationHeader()
    {
        Assert.Equal("""{"text":"hi"}""", await Post(_server.Url("/ok"), ""));
        Assert.Null(_last!.Header("Authorization"));
        Assert.Equal("x", _last.Text);
    }

    [Fact]
    public async Task MitKeyBearerHeader()
    {
        await Post(_server.Url("/ok"), Keys.K1);
        Assert.Equal($"Bearer {Keys.K1}", _last!.Header("Authorization"));
    }

    [Fact]
    public async Task EigenerKeyHeaderStattBearer()
    {
        await Post(_server.Url("/ok"), Keys.K1, "xi-api-key");
        Assert.Equal(Keys.K1, _last!.Header("xi-api-key"));
        Assert.Null(_last.Header("Authorization"));

        await Http.GetAsync(_http, _server.Url("/ok"), Keys.K2, new HttpCallOptions(AuthHeader: "xi-api-key"), TestContext.Current.CancellationToken);
        Assert.Equal(Keys.K2, _last!.Header("xi-api-key"));
        Assert.Null(_last.Header("Authorization"));

        await Post(_server.Url("/ok"), "", "xi-api-key");
        Assert.Null(_last!.Header("xi-api-key"));
    }

    [Fact]
    public async Task LeerzeichenKeyZaehltAlsKeinKey()
    {
        await Post(_server.Url("/ok"), "   ");
        Assert.Null(_last!.Header("Authorization"));
    }

    [Fact]
    public async Task EigenerUserAgent()
    {
        await Post(_server.Url("/ok"), "");
        Assert.StartsWith("WhisperLoom/", _last!.Header("User-Agent"), StringComparison.Ordinal);
        await Http.GetAsync(_http, _server.Url("/ok"), "", null, TestContext.Current.CancellationToken);
        Assert.StartsWith("WhisperLoom/", _last!.Header("User-Agent"), StringComparison.Ordinal);
    }

    [Fact]
    public void OllamaFehlerformWirdLesbar()
    {
        Assert.Equal("Unauthorized", Http.ErrorDetail("""{"error":"Unauthorized"}"""));
        Assert.Equal("invalid api key", Http.ErrorDetail("""{"error":{"message":"invalid api key"}}"""));
        Assert.Equal("keine Antwort", Http.ErrorDetail(""));
    }

    [Fact]
    public void ElevenLabsFehlerformenWerdenLesbar()
    {
        Assert.Equal("Invalid API key", Http.ErrorDetail("""{"detail":{"type":"authentication_error","code":"unauthorized","message":"Invalid API key","status":"invalid_api_key"}}"""));
        Assert.Equal(
            "field required; value is not a valid enumeration member",
            Http.ErrorDetail("""{"detail":[{"loc":["body","model_id"],"msg":"field required","type":"missing"},{"msg":"value is not a valid enumeration member"}]}"""));
        Assert.Equal("Not Found", Http.ErrorDetail("""{"detail":"Not Found"}"""));
        Assert.Equal("""{"detail":{}}""", Http.ErrorDetail("""{"detail":{}}"""));
        Assert.Equal("""{"detail":[]}""", Http.ErrorDetail("""{"detail":[]}"""));
    }

    /// <summary>Kotlin prüft zusätzlich <c>e.body</c> — der Vertrag übernimmt den Antwort-Körper bewusst nicht.</summary>
    [Fact]
    public async Task FehlerstatusWirdLesbar()
    {
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => Post(_server.Url("/unauthorized"), Keys.K1));
        Assert.Equal(401, e.StatusCode);
        Assert.StartsWith("API-Fehler 401: invalid api key", e.Message, StringComparison.Ordinal);
        Assert.Contains("API-Key", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NichtJsonFehlerBodyBleibtAlsText()
    {
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => Post(_server.Url("/missing"), ""));
        Assert.Equal(404, e.StatusCode);
        Assert.StartsWith("API-Fehler 404: not here", e.Message, StringComparison.Ordinal);
        Assert.Contains("/v1", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadTimeoutWirdZurZeitueberschreitung()
    {
        var e = await Assert.ThrowsAsync<ApiNetworkException>(() => Post(_server.Url("/slow"), "", readTimeoutMs: 200));
        Assert.Contains("Zeitüberschreitung", e.Message, StringComparison.Ordinal);
        Assert.IsType<TimeoutException>(e.InnerException);
    }

    [Fact]
    public async Task VerbindungVerweigertWirdErklaert()
    {
        var e = await Assert.ThrowsAsync<ApiNetworkException>(() => Post("http://127.0.0.1:1/x", ""));
        Assert.Contains("nicht erreichbar", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UngueltigeUrlWirdZumNetzfehler()
    {
        var e = await Assert.ThrowsAsync<ApiNetworkException>(() => Post("", ""));
        Assert.IsType<UriFormatException>(e.InnerException);
        Assert.False(e.IsRetryable());
    }

    [Fact]
    public void EndpunktTrimmtSchraegstriche()
    {
        Assert.Equal("https://x/v1/audio/transcriptions", Http.Endpoint(" https://x/v1/ ", "/audio/transcriptions"));
        Assert.Equal("https://x/v1/chat/completions", Http.Endpoint("https://x/v1", "/chat/completions"));
    }

    /// <summary>Desktop: Abbruch über den Token bleibt Abbruch und wird nie zum Netzfehler (Vertrag „Threading“).</summary>
    [Fact]
    public async Task AbbruchBleibtAbbruch()
    {
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Http.PostAsync(_http, _server.Url("/slow"), "", X, "text/plain", null, cts.Token));
    }
}
