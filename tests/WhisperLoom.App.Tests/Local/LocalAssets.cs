using System.Diagnostics;
using System.Security.Cryptography;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Tests.Local;

/// <summary>
/// Echte Dateien für die Tests der Kategorie <c>LocalStt</c>: Modell <c>tiny</c> (Katalog, Hugging Face), die CPU-Variante
/// von <c>whisper-server</c> (Release, geprüft gegen das Manifest) und <c>jfk.wav</c> (whisper.cpp-Sample am gepinnten Commit).
/// Geladen über dieselben Wege wie die App, einmal je Cache-Ordner: <c>WHISPERLOOM_TEST_CACHE</c>, sonst im Temp-Ordner.
/// </summary>
internal static class LocalAssets
{
    public const string JfkUrl = "https://raw.githubusercontent.com/ggml-org/whisper.cpp/d1be6fde11ac6e0407606b4e42fe72d34add8037/samples/jfk.wav";
    public const string JfkSha256 = "59dfb9a4acb36fe2a2affc14bacbee2920ff435cb13cc314a08c13f66ba7860e";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Datenordner der Tests; Modelle und Binaries unter <c>local/</c> (wie die App).</summary>
    public static string DataDir { get; } = Environment.GetEnvironmentVariable("WHISPERLOOM_TEST_CACHE") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetTempPath(), "whisperloom-test-cache");

    public static string LocalDir => Path.Combine(DataDir, "local");

    public static string Jfk => Path.Combine(DataDir, "jfk.wav");

    public static async Task EnsureAsync()
    {
        await Gate.WaitAsync();
        try
        {
            using var http = LocalParts.DownloadClient(new DesktopSettings());
            var models = new ModelStore(Path.Combine(LocalDir, "models"));
            Assert.True(await new ModelDownloader(http).DownloadAsync(ModelCatalog.Tiny, models));
            await new SidecarRelease(http, Path.Combine(LocalDir, "whisper-server")).EnsureAsync(SidecarVariant.Cpu, null, CancellationToken.None);
            if (!File.Exists(Jfk) || Sha256(Jfk) != JfkSha256)
            {
                var bytes = await http.GetByteArrayAsync(JfkUrl);
                Assert.Equal(JfkSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
                await File.WriteAllBytesAsync(Jfk, bytes);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Lebt ein Prozess mit dieser PID und heißt er whisper-server?</summary>
    public static bool SidecarAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.StartsWith("whisper-server", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public static async Task<bool> WaitGoneAsync(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (SidecarAlive(pid))
        {
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(200);
        }
        return true;
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
