using System.Diagnostics;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Tests.Local;

/// <summary>
/// Akzeptanz A4.2 mit echtem Begleitprozess (Kategorie <c>LocalStt</c>): Start auf 127.0.0.1, <c>jfk.wav</c> mit
/// <c>tiny</c> auf der CPU erkennt „fellow Americans“, nach dem Kill des Hosts läuft kein <c>whisper-server</c> mehr.
/// </summary>
[Trait("Category", "LocalStt")]
public sealed class LocalSttTest
{
    private static WhisperLoomSettings CpuTiny()
    {
        var s = new WhisperLoomSettings { Engine = "offline", Language = "en" };
        s.Offline.Model = "tiny";
        s.Offline.Accurate = false;
        LocalSettings.Set(s, new LocalSettings { Backend = "cpu" });
        return s;
    }

    private static AudioBuffer JfkAudio() => new(WavFileRecorder.Read(File.ReadAllBytes(LocalAssets.Jfk)));

    [Fact]
    public async Task ErkenntJfkMitTinyAufDerCpuUeber127001()
    {
        await LocalAssets.EnsureAsync();
        var settings = CpuTiny();
        int pid;
        await using (var stt = new LocalStt(LocalAssets.LocalDir, () => settings))
        {
            Assert.True(await stt.IsReadyAsync(CancellationToken.None));
            var result = await stt.TranscribeAsync(JfkAudio(), "en", CancellationToken.None);
            Assert.Contains("fellow Americans", result.Text, StringComparison.OrdinalIgnoreCase);
            var server = Assert.IsType<WhisperServerProcess>(stt.Server);
            Assert.StartsWith("http://127.0.0.1:", server.BaseUrl, StringComparison.Ordinal);
            Assert.Equal(("cpu", "cpu"), stt.LastBackend);
            pid = server.Pid;
            Assert.True(LocalAssets.SidecarAlive(pid));
        }
        Assert.True(await LocalAssets.WaitGoneAsync(pid, TimeSpan.FromSeconds(10)), $"whisper-server {pid} läuft noch");
    }

    /// <summary>Host = die App mit dem Verb <c>bench</c> (lange Messung); Kill nur über die gespeicherte PID des Hosts.</summary>
    [Fact]
    public async Task NachKillDesHostsLaeuftKeinServerMehr()
    {
        await LocalAssets.EnsureAsync();
        var exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "WhisperLoom.exe" : "WhisperLoom");
        var psi = File.Exists(exe) ? new ProcessStartInfo(exe) : new ProcessStartInfo("dotnet") { ArgumentList = { Path.Combine(AppContext.BaseDirectory, "WhisperLoom.dll") } };
        foreach (var a in new[] { "bench", "--model", "tiny", "--seconds", "1200", "--backend", "cpu", "--data-dir", LocalAssets.DataDir }) psi.ArgumentList.Add(a);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        using var host = Process.Start(psi)!;
        var hostPid = host.Id;
        int? sidecarPid = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            while (sidecarPid is null && await host.StandardOutput.ReadLineAsync(cts.Token) is { } line)
            {
                if (line.StartsWith("sidecar pid=", StringComparison.Ordinal))
                {
                    sidecarPid = int.Parse(line["sidecar pid=".Length..].Split(' ')[0]);
                }
            }
            Assert.NotNull(sidecarPid);
            Assert.True(LocalAssets.SidecarAlive(sidecarPid.Value), "Begleitprozess läuft nicht");
        }
        finally
        {
            using var own = Process.GetProcessById(hostPid);
            own.Kill(entireProcessTree: false); // nur der Host; den Begleitprozess muss der Schutz beenden
            await own.WaitForExitAsync();
        }
        Assert.True(await LocalAssets.WaitGoneAsync(sidecarPid.Value, TimeSpan.FromSeconds(15)),
            $"whisper-server {sidecarPid} läuft nach dem Kill des Hosts {hostPid} weiter");
    }

    [Fact]
    public async Task EndetNachLeerlauf()
    {
        await LocalAssets.EnsureAsync();
        var settings = CpuTiny();
        await using var stt = new LocalStt(LocalAssets.LocalDir, () => settings) { IdleTimeout = TimeSpan.FromSeconds(1) };
        await stt.TranscribeAsync(new AudioBuffer(new short[16_000]), "en", CancellationToken.None);
        var pid = stt.Server!.Pid;
        Assert.True(await LocalAssets.WaitGoneAsync(pid, TimeSpan.FromSeconds(15)), "kein Ende nach Leerlauf");
        Assert.Null(stt.Server);
    }

    /// <summary>Startet die Vulkan-Variante nicht (hier: kaputte Datei), rechnet die CPU-Variante.</summary>
    [Fact]
    public async Task VulkanFehlerFaelltAufCpuZurueck()
    {
        await LocalAssets.EnsureAsync();
        var dir = Path.Combine(Path.GetTempPath(), "wl-fallback-" + Guid.NewGuid().ToString("N"));
        try
        {
            var release = new SidecarRelease(new HttpClient(), Path.Combine(dir, "whisper-server"));
            var source = new SidecarRelease(new HttpClient(), Path.Combine(LocalAssets.LocalDir, "whisper-server"));
            Directory.CreateDirectory(Path.GetDirectoryName(release.ExePath(SidecarVariant.Vulkan))!);
            await File.WriteAllTextAsync(release.ExePath(SidecarVariant.Vulkan), "kaputt");
            CopyDir(Path.GetDirectoryName(source.ExePath(SidecarVariant.Cpu))!, Path.GetDirectoryName(release.ExePath(SidecarVariant.Cpu))!);
            Directory.CreateDirectory(Path.Combine(dir, "models"));
            File.Copy(new ModelStore(Path.Combine(LocalAssets.LocalDir, "models")).File(ModelCatalog.Tiny), Path.Combine(dir, "models", ModelCatalog.Tiny.FileName));

            var settings = CpuTiny();
            LocalSettings.Set(settings, new LocalSettings { Backend = "auto" });
            await using var stt = new LocalStt(dir, () => settings);
            var server = await stt.EnsureServerAsync(CancellationToken.None);
            Assert.Equal("cpu", server.Backend);
            Assert.Equal(("cpu", "cpu"), stt.LastBackend);
            // Nach dem Vulkan-Fehler bleibt es in dieser Sitzung bei der CPU, auch nach einem Neustart.
            await stt.StopAsync();
            Assert.Equal("cpu", (await stt.EnsureServerAsync(CancellationToken.None)).Backend);
        }
        finally
        {
            // Windows gibt die Exe eines gerade beendeten Prozesses erst kurz danach frei.
            for (var i = 0; i < 25 && Directory.Exists(dir); i++)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(200);
                }
                catch (IOException)
                {
                    await Task.Delay(200);
                }
            }
        }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(f));
            File.Copy(f, target, overwrite: true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(f));
        }
    }
}
