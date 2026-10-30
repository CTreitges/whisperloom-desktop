using System.IO.Pipes;
using System.Net.Sockets;

namespace WhisperLoom.App.Instance;

/// <summary>
/// Nimmt Befehle eines zweiten Prozesses an (<see cref="InstanceEndpoint"/>) und reicht sie an den Handler.
/// Läuft im Hintergrund, bis <see cref="DisposeAsync"/>; Fehler einer Verbindung beenden den Server nicht.
/// </summary>
internal sealed class IpcServer : IAsyncDisposable
{
    private readonly InstanceEndpoint _endpoint;
    private readonly Func<IpcRequest, CancellationToken, Task<IpcResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private Socket? _listener;
    private Task? _loop;

    public IpcServer(InstanceEndpoint endpoint, Func<IpcRequest, CancellationToken, Task<IpcResponse>> handler)
    {
        _endpoint = endpoint;
        _handler = handler;
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Wer den Lock hält, darf einen verwaisten Socket eines abgestürzten Vorgängers entfernen.
            File.Delete(_endpoint.SocketPath);
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_endpoint.SocketPath));
            File.SetUnixFileMode(_endpoint.SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _listener.Listen(8);
        }
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = await AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or SocketException)
            {
                continue;
            }
            _ = Task.Run(() => ServeAsync(stream, ct), ct);
        }
    }

    private async Task<Stream> AcceptAsync(CancellationToken ct)
    {
        if (_listener is not null)
        {
            var client = await _listener.AcceptAsync(ct).ConfigureAwait(false);
            return new NetworkStream(client, ownsSocket: true);
        }
        // Doku: learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeserverstream (abgerufen 2026-10-30).
        var pipe = new NamedPipeServerStream(
            _endpoint.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ServeAsync(Stream stream, CancellationToken ct)
    {
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                var request = await IpcProtocol.ReadAsync<IpcRequest>(stream, ct).ConfigureAwait(false);
                var response = request is null
                    ? new IpcResponse(false, "Ungültige Anfrage.")
                    : await _handler(request, ct).ConfigureAwait(false);
                await IpcProtocol.WriteAsync(stream, response, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
            {
                // Client weg oder Server endet: nichts zu tun.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener?.Dispose();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }
        if (_listener is not null)
        {
            File.Delete(_endpoint.SocketPath);
        }
        _cts.Dispose();
    }
}
