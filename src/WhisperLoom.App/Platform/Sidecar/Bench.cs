using System.Diagnostics;
using System.Globalization;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// CLI-Verb <c>bench --model &lt;m&gt; --seconds &lt;n&gt; [--backend auto|cpu]</c>: lädt Modell und Begleitprozess bei Bedarf,
/// startet ihn und misst die Erkennung von n Sekunden Audio (<c>--test-audio</c> bzw. ein Testsignal). Ausgabe je Zeile
/// <c>schlüssel=wert</c>, u. a. <c>backend=… device=…</c> und <c>rtf=…</c> (Rechenzeit / Audiodauer). Läuft ohne App-Instanz.
/// </summary>
internal static class Bench
{
    public static async Task<int> RunAsync(CliRequest request, TextWriter output, CancellationToken ct)
    {
        var options = request.Options;
        var modelId = Arg(request.Args, "--model") ?? "small";
        var seconds = double.Parse(Arg(request.Args, "--seconds") ?? "10", CultureInfo.InvariantCulture);
        var backend = Arg(request.Args, "--backend") ?? "auto";
        if (ModelCatalog.Find(modelId) is not { } model)
        {
            await Console.Error.WriteLineAsync($"Unbekanntes Modell: {modelId} ({string.Join(", ", ModelCatalog.Models.Select(m => m.Id))})");
            return 2;
        }

        var settings = LoadSettings(options.DataDir);
        settings.Offline.Model = model.Id;
        var local = LocalSettings.Of(settings);
        local.Backend = backend;
        LocalSettings.Set(settings, local);

        using var download = LocalParts.DownloadClient(SettingsStore.Desktop(settings));
        await using var stt = new LocalStt(LocalDir(options), () => settings, download);
        var lastPercent = -10L;
        DownloadProgress progress = (bytes, total, _) =>
        {
            var percent = total > 0 ? bytes * 100 / total : 0;
            if (percent < lastPercent + 10 && bytes < total) return;
            lastPercent = percent;
            Console.Error.WriteLine($"download {percent}%");
        };
        if (!await new ModelDownloader(download).DownloadAsync(model, stt.Models, progress, ct))
        {
            return 1;
        }
        await output.WriteLineAsync($"model={model.Id} file={model.FileName}");

        var load = Stopwatch.StartNew();
        var server = await stt.EnsureServerAsync(ct, progress);
        await output.WriteLineAsync($"sidecar pid={server.Pid} port={server.Port} load_ms={load.ElapsedMilliseconds}");
        await output.WriteLineAsync($"backend={server.Backend} device={server.Device}");
        await output.FlushAsync(ct);

        var audio = Audio(options.TestAudio, seconds);
        // Aufwärmen (Vulkan übersetzt beim ersten Lauf seine Shader), dann messen.
        await WhisperServerClient.TranscribeAsync(stt.LoopbackHttp, server.BaseUrl, Audio(options.TestAudio, 1), "auto", "", settings.Offline.Accurate, ct);
        var watch = Stopwatch.StartNew();
        var result = await WhisperServerClient.TranscribeAsync(stt.LoopbackHttp, server.BaseUrl, audio, "auto", "", settings.Offline.Accurate, ct);
        watch.Stop();
        var audioS = audio.DurationMs / 1000.0;
        var rtf = watch.Elapsed.TotalSeconds / audioS;
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"audio_s={audioS:0.0} inference_ms={watch.ElapsedMilliseconds} rtf={rtf:0.000} chars={result.Text.Length}"));
        return 0;
    }

    /// <summary>Ordner für Modelle und Binaries: <c>&lt;Datenordner&gt;/local</c>.</summary>
    public static string LocalDir(AppOptions options) => Path.Combine(options.DataDir, "local");

    /// <summary>WAV (auf n Sekunden gekürzt bzw. wiederholt) oder ein sprachähnliches Testsignal.</summary>
    internal static AudioBuffer Audio(string? wav, double seconds)
    {
        var count = (int)(seconds * AudioBuffer.DefaultSampleRate);
        var samples = new short[count];
        if (wav is not null)
        {
            var source = WavFileRecorder.Read(File.ReadAllBytes(wav));
            for (var i = 0; i < count && source.Length > 0; i++) samples[i] = source[i % source.Length];
        }
        else
        {
            var random = new Random(42);
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / AudioBuffer.DefaultSampleRate;
                var envelope = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 3 * t);
                var voice = Math.Sin(2 * Math.PI * (180 + 40 * Math.Sin(2 * Math.PI * 0.7 * t)) * t) + 0.4 * Math.Sin(2 * Math.PI * 720 * t);
                samples[i] = (short)(envelope * 6000 * voice + random.Next(-300, 300));
            }
        }
        return new AudioBuffer(samples);
    }

    private static WhisperLoomSettings LoadSettings(string dataDir)
    {
        try
        {
            return new SettingsStore(dataDir).Load();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or NotSupportedException)
        {
            return new WhisperLoomSettings();
        }
    }

    private static string? Arg(IReadOnlyList<string> args, string name)
    {
        var i = args.ToList().IndexOf(name);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }
}
