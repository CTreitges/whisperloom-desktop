using System.Text.Json.Nodes;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Instance;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Output;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Secrets;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.History;

namespace WhisperLoom.App;

/// <summary>
/// Setzt die App zusammen und führt CLI-Befehle aus: echte <see cref="AppPipeline"/>, Verlauf als Dateien im Datenordner.
/// </summary>
public sealed class AppHost
{
    internal AppHost(
        AppOptions options,
        ISecretStore secrets,
        Func<Func<WhisperLoomSettings>, IDictationPipeline> pipeline,
        IAudioRecorder recorder,
        IOutputSink output)
    {
        Options = options;
        Secrets = secrets;
        Store = new SettingsStore(options.DataDir);
        Log = new AppLog(options.DataDir);
        WhisperLoomSettings settings;
        try
        {
            settings = Store.Load();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException)
        {
            Log.Event("settings", ("status", "unreadable"), ("errorClass", e.GetType().Name));
            settings = new WhisperLoomSettings();
        }
        History = new FileHistoryStore(Path.Combine(options.DataDir, "history"), () => Controller!.Settings.History);
        Controller = new DictationController(recorder, pipeline(() => Controller!.Settings), output, History, Log, settings, Store.Save);
        Loc.Instance.SetLanguage(SettingsStore.Desktop(settings).UiLanguage);
    }

    internal AppOptions Options { get; }

    public ISecretStore Secrets { get; }

    public SettingsStore Store { get; }

    public AppLog Log { get; }

    public IHistoryStore History { get; }

    public DictationController Controller { get; }

    /// <summary>Lokale Erkennung und Textverbesserung (WLD-P4b); null in Tests ohne lokale Teile.</summary>
    public LocalParts? Local { get; private set; }

    /// <summary>Fenster-Aktionen (Bühnenansicht, Verlauf, Einstellungen, Beenden); setzt die App.</summary>
    public Action<string>? ShowWindow { get; set; }

    /// <summary>Die echte Zusammensetzung für einen App-Start.</summary>
    internal static async Task<AppHost> CreateAsync(AppOptions options, CancellationToken ct)
    {
        var secrets = await SecretStoreFactory.CreateAsync(options.DataDir, options.SecretPrefix, ct).ConfigureAwait(false);
        IAudioRecorder recorder = options.TestAudio is null ? new PortAudioRecorder() : new WavFileRecorder(options.TestAudio);
        IOutputSink output = options.OutputFile is null ? new ClipboardOutputSink(ProcessRunner.Instance) : new FileOutputSink(options.OutputFile);
        var cache = new ModelCache(Path.Combine(options.DataDir, "model-cache.json"));
        LocalParts? local = null;
        var desktop = SettingsStore.Desktop(TryLoad(options.DataDir));
        var host = new AppHost(options, secrets, settings =>
        {
            local = new LocalParts(options, settings, desktop);
            return new AppPipeline(settings, secrets, cache, local.Stt, local.Refiner);
        }, recorder, output);
        host.Local = local;
        return host;
    }

    private static WhisperLoomSettings TryLoad(string dataDir)
    {
        try
        {
            return new SettingsStore(dataDir).Load();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException or IOException)
        {
            return new WhisperLoomSettings();
        }
    }

    /// <summary>Führt ein Verb aus (aus der IPC oder vom eigenen Start). Läuft auf dem UI-Thread.</summary>
    public async Task<IpcResponse> ExecuteAsync(string verb, IReadOnlyList<string> args)
    {
        string message;
        var ok = true;
        switch (verb)
        {
            case "toggle": message = await Controller.ToggleAsync(); break;
            case "start": message = Controller.Start(); break;
            case "stop": message = await Controller.StopAsync(); break;
            case "cancel": message = Controller.Cancel(); break;
            case "pause": message = Controller.Pause(); break;
            case "resume": message = Controller.Resume(); break;
            case "status": message = "status"; break;
            case "stage":
                try
                {
                    message = "stage=" + Controller.SetStage(args.Count > 0 ? args[0] : "next").Key();
                }
                catch (ArgumentException)
                {
                    ok = false;
                    message = "stage-unavailable";
                }
                break;
            case "show": ShowWindow?.Invoke("stage"); message = "shown"; break;
            case "quit": ShowWindow?.Invoke("quit"); message = "quitting"; break;
            default: ok = false; message = "unknown-verb"; break;
        }
        if (message is "audio-unavailable" or "error" or "busy")
        {
            ok = message == "busy";
        }
        return new IpcResponse(ok, message, await StatusAsync());
    }

    /// <summary>Lokale Erkennung für <c>status --json</c>: Engine, Begleitprozess (PID, Backend, Gerät), Ollama-Modell.</summary>
    private JsonObject LocalStatus()
    {
        var server = Local?.Stt.Server;
        var last = Local?.Stt.LastBackend;
        return new JsonObject
        {
            ["engine"] = Controller.Settings.Engine,
            ["model"] = Controller.Settings.Offline.Model,
            ["sidecarPid"] = server is { HasExited: false } ? server.Pid : null,
            ["backend"] = last?.Backend,
            ["device"] = last?.Device,
            ["ollamaModel"] = LocalSettings.Of(Controller.Settings).OllamaModel,
        };
    }

    /// <summary>Stand für <c>status --json</c>. Keys nur als vorhanden/fehlt, nie der Wert.</summary>
    public async Task<JsonObject> StatusAsync()
    {
        var keys = new JsonObject();
        foreach (var name in SecretNames.All)
        {
            bool? present;
            try
            {
                present = await Secrets.GetAsync(name, CancellationToken.None) is not null;
            }
            catch (IOException)
            {
                present = null;
            }
            keys[name] = present;
        }
        var history = await History.ListAsync(CancellationToken.None);
        var latest = history.Count > 0 ? history[0] : null;
        var c = Controller;
        return new JsonObject
        {
            ["running"] = true,
            ["pid"] = Environment.ProcessId,
            ["version"] = typeof(AppHost).Assembly.GetName().Version?.ToString(),
            ["state"] = c.State.ToString().ToLowerInvariant(),
            ["stage"] = c.Stage.Key(),
            ["language"] = c.Language,
            ["uiLanguage"] = Loc.Instance.Language,
            ["elapsedMs"] = (long)c.Elapsed.TotalMilliseconds,
            ["lastRaw"] = c.LastRaw,
            ["lastResult"] = c.LastResult,
            ["lastModel"] = c.LastRefined?.Model,
            ["lastError"] = c.LastErrorClass,
            ["history"] = new JsonObject
            {
                ["enabled"] = c.Settings.History.Enabled,
                ["count"] = history.Count,
                ["latest"] = latest is null ? null : new JsonObject
                {
                    ["raw"] = latest.Raw,
                    ["text"] = latest.Text,
                    ["stage"] = latest.Refinement.Mode.Key(),
                    ["stt"] = latest.SttLabel,
                },
            },
            ["secretStore"] = Secrets.Kind,
            ["keys"] = keys,
            ["dataDir"] = Options.DataDir,
            ["output"] = Options.OutputFile is null ? "clipboard" : "file",
            ["local"] = LocalStatus(),
        };
    }
}
