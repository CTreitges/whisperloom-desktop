using System.Text.Json.Nodes;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Instance;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Management;
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
        settings = EnforceFixed(settings, policy);
        policy = RetireDefaults(policy, settings);
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

    /// <summary>Firmen-Anbindung (W2-ENR); null in Tests ohne Server.</summary>
    public ManagementService? Manager { get; internal set; }

    /// <summary>Einladungslink, mit dem der Assistent als Nächstes öffnet (Server, Code); die App liest und leert ihn.</summary>
    public JoinLink? PendingJoin { get; set; }

    /// <summary>Werte fester Pfade aus policy-fixed.json wieder einsetzen (lokal geänderte settings.json, auch offline).</summary>
    private WhisperLoomSettings EnforceFixed(WhisperLoomSettings settings, PolicySettings policy)
    {
        var (enforced, changed, retired) = Policies.EnforceFixed(settings, policy);
        foreach (var path in retired)
        {
            // TD-A4: festes Modell der Firma abgeschaltet und auf den Nachfolger umgestellt — nur der Pfad, kein Wert.
            Log.Event("management.retired_model", ("path", path), ("level", "fixed"));
        }
        if (changed != 0)
        {
            Log.Event("policy.fixed", ("restored", changed));
        }
        if (changed == -1)
        {
            // W2-FIX N-1: ohne Fest-Werte keine 304 auf dem lokalen Stand; der nächste Abgleich holt alles neu.
            if (Manager is { } management)
            {
                management.ForgetConfigVersion();
            }
            else
            {
                new ManagementCache(Options.DataDir).Load().ForgetETag();
            }
        }
        return enforced;
    }

    /// <summary>
    /// TD-A4: Vorgaben der Firma mit abgeschaltetem OpenAI-Modell gehen auf den Nachfolger und werden gespeichert, sonst
    /// setzte „Auf Firmenvorgabe zurücksetzen“ das alte Modell zurück. Log nur mit Pfad.
    /// </summary>
    private PolicySettings RetireDefaults(PolicySettings policy, WhisperLoomSettings settings)
    {
        var (migrated, changed) = RetiredModels.ApplyDefaults(policy, settings);
        if (changed.Count == 0)
        {
            return policy;
        }
        Policies.Save(migrated);
        foreach (var path in changed)
        {
            Log.Event("management.retired_model", ("path", path), ("level", "default"));
        }
        return migrated;
    }

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
            settings = EnforceFixed(settings, policy);
            policy = RetireDefaults(policy, settings);
            policy.Enforce(settings);
            Controller.Policy = policy;
            await Controller.ApplySettingsAsync(settings);
            Loc.Instance.SetLanguage(SettingsStore.Desktop(settings).UiLanguage);
            Log.Event("settings.reload", ("policy", policy.IsActive));
            // Nach join bzw. provision mit Firmencode aus einem zweiten Prozess: gleich abgleichen bzw. verbinden.
            if (Manager is { } management && settings.Management.ServerUrl.Length > 0)
            {
                _ = management.SyncAsync(CancellationToken.None);
            }
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

    /// <summary>
    /// „Markierung verarbeiten“ (W2-SEL): Windows mit Hotkeys über den Eingabe-Ablauf, Linux über die Primärauswahl. Argument:
    /// Stufe (CLI <c>--stage</c>) oder null (Stufen-Wähler); zweites Argument true = Tray (Ziel = Fenster vor dem Tray-Klick).
    /// null = nicht verfügbar.
    /// </summary>
    public Func<string?, bool, Task<string>>? ProcessSelection { get; set; }

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
    /// <param name="onUiThread">Führt Änderungen der Firmen-Anbindung auf dem UI-Thread aus (Controller, Fenster).</param>
    internal static async Task<AppHost> CreateAsync(AppOptions options, Func<Func<Task>, Task> onUiThread, CancellationToken ct)
    {
        var userSecrets = await SecretStoreFactory.CreateAsync(options.DataDir, options.SecretPrefix, ct).ConfigureAwait(false);
        var companySecrets = await ManagementFactory.CompanyStoreAsync(options.DataDir, options.SecretPrefix, ct).ConfigureAwait(false);
        ManagementService? management = null;
        AppHost? host = null;
        // Firmen-Zugänge (W2-ENR): Geräte-Token für den Proxy bzw. verteilte Keys statt eigener Keys, wo die Firma das Ziel festlegt.
        ISecretStore secrets = new CompanySecretStore(userSecrets, companySecrets,
            () => host?.Controller.Settings ?? new WhisperLoomSettings(), () => host?.Controller.Policy ?? PolicySettings.None,
            () => management?.KeyTargets ?? new Dictionary<string, KeyTarget>(), new OwnKeyTargets(options.DataDir));
        IAudioRecorder recorder = options.TestAudio is null ? new PortAudioRecorder() : new WavFileRecorder(options.TestAudio);
        IOutputSink clipboard = options.OutputFile is null ? new ClipboardOutputSink(ProcessRunner.Instance) : new FileOutputSink(options.OutputFile);
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
        management = new ManagementService(
            new HostManagedTarget(host.Controller, host.Policies, onUiThread), userSecrets, companySecrets,
            ManagementFactory.Api(HttpClients.Create(desktop)), new ManagementCache(options.DataDir).Load(), host.Log);
        host.Manager = management;
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
}
