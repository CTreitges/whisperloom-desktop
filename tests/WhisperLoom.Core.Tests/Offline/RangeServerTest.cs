using System.Net;
using System.Net.Sockets;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>
/// Beenden des <see cref="RangeServer"/>: <c>DisposeAsync</c> bricht ab und stoppt den Listener, während die
/// Accept-Schleife läuft. Trifft der Stopp die Lücke zwischen Schleifenprüfung und Accept-Aufruf, wirft
/// <see cref="TcpListener"/> <see cref="InvalidOperationException"/> („Not listening“) — die Ursache der sporadisch
/// roten <c>ModelDownloaderTest</c>-Läufe in der CI. Abgefangen wird sie nur beim Beenden; bei laufendem Server ist sie
/// ein echter Fehler. Die Tests stellen beide Fälle fest ein, ohne Wartezeiten.
/// </summary>
public sealed class RangeServerTest
{
    private static TcpListener StoppedListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        listener.Stop();
        return listener;
    }

    [Fact]
    public async Task ListenerBeimBeendenGestopptBeendetDieSchleifeOhneAusnahme()
    {
        // Wie in DisposeAsync: abgebrochen und gestoppt, bevor die Schleife Accept aufruft.
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        Assert.Null(await RangeServer.AcceptOrNullAsync(StoppedListener(), stop.Token));
    }

    [Fact]
    public async Task ListenerOhneBeendenGestopptMeldetDenFehlerWeiter()
    {
        using var stop = new CancellationTokenSource();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RangeServer.AcceptOrNullAsync(StoppedListener(), stop.Token));
    }
}
