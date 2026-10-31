using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Local;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>HTTP-Teil des Begleitprozesses gegen einen Fake-whisper-server: Formular, Antwort, Fehler.</summary>
public sealed class WhisperServerClientTest : IAsyncLifetime
{
    private readonly FakeServer _server = new();
    private readonly HttpClient _http = new();
    private string _form = "";
    private int _status = 200;
    private string _answer = """{"task":"transcribe","language":"english","duration":1.0,"text":" And so my fellow Americans. "}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _server.Map("/inference", r =>
        {
            _form = r.Latin1;
            return new FakeResponse(_status, _answer);
        });
        _server.Map("/health", _ => new FakeResponse(_status, """{"status":"ok"}"""));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private static string Field(string form, string name)
    {
        var start = form.IndexOf($"name={name}", StringComparison.Ordinal);
        if (start < 0) start = form.IndexOf($"name=\"{name}\"", StringComparison.Ordinal);
        if (start < 0) return "";
        var body = form.IndexOf("\r\n\r\n", start, StringComparison.Ordinal) + 4;
        return form[body..form.IndexOf("\r\n--", body, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task ErkenntUndMeldetDieSpracheBeiAuto()
    {
        var r = await WhisperServerClient.TranscribeAsync(_http, _server.Base, new AudioBuffer(new short[32_000]), "auto", "WhisperLoom", accurate: true, Ct);
        Assert.Equal("And so my fellow Americans.", r.Text);
        Assert.Equal("en", r.DetectedLanguage);
        Assert.Equal("verbose_json", Field(_form, "response_format"));
        Assert.Equal("auto", Field(_form, "language"));
        Assert.Equal("5", Field(_form, "beam_size"));
        Assert.Equal("true", Field(_form, "no_timestamps"));
        Assert.Equal("WhisperLoom", Field(_form, "prompt"));
    }

    [Fact]
    public async Task FesteSpracheGreedyOhnePrompt()
    {
        var r = await WhisperServerClient.TranscribeAsync(_http, _server.Base, new AudioBuffer(new short[100]), "de", "", accurate: false, Ct);
        Assert.Null(r.DetectedLanguage);
        Assert.Equal("de", Field(_form, "language"));
        Assert.Equal("1", Field(_form, "beam_size"));
        Assert.Equal("", Field(_form, "prompt"));
    }

    [Fact]
    public async Task KurzesAudioWirdAufEineSekundeAufgefuellt()
    {
        using var form = WhisperServerClient.Form(new AudioBuffer(new short[100]), "de", "", false);
        var wav = await form.First(c => c.Headers.ContentDisposition?.Name?.Trim('"') == "file").ReadAsByteArrayAsync(Ct);
        Assert.Equal(44 + WhisperServerClient.MinSamples * 2, wav.Length);
    }

    [Fact]
    public async Task LangesAudioMitZeitstempeln()
    {
        await WhisperServerClient.TranscribeAsync(_http, _server.Base, new AudioBuffer(new short[WhisperServerClient.WindowSamples + 1]), "de", "", false, Ct);
        Assert.Equal("false", Field(_form, "no_timestamps"));
    }

    [Fact]
    public async Task FehlerstatusWirdApiHttpException()
    {
        _status = 500;
        var e = await Assert.ThrowsAsync<ApiHttpException>(() =>
            WhisperServerClient.TranscribeAsync(_http, _server.Base, new AudioBuffer(new short[10]), "de", "", false, Ct));
        Assert.Equal(500, e.StatusCode);
        Assert.False(await WhisperServerClient.HealthAsync(_http, _server.Base, Ct));
    }

    [Fact]
    public async Task NichtErreichbarIstNetzfehler()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var dead = $"http://127.0.0.1:{((System.Net.IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();
        await Assert.ThrowsAsync<ApiNetworkException>(() =>
            WhisperServerClient.TranscribeAsync(_http, dead, new AudioBuffer(new short[10]), "de", "", false, Ct));
        Assert.False(await WhisperServerClient.HealthAsync(_http, dead, Ct));
    }

    [Fact]
    public void AntwortMitFehlerfeld()
    {
        Assert.Throws<ApiHttpException>(() => WhisperServerClient.Parse("""{"error":"failed to read audio"}""", "de"));
        Assert.Equal("", WhisperServerClient.Parse("{}", "de").Text);
        Assert.Equal("de", WhisperServerClient.Parse(new JsonObject { ["text"] = "x", ["language"] = "german" }.ToJsonString(), "auto").DetectedLanguage);
    }
}
