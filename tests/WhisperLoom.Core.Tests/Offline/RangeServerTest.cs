using System.Net;
using System.Net.Sockets;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>
/// Beenden des <see cref="RangeServer"/>: <c>DisposeAsync</c> stoppt den Listener, während die Accept-Schleife läuft.
/// Trifft der Stopp die Lücke zwischen Schleifenprüfung und Accept-Aufruf, wirft <see cref="TcpListener"/>
/// <see cref="InvalidOperationException"/> („Not listening“) — die Ursache der sporadisch roten
/// <c>ModelDownloaderTest</c>-Läufe in der CI. Die Tests stellen beide Reihenfolgen fest ein, ohne Wartezeiten.
/// </summary>
public sealed class RangeServerTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListenerVorDemAcceptGestopptBeendetDieSchleifeOhneAusnahme()
    {
        // Reihenfolge wie in DisposeAsync: erst abbrechen, dann stoppen.
        using var stop = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await stop.CancelAsync();
        listener.Stop();

        Assert.Null(await RangeServer.AcceptOrNullAsync(listener, stop.Token));
    }

    [Fact]
    public async Task ListenerWaehrendDesAcceptGestopptBeendetDieSchleifeOhneAusnahme()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var pending = RangeServer.AcceptOrNullAsync(listener, Ct);
        Assert.False(pending.IsCompleted);

        listener.Stop();

        Assert.Null(await pending);
    }
}
