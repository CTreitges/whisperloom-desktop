using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace WhisperLoom.Core.Tests.Support;

/// <summary>
/// Wer bei <c>Connection: close</c> zuerst schließt, behält TIME_WAIT. Schließt der <see cref="FakeServer"/> zuerst,
/// liegt der Eintrag auf (Server-Port, Client-Port); vergibt Windows denselben Client-Port im vollen Testlauf erneut an
/// denselben Server, trifft der SYN auf diesen Eintrag und hängt bis zum Connect-Limit (15 s) — die Ursache der
/// sporadisch roten Pipeline-Tests (TP-WAUDIO). Der Server wartet deshalb, bis der Client geschlossen hat.
/// </summary>
public sealed class FakeServerTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int FreePort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    /// <summary>Eine Anfrage von einem festen Client-Port; der Client schließt nach der vollständigen Antwort.</summary>
    private static async Task<string> RequestFromAsync(int clientPort, int serverPort)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, clientPort));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limit.CancelAfter(10_000);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, serverPort), limit.Token);
        await socket.SendAsync(Encoding.ASCII.GetBytes("GET /x HTTP/1.1\r\nHost: test\r\nContent-Length: 0\r\n\r\n"), limit.Token);
        var received = new StringBuilder();
        var buffer = new byte[1024];
        // Wie SocketsHttpHandler: Kopf + Content-Length lesen, dann selbst schließen (nicht auf das EOF warten).
        while (!Complete(received.ToString()))
        {
            var n = await socket.ReceiveAsync(buffer, limit.Token);
            if (n == 0) break;
            received.Append(Encoding.ASCII.GetString(buffer, 0, n));
        }
        socket.Close();
        return received.ToString();
    }

    private static bool Complete(string response)
    {
        var head = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return head >= 0 && response.Length - head - 4 >= 2; // Body "{}"
    }

    private static bool ServerHoldsTimeWait(int serverPort, int clientPort) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(c =>
            c.State == TcpState.TimeWait && c.LocalEndPoint.Port == serverPort && c.RemoteEndPoint.Port == clientPort);

    [Fact]
    public async Task DerClientSchliesstZuerstUndDerServerBehaeltKeinTimeWait()
    {
        await using var server = new FakeServer();
        server.Map("/x", _ => new FakeResponse(200, "{}"));
        var clientPort = FreePort();

        var response = await RequestFromAsync(clientPort, server.Port);
        Assert.StartsWith("HTTP/1.1 200", response, StringComparison.Ordinal);

        // Dem Server Zeit geben, die Verbindung abzubauen (er wartet auf das EOF des Clients).
        for (var i = 0; i < 50 && !ServerHoldsTimeWait(server.Port, clientPort); i++) await Task.Delay(20, Ct);
        Assert.False(ServerHoldsTimeWait(server.Port, clientPort), "TIME_WAIT auf (Server-Port, Client-Port)");
    }
}
