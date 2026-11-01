using System.Diagnostics;
using System.Security.Cryptography;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Tests.Local;

/// <summary>
/// W2-LOCAL mit echtem <c>llama-server</c> (offizielles Release, CPU) und dem Minimodell <c>stories260K.gguf</c> (1,2 MB):
/// Start auf 127.0.0.1, Chat über die OpenAI-Schicht, Ende über die eigene PID. Nur lokal — große bzw. zusätzliche
/// Modelle laufen nie in der CI (task.md); dort übersprungen.
/// </summary>
[Trait("Category", "LocalText")]
public sealed class LlamaServerTest
{
    public const string TinyUrl = "https://huggingface.co/ggml-org/models/resolve/499bc8821c6b12b4e53c5bffcb21ec206f212d81/tinyllamas/stories260K.gguf";
    public const string TinySha256 = "270cba1bd5109f42d03350f60406024560464db173c0e387d91f0426d3bd256d";

    private static async Task<string> TinyModelAsync()
    {
        var path = Path.Combine(LocalAssets.DataDir, "stories260K.gguf");
        if (!File.Exists(path) || Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))) != TinySha256)
        {
            using var http = LocalParts.DownloadClient(new DesktopSettings());
            var bytes = await http.GetByteArrayAsync(TinyUrl);
            Assert.Equal(TinySha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            Directory.CreateDirectory(LocalAssets.DataDir);
            await File.WriteAllBytesAsync(path, bytes);
        }
        return path;
    }

    [Fact]
    public async Task Startet_rechnet_und_endet_ueber_die_eigene_pid()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("CI") == "true", "Minimodell und llama-server nur lokal (task.md)");
        var model = await TinyModelAsync();
        var settings = new WhisperLoomSettings { Engine = "offline" };
        LocalSettings.Set(settings, new LocalSettings { Backend = "cpu", TextEngine = "bundled" });
        using var download = LocalParts.DownloadClient(new DesktopSettings());
        int pid;
        await using (var engine = new BundledTextEngine(LocalAssets.LocalDir, () => settings, CoreText.Prompts, TextCleanup.For(CoreText.Polisher), download, model))
        {
            Assert.True(await engine.IsReadyAsync(CancellationToken.None));
            await engine.WarmUpAsync(CancellationToken.None);
            var server = Assert.IsType<LlamaServerProcess>(engine.Server);
            Assert.StartsWith("http://127.0.0.1:", server.BaseUrl, StringComparison.Ordinal);
            Assert.Equal("cpu", server.Backend);
            pid = server.Pid;
            try
            {
                // Das Minimodell erzählt Geschichten: Ergebnis oder „keine Bearbeitung“ — beides heißt, der Transport ging.
                var text = await engine.RefineAsync("hallo welt das ist ein test", "de", new Refinement(RefineMode.Polish), CancellationToken.None);
                Assert.False(string.IsNullOrWhiteSpace(text));
            }
            catch (RefineRejectedException)
            {
            }
            Assert.Same(server, engine.Server); // vorgeladen = derselbe Prozess
        }
        Assert.True(await GoneAsync(pid), $"llama-server {pid} läuft noch");
    }

    private static async Task<bool> GoneAsync(int pid)
    {
        for (var i = 0; i < 50; i++)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited || !p.ProcessName.StartsWith("llama-server", StringComparison.Ordinal)) return true;
            }
            catch (ArgumentException)
            {
                return true;
            }
            await Task.Delay(200);
        }
        return false;
    }
}
