using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Instance;
using WhisperLoom.App.Services;
using WhisperLoom.App.Setup;
using WhisperLoom.App.Tray;
using WhisperLoom.App.Updates;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;

namespace WhisperLoom.App;

/// <summary>
/// Hintergrund-App: Tray plus Fenster auf Abruf (Bühnenansicht, Verlauf, Einstellungen). Läuft bis „Beenden“
/// (<see cref="ShutdownMode.OnExplicitShutdown"/>); Befehle zweiter Prozesse kommen über den <see cref="IpcServer"/>.
/// </summary>
public partial class App : Application
{
    private AppHost? _host;
    private IpcServer? _server;
    private TrayIcon? _tray;
    private TrayMenu? _trayMenu;
    private StageWindow? _stage;
    private HistoryWindow? _historyWindow;
    private SettingsWindow? _settingsWindow;
    private SetupWindow? _setupWindow;
    private Views.Help.HelpWindow? _helpWindow;
    private Views.Updates.VersionListWindow? _versionsWindow;
    private VelopackUpdateClient? _updateClient;
    private IDisposable? _input;
    private Platform.Windows.Input.InputFlow? _flow;
    private readonly CancellationTokenSource _updates = new();
    private readonly Design.ISystemPreferences _preferences = Design.SystemPreferences.ForCurrentOs();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Program.Startup is { } startup)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => startup.Lock.Dispose();
            Design.TextScale.Follow(this, _preferences);
            Dispatcher.UIThread.Post(async () => await StartAsync(startup.Request.Options, startup.Request.Verb, startup.Request.Args));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(Cli.AppOptions options, string? verb, IReadOnlyList<string> args)
    {
        _host = await AppHost.CreateAsync(options, f => Dispatcher.UIThread.InvokeAsync(f), CancellationToken.None);
        _host.ShowWindow = Open;
        // Hilfe (W2-HELP): „?“ der Einstellungen, Assistent, Tray und F1 in jedem Fenster.
        Views.Help.HelpLauncher.Handler = OpenHelp;
        Views.Help.HelpLauncher.Install();
        _host.Log.Event("app.start", ("pid", Environment.ProcessId), ("secretStore", _host.Secrets.Kind.Replace(' ', '-')));

        _server = new IpcServer(new InstanceEndpoint(options.DataDir), (request, _) =>
            Dispatcher.UIThread.InvokeAsync(() => _host.ExecuteAsync(request.Verb, request.Args)));
        _server.Start();

        CreateTray(_host);
        StartHotkeys(_host);
        // Die Windows-Bedienung steht erst jetzt: „Letzte Diktate“ heißen dann „Einfügen – …“ statt „Kopieren – …“.
        _trayMenu?.Refresh();
        StartLengthWatch(_host);
        StartUpdates(_host);
        StartManagement(_host);
        if (verb is not null)
        {
            await _host.ExecuteAsync(verb, args);
        }
        else if (SetupState.ShowOnStart(options, new SetupState(options.DataDir)))
        {
            Open("setup");
        }
        else if (_tray is null)
        {
            // Ohne Tray bliebe die App unsichtbar: Bühnenansicht zeigen.
            Open("stage");
        }
    }

    private void CreateTray(AppHost host)
    {
        try
        {
            var menu = new TrayMenu(host.Controller, Open, entry => host.InsertRecentTextAsync(entry.Text),
                async () =>
                {
                    // „Markierung verarbeiten …“ (W2-SEL); ohne Windows-Bedienung bzw. Linux-Weg nichts.
                    if (host.ProcessSelection is { } process)
                    {
                        await process(null, true);
                    }
                },
                // Letzte Diktate: einfügen, sobald die Windows-Bedienung läuft (startet nach dem Tray), sonst kopieren.
                pastes: () => host.InsertRecent is not null);
            _ = menu.ReloadRecentAsync();
            var tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://WhisperLoom/Assets/whisperloom.ico"))),
                ToolTipText = menu.ToolTip,
                Menu = menu.Menu,
                IsVisible = true,
            };
            TrayIcon.SetIcons(this, [tray]);
            _tray = tray;
            _trayMenu = menu;
            tray.Clicked += (_, _) => Open("stage");
            // Abos erst nach SetIcons und auf das lokale Symbol: ohne Infobereich (catch unten, _tray = null) hängt nichts
            // (Prüfer-Fund UX-4). Tooltip auch nach einem Sprachwechsel neu.
            host.Controller.PropertyChanged += (_, _) => tray.ToolTipText = menu.ToolTip;
            Localization.Loc.Instance.PropertyChanged += (_, _) => tray.ToolTipText = menu.ToolTip;
        }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException)
        {
            host.Log.Event("tray", ("status", "unavailable"));
            _tray = null;
        }
    }

    /// <summary>Windows: globale Tastenkürzel, Overlay und Einfügen (Phase 3); im Testbetrieb nur mit <c>--hotkeys</c>.</summary>
    /// <summary>
    /// Längen-Takt für Diktate ohne Tastenkürzel (Tray, CLI, Linux; W2-OUT): Hinweis ab 10 min, bei 12 min hält die Aufnahme an.
    /// Kürzel-Diktate prüft <c>InputFlow.Tick</c> selbst (dort wird beim Halten gesendet).
    /// </summary>
    private void StartLengthWatch(AppHost host)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            var flow = _flow;
            if (flow is not null && flow.ModeName != "none")
            {
                return;
            }
            if (host.Controller.CheckLength() is not { } notice)
            {
                return;
            }
            var key = notice == "long" ? "Recording_Long" : "Recording_Capped";
            if (flow is not null)
            {
                flow.ShowNotice(key);
            }
            else if (OperatingSystem.IsLinux())
            {
                _ = Output.DesktopNotify.ShowAsync(Localization.Loc.Instance[key]);
            }
        };
        timer.Start();
    }

    private void StartHotkeys(AppHost host)
    {
        if (OperatingSystem.IsWindows())
        {
            host.ProbeFocus = ProbeFocusAsync;
        }
        if (OperatingSystem.IsWindows() && host.Options.HotkeysActive && host.Output is Platform.Windows.TextOutput.InsertOutputSink sink)
        {
            StartWindowsInput(host, sink);
        }
        if (OperatingSystem.IsLinux())
        {
            // Markierung verarbeiten (W2-SEL): Primärauswahl → Wähler-Fenster → Zwischenablage; auch mit --output-file (Smoke).
            var flow = new Features.Selection.LinuxSelectionFlow(host.Controller,
                new Platform.Linux.PrimarySelection(Secrets.ProcessRunner.Instance, Environment.GetEnvironmentVariable),
                Features.Selection.LinuxSelectionFlow.PickInWindowAsync, text => _ = Output.DesktopNotify.ShowAsync(text));
            host.ProcessSelection = (stage, _) => flow.RunAsync(stage);
        }
        if (OperatingSystem.IsLinux() && host.Options.OutputFile is null)
        {
            // Linux: Hinweis „Kopiert – Strg+V“ als Desktop-Benachrichtigung (W2-OUT); fehlt notify-send, bleibt es beim Status.
            host.HintPresenter = hint => _ = Output.DesktopNotify.ShowAsync(Localization.Loc.Instance[Output.OutputHints.Key(hint)]);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<string> ProbeFocusAsync()
    {
        var probe = new Platform.Windows.Input.FocusProbe(new Platform.Windows.Input.UiaFocusSource(), fallback: new Platform.Windows.Input.MsaaFocusSource());
        var kind = await probe.ProbeAsync(CancellationToken.None);
        return $"{kind} {probe.Last?.ControlType ?? 0}";
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void StartWindowsInput(AppHost host, Platform.Windows.TextOutput.InsertOutputSink sink)
    {
        var input = Platform.Windows.Input.WindowsInput.Start(host.Controller, sink, host.Log);
        if (input is not null)
        {
            _input = input;
            _flow = input.Flow;
            host.InputStatus = () =>
            {
                var status = input.Flow.Status();
                status["hook"] = input.HookRunning;
                status["mouseHook"] = input.MouseHookRunning;
                return status;
            };
            RecordHotkey = async () => (await input.RecordAsync())?.ToString();
            host.InsertRecent = input.Flow.InsertRecentAsync;
            host.ProcessSelection = (stage, tray) => tray ? input.Flow.ProcessSelectionFromTrayAsync() : input.Flow.ProcessSelectionFromCliAsync(stage);
            host.Reinsert = input.Flow.ReinsertAsync;
            // Hinweise (W2-OUT): Einfügen per Tastenkürzel zeigt seinen Toast selbst, hier nur merken; Tray/CLI zeigen ihn hier.
            sink.Inserted += outcome => host.RecordHint(Platform.Windows.TextOutput.InsertModes.Hint(outcome));
            host.HintPresenter = hint => Dispatcher.UIThread.Post(() => input.Flow.ShowHint(hint));
        }
    }

    /// <summary>Auto-Update (Phase 6): beim Start und täglich, Installation erst nach Bestätigung; Richtlinie schaltet ab.</summary>
    private void StartUpdates(AppHost host)
    {
        var client = _updateClient = new VelopackUpdateClient(feed: null, restartArgs: [.. Environment.GetCommandLineArgs().Skip(1)]);
        // W3-CHAN: Kanal und Vorab-Schalter je Runde aus den aktuellen Einstellungen.
        var service = new UpdateService(client, host.Log, () => host.Controller.Policy,
            version => Dispatcher.UIThread.InvokeAsync(() => UpdatePrompt.AskAsync(version)),
            preferences: () => UpdatePreferences.From(SettingsStore.Desktop(host.Controller.Settings)));
        // Auf dem Thread-Pool: die Feed-Abfrage soll den UI-Thread nie blockieren.
        _ = Task.Run(() => service.RunAsync(_updates.Token));
    }

    /// <summary>
    /// Firmen-Anbindung (W2-ENR): Abgleich beim Start und alle 15 min (Thread-Pool). Im Normalbetrieb registriert die App
    /// das URL-Schema <c>whisperloom://</c> für Einladungslinks (je Benutzer, ohne Admin-Rechte).
    /// </summary>
    private void StartManagement(AppHost host)
    {
        if (host.Manager is { } manager)
        {
            _ = Task.Run(() => manager.RunAsync(_updates.Token));
        }
        if (host.Options.TestAudio is null && host.Options.OutputFile is null && host.Options.SecretPrefix == Cli.AppOptions.DefaultSecretPrefix
            && Autostart.ExecutablePath() is { } exe)
        {
            var status = Platform.JoinLinkRegistration.Register(exe);
            host.Log.Event("joinlink.register", ("status", status));
        }
    }

    /// <summary>Rekorder für die Einstellungen (nur mit laufendem Hook).</summary>
    internal Func<Task<string?>>? RecordHotkey { get; private set; }

    /// <summary>"stage", "history", "settings", "setup", "help" oder "quit".</summary>
    private void Open(string what)
    {
        var host = _host;
        if (host is null)
        {
            return;
        }
        // Textgröße des Systems (UX-1): beim Öffnen neu lesen, damit eine geänderte Einstellung ohne Neustart greift.
        Design.TextScale.Follow(this, _preferences);
        switch (what)
        {
            case "stage":
                if (_stage is null)
                {
                    _stage = new StageWindow(new StageViewModel(host.Controller));
                    _stage.Closed += (_, _) => _stage = null;
                }
                Activate(_stage);
                break;
            case "history":
                if (_historyWindow is null)
                {
                    var vm = new Features.History.HistoryViewModel(host.History, HistoryActionsFor(host));
                    _historyWindow = new HistoryWindow(vm);
                    _historyWindow.Closed += (_, _) => _historyWindow = null;
                    _ = vm.LoadAsync();
                }
                Activate(_historyWindow);
                break;
            case "settings":
                if (_settingsWindow is null)
                {
                    var vm = CreateSettings(host);
                    _settingsWindow = new SettingsWindow(vm);
                    _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                    // Nach Import bzw. „Zurücksetzen“ baut die Hülle (SettingsShellViewModel) die Arbeitskopie selbst neu auf.
                    _ = vm.LoadKeysAsync();
                    _ = vm.Local.RefreshOllamaCommand.ExecuteAsync(null);
                }
                Activate(_settingsWindow);
                break;
            case "setup":
                if (_setupWindow is null)
                {
                    var settings = CreateSettings(host);
                    var recorder = CreateTestRecorder(host);
                    SetupViewModel? current = null;
                    // Die Arbeitskopie wird nach einer Firmen-Freigabe neu aufgebaut: Mikrofon immer aus der aktuellen lesen.
                    var setup = new SetupViewModel(settings, new SetupState(host.Options.DataDir), Autostart.ForCurrentOs(),
                        new MicLevelTest(recorder, () => (current?.Settings ?? settings).Microphone.Value), RecordHotkey,
                        OperatingSystem.IsWindows(), ToggleCommand(), host.Manager);
                    current = setup;
                    _setupWindow = new SetupWindow(setup);
                    _setupWindow.Closed += (_, _) =>
                    {
                        _setupWindow = null;
                        (recorder as IDisposable)?.Dispose();
                    };
                    _ = settings.LoadKeysAsync();
                }
                if (host.PendingJoin is { } link && _setupWindow.DataContext is SetupViewModel wizard)
                {
                    host.PendingJoin = null;
                    wizard.OpenJoinLink(link);
                }
                Activate(_setupWindow);
                break;
            case "versions":
                OpenVersions(host);
                break;
            case "help":
                OpenHelp(Views.Help.HelpCatalog.Overview);
                break;
            case "quit":
                // Erst die IPC-Antwort rausgehen lassen, dann beenden.
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(300);
                    await QuitAsync();
                });
                break;
        }
    }

    /// <summary>Fenster „Version wählen“ (W3-CHAN), aus Tray und Einstellungen; lädt die Liste bei jedem Öffnen neu.</summary>
    private void OpenVersions(AppHost host)
    {
        if (_versionsWindow is null)
        {
            var client = _updateClient ??= new VelopackUpdateClient(feed: null, restartArgs: [.. Environment.GetCommandLineArgs().Skip(1)]);
            var vm = new ViewModels.Updates.VersionListViewModel(client, host.Log, () => host.Controller.Policy, UpdatePrompt.AskInstallAsync,
                channelChosen: channel => SaveUpdateChannelAsync(host, channel));
            _versionsWindow = new Views.Updates.VersionListWindow(vm);
            _versionsWindow.Closed += (_, _) => _versionsWindow = null;
            _ = vm.LoadAsync();
        }
        Activate(_versionsWindow);
    }

    /// <summary>Speichert den Update-Kanal der manuell installierten Version (W3-CHAN), bevor die App neu startet.</summary>
    private static async Task SaveUpdateChannelAsync(AppHost host, UpdateChannel channel)
    {
        var settings = host.Controller.Settings;
        var desktop = SettingsStore.Desktop(settings);
        if (desktop.UpdateChannel == channel.Key())
        {
            return;
        }
        desktop.UpdateChannel = channel.Key();
        SettingsStore.SetDesktop(settings, desktop);
        await host.Controller.ApplySettingsAsync(settings);
    }

    /// <summary>Verlauf (W2-OUT): Kopieren mit Gegenprobe, Einfügen wie im Tray, Neu verarbeiten als Fassung im Hintergrund.</summary>
    internal static Features.History.HistoryActions HistoryActionsFor(AppHost host)
    {
        var c = host.Controller;
        return new Features.History.HistoryActions(
            host.CopyTextAsync,
            host.InsertRecent,
            c.StageLocked ? null : (id, stage) => c.ReprocessVersionAsync(id, stage),
            c.EditHistoryAsync,
            [.. Core.Contracts.RefineModes.SettingsFor(c.Settings.Refine.PromptEnabled)
                .Select(m => (Core.Contracts.RefineModes.Key(m), Localization.Loc.Instance["Stage_" + Core.Contracts.RefineModes.Key(m)]))],
            entry => c.NotifyHistoryChanged(cleared: false),
            () => c.NotifyHistoryChanged(cleared: true),
            Features.History.HistoryActions.StartKeysFor(SettingsStore.Desktop(c.Settings), OperatingSystem.IsWindows()));
    }

    private SettingsViewModel CreateSettings(AppHost host) =>
        new(host.Controller, host.Secrets, new HttpAccessChecker(() => HttpClients.Create(SettingsStore.Desktop(host.Controller.Settings)),
                () => Core.Api.ProviderDirectory.For(host.Controller.Settings)),
            Microphones(host), host.Local, RecordHotkey) { Company = host.Manager, OpenVersions = () => Open("versions"), OpenSetup = () => Open("setup") };

    /// <summary>Eigener Recorder für den Pegel-Test, damit ein laufendes Diktat unberührt bleibt.</summary>
    private static IAudioRecorder CreateTestRecorder(AppHost host) =>
        host.Options.TestAudio is null ? new PortAudioRecorder() : new WavFileRecorder(host.Options.TestAudio);

    /// <summary>Befehl fürs Desktop-Kürzel (Linux), mit dem Pfad dieser App.</summary>
    private static string ToggleCommand() =>
        Autostart.ExecutablePath() is { } exe ? $"\"{exe}\" toggle" : "whisperloom toggle";

    private static IReadOnlyList<string> Microphones(AppHost host)
    {
        if (host.Options.TestAudio is not null)
        {
            return [];
        }
        try
        {
            return PortAudioRecorder.InputDevices();
        }
        catch (AudioUnavailableException)
        {
            return [];
        }
    }

    /// <summary>Hilfe-Fenster öffnen bzw. nach vorn holen, beim Abschnitt <paramref name="topic"/>.</summary>
    private void OpenHelp(string topic)
    {
        if (_helpWindow is null)
        {
            _helpWindow = new Views.Help.HelpWindow(new Views.Help.HelpViewModel());
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        Activate(_helpWindow);
        _helpWindow.ShowTopic(topic);
    }

    private static void Activate(Window window)
    {
        window.Show();
        window.Activate();
    }

    private async Task QuitAsync()
    {
        _host?.Controller.Cancel();
        _host?.Log.Event("app.quit");
        _updates.Cancel();
        _input?.Dispose();
        _tray?.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
        if (_host?.Local is { } local)
        {
            await local.DisposeAsync();
        }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
