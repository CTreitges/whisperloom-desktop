using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>
/// Lokaler HTTP-Server für Downloads (Gegenstück zum JDK-HttpServer in <c>ModelDownloaderTest.kt</c>): liefert Bytes, kann
/// Range (206 + Content-Range), lässt sich für Fehlerfälle verstellen. Jede Antwort mit <c>Connection: close</c>.
/// </summary>
public sealed class RangeServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly List<string?> _ranges = [];
    private readonly List<string> _paths = [];
    private readonly List<int> _statuses = [];

    public RangeServer(byte[] body)
    {
        Body = body;
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public byte[] Body { get; set; }

    public bool RangeSupported { get; set; } = true;

    public int SlowChunkMs { get; set; }

    /// <summary>So lange keine Antwort-Header ("Server hängt").</summary>
    public int HoldMs { get; set; }

    public int StatusOverride { get; set; }

    /// <summary>Erste Antwort nach so vielen Bytes beenden (Netzabbruch); -1 = nie.</summary>
    public int TruncateFirstResponseAt { get; set; } = -1;

    public string Url(string path) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";

    public IReadOnlyList<string?> Ranges { get { lock (_ranges) return [.. _ranges]; } }

    public IReadOnlyList<string> Paths { get { lock (_paths) return [.. _paths]; } }

    public IReadOnlyList<int> Statuses { get { lock (_statuses) return [.. _statuses]; } }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (path, range) = await ReadHeadAsync(stream);
                bool first;
                lock (_ranges)
                {
                    _ranges.Add(range);
                    first = _ranges.Count == 1;
                }
                lock (_paths) _paths.Add(path);
                if (HoldMs > 0) await Task.Delay(HoldMs, _stop.Token);
                if (StatusOverride != 0)
                {
                    lock (_statuses) _statuses.Add(StatusOverride);
                    await WriteHeadAsync(stream, StatusOverride, 0, null);
                    return;
                }
                var body = Body;
                var from = 0;
                var status = 200;
                string? contentRange = null;
                if (RangeSupported && range is not null && range.StartsWith("bytes=", StringComparison.Ordinal))
                {
                    from = int.Parse(range["bytes=".Length..].TrimEnd('-'));
                    if (from >= body.Length)
                    {
                        lock (_statuses) _statuses.Add(416);
                        await WriteHeadAsync(stream, 416, 0, null);
                        return;
                    }
                    status = 206;
                    contentRange = $"bytes {from}-{body.Length - 1}/{body.Length}";
                }
                lock (_statuses) _statuses.Add(status);
                var slice = body.AsMemory(from);
                var cut = first && TruncateFirstResponseAt >= 0 ? Math.Min(TruncateFirstResponseAt, slice.Length) : slice.Length;
                // Abgeschnitten: volle Content-Length ankündigen, dann die Verbindung schließen (Netzabbruch).
                await WriteHeadAsync(stream, status, slice.Length, contentRange);
                for (var off = 0; off < cut;)
                {
                    var n = Math.Min(16 * 1024, cut - off);
                    await stream.WriteAsync(slice.Slice(off, n));
                    await stream.FlushAsync();
                    off += n;
                    if (SlowChunkMs > 0) await Task.Delay(SlowChunkMs, _stop.Token);
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // Client hat abgebrochen.
            }
        }
    }

    private static async Task<(string Path, string? Range)> ReadHeadAsync(NetworkStream stream)
    {
        var head = new StringBuilder();
        var buf = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(buf) == 0) break;
            head.Append((char)buf[0]);
        }
        var lines = head.ToString().Split("\r\n");
        var path = lines[0].Split(' ')[1];
        var range = lines.Skip(1)
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2 && p[0].Trim().Equals("Range", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[1].Trim())
            .FirstOrDefault();
        return (path, range);
    }

    private static Task WriteHeadAsync(NetworkStream stream, int status, long length, string? contentRange)
    {
        var head = new StringBuilder($"HTTP/1.1 {status} X\r\nContent-Length: {length}\r\nConnection: close\r\n");
        if (contentRange is not null) head.Append($"Content-Range: {contentRange}\r\n");
        head.Append("\r\n");
        return stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString())).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
        _stop.Dispose();
    }
}
