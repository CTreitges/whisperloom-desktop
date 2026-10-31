using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>Der Begleitprozess startet nicht oder wird nicht gesund.</summary>
public sealed class SidecarStartException(string message) : Exception(message);

/// <summary>
/// Ein laufender <c>whisper-server</c> auf 127.0.0.1 mit freiem Port: Start mit Health-Check, Backend und Gerät aus dem
/// Log, Ende nur über die eigene PID. Vom Log werden nur Geräte-Zeilen behalten (nie Text), nichts kommt auf die Platte.
/// </summary>
// Doku: https://github.com/ggml-org/whisper.cpp/blob/master/examples/server/README.md (abgerufen 2026-10-31)
public sealed partial class WhisperServerProcess : IAsyncDisposable
{
    public const string Host = "127.0.0.1";

    private readonly Process _process;
    private readonly ProcessGuard _guard;
    private readonly List<string> _deviceLines = [];

    private WhisperServerProcess(Process process, int port, string model)
    {
        _process = process;
        Port = port;
        Model = model;
        _guard = ProcessGuard.Attach(process);
    }

    public int Pid => _process.Id;

    public int Port { get; }

    public string Model { get; }

    public string BaseUrl => $"http://{Host}:{Port}";

    public bool HasExited => _process.HasExited;

    /// <summary>"vulkan" oder "cpu", sobald der Server gesund ist.</summary>
    public string Backend { get; private set; } = "cpu";

    /// <summary>Name der GPU bzw. "cpu".</summary>
    public string Device { get; private set; } = "cpu";

    /// <summary>„ggml_vulkan: 0 = NVIDIA GeForce GTX 1080 Ti (NVIDIA) | uma: 0 …“ (wie tools/sidecar_probe.py).</summary>
    [GeneratedRegex(@"ggml_vulkan: \d+ = (.+?) \(")]
    private static partial Regex VulkanDevice();

    [GeneratedRegex(@"using (Vulkan\d*) backend|ggml_vulkan|backend|device", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceLine();

    /// <summary>Startet den Server und wartet auf <c>/health</c>.</summary>
    /// <exception cref="SidecarStartException">Prozess endet vorher oder wird nicht rechtzeitig gesund.</exception>
    public static async Task<WhisperServerProcess> StartAsync(
        string exe, string model, HttpClient http, bool noGpu, TimeSpan timeout, CancellationToken ct)
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
        foreach (var a in new[] { "-m", model, "--host", Host, "--port", port.ToString(), "-t", Threads().ToString() }) psi.ArgumentList.Add(a);
        if (noGpu) psi.ArgumentList.Add("--no-gpu");
        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new SidecarStartException("whisper-server startet nicht");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new SidecarStartException($"whisper-server startet nicht ({e.NativeErrorCode})");
        }
        var server = new WhisperServerProcess(process, port, model);
        process.OutputDataReceived += (_, e) => server.OnLog(e.Data);
        process.ErrorDataReceived += (_, e) => server.OnLog(e.Data);
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

    /// <summary>Gerätezeilen des Logs (für <c>bench</c> und die Fehlersuche).</summary>
    public IReadOnlyList<string> DeviceLog()
    {
        lock (_deviceLines) return [.. _deviceLines];
    }

    private void OnLog(string? line)
    {
        if (line is null || !DeviceLine().IsMatch(line)) return;
        lock (_deviceLines)
        {
            if (_deviceLines.Count < 50) _deviceLines.Add(line.Trim());
        }
        if (VulkanDevice().Match(line) is { Success: true } m && Device == "cpu") Device = m.Groups[1].Value.Trim();
        if (line.Contains("using Vulkan", StringComparison.Ordinal)) Backend = "vulkan";
    }

    private async Task WaitHealthyAsync(HttpClient http, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!await WhisperServerClient.HealthAsync(http, BaseUrl, ct).ConfigureAwait(false))
        {
            if (_process.HasExited) throw new SidecarStartException($"whisper-server beendet mit Exit {_process.ExitCode}");
            if (Environment.TickCount64 > deadline) throw new SidecarStartException($"whisper-server nicht bereit nach {timeout.TotalSeconds:0} s");
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        if (Backend != "vulkan") Device = "cpu";
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
            // Schon beendet oder nicht mehr erreichbar; das Job-Objekt bzw. der Wächter räumt auf.
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

    /// <summary>Wie Android: Kerne, mindestens 2, höchstens 8 (Desktop-CPUs ohne Effizienzkerne-Erkennung).</summary>
    internal static int Threads() => Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
}
