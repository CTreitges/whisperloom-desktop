using WhisperLoom.App.Services;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// WLD-INT-17: „Zugang prüfen“ in den Einstellungen baut je Prüfung einen HttpClient (Proxy kann wechseln) — die Prüfung
/// gibt ihn danach frei. Ein geteilter Client (Einrichtung, lokale Server) bleibt dagegen offen.
/// </summary>
public sealed class AccessCheckerDisposeTest : IAsyncDisposable
{
    private readonly FakeServer _server = new();
    private int _created;
    private int _disposed;

    public AccessCheckerDisposeTest()
    {
        _server.Map("/v1/models", _ => new FakeResponse(200, """{"data":[]}"""));
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private sealed class CountingClient(Action onDispose) : HttpClient
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) onDispose();
            base.Dispose(disposing);
        }
    }

    private HttpClient Create()
    {
        _created++;
        return new CountingClient(() => _disposed++);
    }

    [Fact]
    public async Task Je_Pruefung_wird_der_erzeugte_Client_freigegeben_und_zwei_Pruefungen_gehen()
    {
        var checker = HttpAccessChecker.PerCheck(Create);

        var first = await checker.CheckAsync("custom", _server.Url("/v1"), null, CancellationToken.None);
        Assert.Equal((1, 1), (_created, _disposed));
        var second = await checker.CheckAsync("custom", _server.Url("/v1"), null, CancellationToken.None);

        Assert.Equal(("Access_Ok", 200), (first.MessageKey, first.StatusCode));
        Assert.Equal(("Access_Ok", 200), (second.MessageKey, second.StatusCode));
        Assert.Equal((2, 2), (_created, _disposed));
    }

    [Fact]
    public async Task Ein_geteilter_Client_bleibt_offen()
    {
        using var shared = new CountingClient(() => _disposed++);
        var checker = new HttpAccessChecker(() => shared);

        await checker.CheckAsync("custom", _server.Url("/v1"), null, CancellationToken.None);
        var again = await checker.CheckAsync("custom", _server.Url("/v1"), null, CancellationToken.None);

        Assert.Equal("Access_Ok", again.MessageKey);
        Assert.Equal(0, _disposed);
    }
}
