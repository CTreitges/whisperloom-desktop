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
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App;

/// <summary>
/// Setzt die App zusammen und führt CLI-Befehle aus: echte <see cref="AppPipeline"/>, Verlauf als Dateien im Datenordner.
/// </summary>
public sealed partial class AppHost
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
        Output = output;
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
        Policies = new PolicyStore(options.DataDir);
        var policy = LoadPolicy();
        policy.Enforce(settings);
        History = new FileHistoryStore(Path.Combine(options.DataDir, "history"), () => Controller!.Settings.History);
        var dictation = pipeline(() => Controller!.Settings);
        Controller = new DictationController(recorder, dictation, output, History, Log, settings, Store.Save)
        {
            Policy = policy,
            Rewriter = dictation as Features.Rewrite.ISelectionRewriter,
        };
        Loc.Instance.SetLanguage(SettingsStore.Desktop(settings).UiLanguage);
        ClearHistoryIfDisabled(settings);
    }

    /// <summary>
    /// Verlauf aus (Einstellung oder Richtlinie, auch bei geschlossener App gesetzt): alte Diktate löschen, wie beim Ausschalten
    /// im laufenden Betrieb (<see cref="DictationController.ApplySettingsAsync"/>). Ein Fehler verhindert den Start nicht.
    /// </summary>
    private void ClearHistoryIfDisabled(WhisperLoomSettings settings)
    {
        if (settings.History.Enabled)
        {
            return;
        }
        try
        {
            History.ClearAsync(CancellationToken.None).GetAwaiter().GetResult();
            Log.Event("history.cleared", ("reason", "disabled-at-start"));
        }
        catch (IOException e)
        {
            Log.Event("history.cleared", ("status", "error"), ("errorClass", e.GetType().Name));
        }
    }

    public PolicyStore Policies { get; }

    /// <summary>Eine kaputte policy.json gilt als keine Richtlinie (geloggt), statt den Start zu verhindern.</summary>
    private PolicySettings LoadPolicy()
    {
        try
        {
            return Policies.Load();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException)
        {
            Log.Event("policy", ("status", "unreadable"), ("errorClass", e.GetType().Name));
            return PolicySettings.None;
        }
    }

    /// <summary>Nach <c>provision</c>/<c>import</c> aus einem zweiten Prozess: Einstellungen und Richtlinie neu lesen.</summary>
    private async Task<string> ReloadAsync()
    {
        try
        {
            var settings = Store.Load();
            var policy = LoadPolicy();
            policy.Enforce(settings);
            Controller.Policy = policy;
            await Controller.ApplySettingsAsync(settings);
            Loc.Instance.SetLanguage(SettingsStore.Desktop(settings).UiLanguage);
            Log.Event("settings.reload", ("policy", policy.IsActive));
            return "reloaded";
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException or IOException)
        {
            Log.Event("settings.reload", ("status", "failed"), ("errorClass", e.GetType().Name));
            return "error";
        }
    }

    internal AppOptions Options { get; }

    public ISecretStore Secrets { get; }

    public SettingsStore Store { get; }

    public AppLog Log { get; }

    public IHistoryStore History { get; }

    public DictationController Controller { get; }

    /// <summary>Die Ausgabe-Senke (unter Windows mit Einfügen per Tastenkürzel, Phase 3).</summary>
    public IOutputSink Output { get; }

    /// <summary>Stand der Windows-Bedienung für <c>status --json</c> (Overlay, Pille, Hinweis); null = keine Hotkeys.</summary>
    public Func<JsonObject>? InputStatus { get; set; }

    /// <summary>Lokale Erkennung und Textverbesserung (WLD-P4b); null in Tests ohne lokale Teile.</summary>
    public LocalParts? Local { get; private set; }

    /// <summary>Fenster-Aktionen (Bühnenansicht, Verlauf, Einstellungen, Einrichtung, Beenden); setzt die App.</summary>
    public Action<string>? ShowWindow { get; set; }

    /// <summary>Windows mit Hotkeys: Text ins Ziel des letzten Diktats einfügen (Tray-Verlauf); null = kopieren.</summary>
    public Func<string, Task<string>>? InsertRecent { get; set; }

    /// <summary>Windows mit Hotkeys: wie der Hotkey „erneut einfügen“ (Vordergrundfenster); null = kopieren.</summary>
    public Func<Task<string>>? Reinsert { get; set; }

    /// <summary>
    /// Klick im Tray-Verlauf bzw. <c>recent-insert</c>: einfügen (Windows mit Hotkeys; der Text bleibt dabei auch in der
    /// Zwischenablage, W2-OUT) bzw. in die Zwischenablage mit Hinweis „Kopiert“.
    /// </summary>
    public Task<string> InsertRecentTextAsync(string text) => InsertRecent is { } insert ? insert(text) : CopyAsync(text);

    private async Task<string> RecentInsertAsync(string index)
    {
        IReadOnlyList<HistoryEntry> recent;
        try
        {
            recent = await Controller.RecentAsync();
        }
        catch (IOException)
        {
            return "no-entry";
        }
        return int.TryParse(index, out var n) && n >= 0 && n < recent.Count ? await InsertRecentTextAsync(recent[n].Text) : "no-entry";
    }

    private async Task<string> CopyAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "no-entry";
        }
        try
        {
            await Output.WriteAsync(text, CancellationToken.None);
            return "copied";
        }
        catch (IOException)
        {
            return "error";
        }
    }

    /// <summary>Die echte Zusammensetzung für einen App-Start.</summary>
    internal static async Task<AppHost> CreateAsync(AppOptions options, CancellationToken ct)
    {
        var secrets = await SecretStoreFactory.CreateAsync(options.DataDir, options.SecretPrefix, ct).ConfigureAwait(false);
        IAudioRecorder recorder = options.TestAudio is null ? new PortAudioRecorder() : new WavFileRecorder(options.TestAudio);
        IOutputSink clipboard = options.OutputFile is null ? new ClipboardOutputSink(ProcessRunner.Instance) : new FileOutputSink(options.OutputFile);
        AppHost? host = null;
        // Ohne Einfügen (Linux, Tray/CLI): Zwischenablage + Hinweis „Kopiert – Strg+V“ (W2-OUT, OutputDecision).
        IOutputSink output = new HintingOutputSink(clipboard, () => host?.Controller.Settings.Output ?? new OutputSettings(), hint => host?.OnHint(hint));
        if (OperatingSystem.IsWindows() && options.OutputFile is null)
        {
            // Einfügen per Tastenkürzel; Modus, Enter danach und „immer kopieren“ folgen den Einstellungen des fertigen Hosts.
            output = Platform.Windows.Input.WindowsInput.CreateSink(output, () =>
                Platform.Windows.TextOutput.InsertModes.FromKey(host is null ? null : SettingsStore.Desktop(host.Controller.Settings).InsertMode),
                () => host?.Controller.Settings.Output ?? new OutputSettings());
        }
        var cache = new ModelCache(Path.Combine(options.DataDir, "model-cache.json"));
        LocalParts? local = null;
        AppPipeline? pipeline = null;
        var desktop = SettingsStore.Desktop(TryLoad(options.DataDir));
        host = new AppHost(options, secrets, settings =>
        {
            local = new LocalParts(options, settings, desktop);
            return pipeline = new AppPipeline(settings, secrets, cache, local.Stt, local.Text);
        }, recorder, output);
        host.Clipboard = clipboard;
        host.Local = local;
        if (local is not null && pipeline is not null)
        {
            LocalWarmUp.Attach(host.Controller, pipeline.ShouldWarmUpLocalAsync, local.Text);
        }
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
            case "reload": message = await ReloadAsync(); break;
            case "stage" when Controller.StageLocked:
                ok = false;
                message = "stage-locked";
                break;
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
            case "reprocess":
                try
                {
                    message = await Controller.ReprocessAsync(args.Count > 0 ? args[0] : Controller.Stage.Key(), args.Count > 1 ? args[1] : null);
                }
                catch (ArgumentException)
                {
                    ok = false;
                    message = "stage-unavailable";
                }
                break;
            case "reinsert":
                message = Reinsert is { } reinsert ? await reinsert() : await CopyAsync(await Controller.LatestTextAsync());
                break;
            case "recent-insert":
                message = await RecentInsertAsync(args.Count > 0 ? args[0] : "0");
                ok = message != "no-entry";
                break;
            case "retry":
                message = await Controller.RetryAsync();
                ok = message == "done";
                break;
            case "focus-probe":
                message = ProbeFocus is { } probe ? await probe() : "Unknown 0";
                break;
            case "history-copy":
                message = await HistoryCopyAsync(args.Count > 0 ? args[0] : "0");
                ok = message == "copied";
                break;
            case "show": ShowWindow?.Invoke("stage"); message = "shown"; break;
            case "setup": ShowWindow?.Invoke("setup"); message = "shown"; break;
            case "quit": ShowWindow?.Invoke("quit"); message = "quitting"; break;
            default: ok = false; message = "unknown-verb"; break;
        }
        if (message is "no-entry")
        {
            ok = false;
        }
        if (message is "audio-unavailable" or "error" or "busy")
        {
            ok = message == "busy";
        }
        return new IpcResponse(ok, message, await StatusAsync());
    }

    private static JsonObject PolicyStatus(PolicySettings p) => new()
    {
        ["active"] = p.IsActive,
        ["locked"] = new JsonArray([.. p.Locked.Select(l => (JsonNode)l)]),
        ["historyEnabled"] = p.HistoryEnabled,
        ["updatesEnabled"] = p.UpdatesEnabled,
        ["allowedProviders"] = p.AllowedProviders is null ? null : new JsonArray([.. p.AllowedProviders.Select(a => (JsonNode)a)]),
    };
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
            ["textEngine"] = Local?.Text.Engine,
            ["llamaPid"] = Local?.Bundled.Server is { HasExited: false } llama ? llama.Pid : null,
            ["llamaBackend"] = Local?.Bundled.Server?.Backend,
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
        var history = await Controller.HistoryIfEnabledAsync();
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
            ["policy"] = PolicyStatus(c.Policy),
            ["dataDir"] = Options.DataDir,
            ["output"] = Options.OutputFile is null ? "clipboard" : "file",
            ["lastHint"] = LastHint,
            ["copies"] = new JsonObject { ["ok"] = CopyCount.Ok, ["failed"] = CopyCount.Failed },
            ["local"] = LocalStatus(),
            ["input"] = InputStatus?.Invoke(),
            ["recent"] = new JsonArray([.. Features.Reinsert.RecentHistory.Take(history).Select(e => (JsonNode)new JsonObject
            {
                ["id"] = e.Id,
                ["label"] = Features.Reinsert.RecentHistory.Label(e),
                ["stage"] = e.Refinement.Mode.Key(),
            })]),
        };
    }
}
