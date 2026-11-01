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
    private StageWindow? _stage;
    private HistoryWindow? _historyWindow;
    private SettingsWindow? _settingsWindow;
    private SetupWindow? _setupWindow;
    private IDisposable? _input;
    private readonly CancellationTokenSource _updates = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Program.Startup is { } startup)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => startup.Lock.Dispose();
            Dispatcher.UIThread.Post(async () => await StartAsync(startup.Request.Options, startup.Request.Verb, startup.Request.Args));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(Cli.AppOptions options, string? verb, IReadOnlyList<string> args)
    {
        _host = await AppHost.CreateAsync(options, f => Dispatcher.UIThread.InvokeAsync(f), CancellationToken.None);
        _host.ShowWindow = Open;
        _host.Log.Event("app.start", ("pid", Environment.ProcessId), ("secretStore", _host.Secrets.Kind.Replace(' ', '-')));

        _server = new IpcServer(new InstanceEndpoint(options.DataDir), (request, _) =>
            Dispatcher.UIThread.InvokeAsync(() => _host.ExecuteAsync(request.Verb, request.Args)));
        _server.Start();

        CreateTray(_host);
        StartHotkeys(_host);
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
            var menu = new TrayMenu(host.Controller, Open, entry => host.InsertRecentTextAsync(entry.Text));
            _ = menu.ReloadRecentAsync();
            _tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://WhisperLoom/Assets/whisperloom.ico"))),
                ToolTipText = menu.ToolTip,
                Menu = menu.Menu,
                IsVisible = true,
            };
            _tray.Clicked += (_, _) => Open("stage");
            host.Controller.PropertyChanged += (_, _) => _tray.ToolTipText = menu.ToolTip;
            TrayIcon.SetIcons(this, [_tray]);
        }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException)
        {
            host.Log.Event("tray", ("status", "unavailable"));
            _tray = null;
        }
    }

    /// <summary>Windows: globale Tastenkürzel, Overlay und Einfügen (Phase 3); im Testbetrieb nur mit <c>--hotkeys</c>.</summary>
    private void StartHotkeys(AppHost host)
    {
        if (OperatingSystem.IsWindows() && host.Options.HotkeysActive && host.Output is Platform.Windows.TextOutput.InsertOutputSink sink)
        {
            StartWindowsInput(host, sink);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void StartWindowsInput(AppHost host, Platform.Windows.TextOutput.InsertOutputSink sink)
    {
        var input = Platform.Windows.Input.WindowsInput.Start(host.Controller, sink, host.Log);
        if (input is not null)
        {
            _input = input;
            host.InputStatus = () =>
            {
                var status = input.Flow.Status();
                status["hook"] = input.HookRunning;
                return status;
            };
            RecordHotkey = async () => (await input.RecordAsync())?.ToString();
            host.InsertRecent = input.Flow.InsertRecentAsync;
            host.Reinsert = input.Flow.ReinsertAsync;
        }
    }

    /// <summary>Auto-Update (Phase 6): beim Start und täglich, Installation erst nach Bestätigung; Richtlinie schaltet ab.</summary>
    private void StartUpdates(AppHost host)
    {
        var client = new VelopackUpdateClient(feed: null, restartArgs: [.. Environment.GetCommandLineArgs().Skip(1)]);
        var service = new UpdateService(client, host.Log, () => host.Controller.Policy,
            version => Dispatcher.UIThread.InvokeAsync(() => UpdatePrompt.AskAsync(version)));
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

    /// <summary>"stage", "history", "settings", "setup" oder "quit".</summary>
    private void Open(string what)
    {
        var host = _host;
        if (host is null)
        {
            return;
        }
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
                    var c = host.Controller;
                    var vm = new HistoryViewModel(host.History,
                        c.StageLocked ? null : (entry, stage) => c.ReprocessAsync(stage, entry.Id), cleared => c.NotifyHistoryChanged(cleared));
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

    private SettingsViewModel CreateSettings(AppHost host) =>
        new(host.Controller, host.Secrets, new HttpAccessChecker(() => HttpClients.Create(SettingsStore.Desktop(host.Controller.Settings)),
                () => Core.Api.ProviderDirectory.For(host.Controller.Settings)),
            Microphones(host), host.Local, RecordHotkey) { Company = host.Manager };

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
