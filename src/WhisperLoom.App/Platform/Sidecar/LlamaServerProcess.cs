using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Ein laufender <c>llama-server</c> auf 127.0.0.1 mit freiem Port (mitgeliefertes Textmodell): Start mit Health-Check
/// (<c>/health</c> = 200, solange das Modell lädt 503), Ende nur über die eigene PID, an die App gebunden
/// (<see cref="ProcessGuard"/>). Vom Log wird nichts behalten (es könnte Diktattext enthalten), nichts kommt auf die Platte.
/// </summary>
// Doku: https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md (abgerufen 2026-11-01)
public sealed class LlamaServerProcess : IAsyncDisposable
{
    public const string Host = "127.0.0.1";

    /// <summary>Kontext in Token: reicht für ein langes Diktat samt Prompt (Glätten verdoppelt grob die Länge).</summary>
    public const int ContextTokens = 8192;

    private readonly Process _process;
    private readonly ProcessGuard _guard;

    private LlamaServerProcess(Process process, int port, string model, string backend)
    {
        _process = process;
        Port = port;
        Model = model;
        Backend = backend;
        _guard = ProcessGuard.Attach(process, "llama-server");
    }

    public int Pid => _process.Id;

    public int Port { get; }

    public string Model { get; }

    /// <summary>"vulkan" oder "cpu" (Variante des Starts).</summary>
    public string Backend { get; }

    /// <summary>OpenAI-kompatible Basis-URL.</summary>
    public string BaseUrl => $"http://{Host}:{Port}/v1";

    public bool HasExited => _process.HasExited;

    /// <summary>Argumente: Modell, nur 127.0.0.1, eine Anfrage zur Zeit, ohne Web-Oberfläche; GPU-Schichten nur mit Vulkan.</summary>
    internal static IReadOnlyList<string> Arguments(string model, int port, bool gpu) =>
    [
        "-m", model, "--host", Host, "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-t", WhisperServerProcess.Threads().ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-c", ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture), "-np", "1", "--no-webui", "--jinja",
        .. gpu ? new[] { "-ngl", "999" } : [],
    ];

    /// <exception cref="SidecarStartException">Prozess endet vorher oder wird nicht rechtzeitig gesund.</exception>
    public static async Task<LlamaServerProcess> StartAsync(string exe, string model, HttpClient http, bool gpu, TimeSpan timeout, CancellationToken ct)
    {
        var port = FreePort();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (var a in Arguments(model, port, gpu)) psi.ArgumentList.Add(a);
        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new SidecarStartException("llama-server startet nicht");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new SidecarStartException($"llama-server startet nicht ({e.NativeErrorCode})");
        }
        LlamaServerProcess server;
        try
        {
            server = new LlamaServerProcess(process, port, model, gpu ? "vulkan" : "cpu");
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            process.Kill();
            process.Dispose();
            throw new SidecarStartException($"llama-server ohne Prozess-Schutz beendet ({e.Message})");
        }
        // Log nur leeren (sonst blockiert der Prozess bei vollem Puffer), nie speichern.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await server.WaitHealthyAsync(http, timeout, ct).ConfigureAwait(false);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return server;
    }

    /// <summary>GET /health: 200 = Modell geladen. Netzfehler und 503 = noch nicht. Wirft nur bei Abbruch.</summary>
    public static async Task<bool> HealthAsync(HttpClient http, string root, CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await http.GetAsync(root + "/health", limit.Token).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task WaitHealthyAsync(HttpClient http, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!await HealthAsync(http, $"http://{Host}:{Port}", ct).ConfigureAwait(false))
        {
            if (_process.HasExited) throw new SidecarStartException($"llama-server beendet mit Exit {_process.ExitCode}");
            if (Environment.TickCount64 > deadline) throw new SidecarStartException($"llama-server nicht bereit nach {timeout.TotalSeconds:0} s");
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Beendet den eigenen Prozess (gespeicherte PID), nie einen anderen.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // Schon beendet; das Job-Objekt bzw. der Wächter räumt auf.
        }
        _guard.Dispose();
        _process.Dispose();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
