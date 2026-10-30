using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WhisperLoom.Core.Tests.Support;

/// <summary>Eine Anfrage an den <see cref="FakeServer"/>.</summary>
public sealed record FakeRequest(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public string Text => Encoding.UTF8.GetString(Body);

    /// <summary>Byte für Byte als Zeichen (wie ISO-8859-1 in den Kotlin-Tests) — für Multipart mit Audio.</summary>
    public string Latin1 => Encoding.Latin1.GetString(Body);

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
}

public sealed record FakeResponse(int Status, string Body);

/// <summary>
/// Minimaler HTTP/1.1-Server auf 127.0.0.1 mit freiem Port für die Tests (Ersatz für den JDK-HttpServer der
/// Kotlin-Tests). Pfade wie <c>createContext</c>: der längste passende Präfix gewinnt. Jede Antwort schließt die
/// Verbindung. Nur lokale Fakes, nie echte Anbieter.
/// </summary>
public sealed class FakeServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, Func<FakeRequest, Task<FakeResponse>>> _routes = new();
    private readonly Task _loop;

    public FakeServer()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string Base => $"http://127.0.0.1:{Port}";

    public string Url(string path) => Base + path;

    public void Map(string path, Func<FakeRequest, FakeResponse> handler) => _routes[path] = r => Task.FromResult(handler(r));

    public void MapAsync(string path, Func<FakeRequest, Task<FakeResponse>> handler) => _routes[path] = handler;

    public void Unmap(string path) => _routes.TryRemove(path, out _);

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                if (request is null) return;
                var route = _routes.Keys.Where(k => request.Path.StartsWith(k, StringComparison.Ordinal))
                    .OrderByDescending(k => k.Length).FirstOrDefault();
                var response = route is null ? new FakeResponse(404, "not found") : await _routes[route](request);
                var body = Encoding.UTF8.GetBytes(response.Body);
                var head = $"HTTP/1.1 {response.Status} Status\r\nContent-Type: application/json; charset=utf-8\r\n" +
                    $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token);
                await stream.WriteAsync(body, _stop.Token);
                await stream.FlushAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // Client hat abgebrochen (Zeitlimit-Tests).
            }
        }
    }

    private async Task<FakeRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (!EndsWithBlankLine(buffer))
        {
            if (await stream.ReadAsync(one, _stop.Token) == 0) return null;
            buffer.Add(one[0]);
        }
        var lines = Encoding.ASCII.GetString([.. buffer]).Split("\r\n");
        var parts = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var length = headers.TryGetValue("Content-Length", out var cl) ? int.Parse(cl, System.Globalization.CultureInfo.InvariantCulture) : 0;
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read), _stop.Token);
            if (n == 0) break;
            read += n;
        }
        var target = parts[1];
        var q = target.IndexOf('?', StringComparison.Ordinal);
        return new FakeRequest(parts[0], q < 0 ? target : target[..q], q < 0 ? "" : target[(q + 1)..], headers, body);
    }

    private static bool EndsWithBlankLine(List<byte> b) =>
        b.Count >= 4 && b[^4] == '\r' && b[^3] == '\n' && b[^2] == '\r' && b[^1] == '\n';

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
