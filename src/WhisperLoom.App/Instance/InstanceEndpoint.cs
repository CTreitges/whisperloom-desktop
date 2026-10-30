using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WhisperLoom.App.Instance;

/// <summary>
/// Wo eine Instanz erreichbar ist — je Datenordner eine: Windows Named Pipe (nur aktueller Benutzer),
/// Linux Unix-Socket in einem Ordner mit Rechten 700 ($XDG_RUNTIME_DIR bzw. /tmp/whisperloom-&lt;user&gt;).
/// Der Lock <c>instance.lock</c> im Datenordner entscheidet, wer die erste Instanz ist; er fällt beim
/// Prozessende (auch nach Absturz) von selbst.
/// </summary>
internal sealed class InstanceEndpoint
{
    public InstanceEndpoint(string dataDir)
    {
        DataDir = dataDir;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant())))[..12].ToLowerInvariant();
        PipeName = $"WhisperLoom-{hash}";
        SocketPath = OperatingSystem.IsWindows() ? "" : Path.Combine(RuntimeDir(), $"whisperloom-{hash}.sock");
    }

    public string DataDir { get; }

    public string PipeName { get; }

    public string SocketPath { get; }

    public string LockPath => Path.Combine(DataDir, "instance.lock");

    /// <summary>Hält den Lock der ersten Instanz; null = eine andere Instanz läuft schon.</summary>
    // Doku: learn.microsoft.com/dotnet/api/system.io.fileshare — FileShare.None sperrt unter Unix per flock (abgerufen 2026-10-30).
    public FileStream? TryAcquireLock()
    {
        Directory.CreateDirectory(DataDir);
        try
        {
            return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Verbindung als Client; null = niemand hört zu.</summary>
    public async Task<Stream?> TryConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            // Doku: learn.microsoft.com/dotnet/api/system.io.pipes.pipeoptions — CurrentUserOnly prüft den Besitzer (abgerufen 2026-10-30).
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(timeout, ct).ConfigureAwait(false);
                return pipe;
            }
            catch (TimeoutException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return null;
            }
        }

        if (!File.Exists(SocketPath))
        {
            return null;
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cts.Token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            socket.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Ordner für den Socket, nur für den aktuellen Benutzer: $XDG_RUNTIME_DIR (systemd legt ihn mit 700 an),
    /// sonst /tmp/whisperloom-&lt;user&gt; mit 700.
    /// </summary>
    private static string RuntimeDir()
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runtime) && Directory.Exists(runtime))
        {
            return runtime;
        }
        var dir = Path.Combine(Path.GetTempPath(), $"whisperloom-{Environment.UserName}");
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return dir;
    }
}
