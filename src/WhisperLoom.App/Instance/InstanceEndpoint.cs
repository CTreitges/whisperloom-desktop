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
    /// sonst /tmp/whisperloom-&lt;user&gt; mit 700, gehört der jemand anderem: ~/.cache/whisperloom-run mit 700.
    /// </summary>
    private static string RuntimeDir()
    {
        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrEmpty(cache))
        {
            cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        }
        return RuntimeDir(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
            Path.Combine(Path.GetTempPath(), $"whisperloom-{Environment.UserName}"), Path.Combine(cache, "whisperloom-run"),
            !OperatingSystem.IsWindows() && geteuid() == 0);
    }

    /// <summary>
    /// TS-WLINUX: <paramref name="candidate"/> (<c>/tmp/whisperloom-&lt;user&gt;</c>) nur, wenn er dem Prozess gehört;
    /// sonst <paramref name="fallback"/> im eigenen Home. Root beweist mit chmod keinen Besitz und nimmt nie <c>/tmp</c>.
    /// </summary>
    /// <exception cref="IOException">Auch der Ausweichordner ist nicht privat; die Meldung nennt Ordner und Ausweg.</exception>
    internal static string RuntimeDir(string? xdgRuntime, string candidate, string fallback, bool isRoot)
    {
        if (!string.IsNullOrEmpty(xdgRuntime) && Directory.Exists(xdgRuntime))
        {
            return xdgRuntime;
        }
        if (OperatingSystem.IsWindows())
        {
            return candidate;
        }
        if (!isRoot && IsPrivateDir(candidate))
        {
            return candidate;
        }
        if (IsPrivateDir(fallback))
        {
            return fallback;
        }
        throw new IOException($"Socket-Ordner {candidate} und {fallback} gehören nicht dem aktuellen Benutzer oder sind nicht "
            + "auf 700 setzbar. XDG_RUNTIME_DIR auf einen eigenen Ordner setzen oder die Ordner entfernen.");
    }

    /// <summary>
    /// Legt <paramref name="dir"/> mit 700 an bzw. setzt ihn auf 700. Ein Symlink zählt nie (könnte auf einen fremden
    /// Ordner zeigen); chmod gelingt nur dem Besitzer, ein fremder Ordner scheitert also hier.
    /// </summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static bool IsPrivateDir(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir, OwnerOnly);
            if (new DirectoryInfo(dir).LinkTarget is not null)
            {
                return false;
            }
            File.SetUnixFileMode(dir, OwnerOnly);
            return File.GetUnixFileMode(dir) == OwnerOnly;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    // Doku: man7.org/linux/man-pages/man2/geteuid.2.html — kann nicht fehlschlagen (abgerufen 2026-11-11).
#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks.
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = false)]
    private static extern uint geteuid();
#pragma warning restore SYSLIB1054
}
