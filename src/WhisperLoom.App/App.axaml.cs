using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Instance;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tray;
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
    private IDisposable? _input;

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
        _host = await AppHost.CreateAsync(options, CancellationToken.None);
        _host.ShowWindow = Open;
        _host.Log.Event("app.start", ("pid", Environment.ProcessId), ("secretStore", _host.Secrets.Kind.Replace(' ', '-')));

        _server = new IpcServer(new InstanceEndpoint(options.DataDir), (request, _) =>
            Dispatcher.UIThread.InvokeAsync(() => _host.ExecuteAsync(request.Verb, request.Args)));
        _server.Start();

        CreateTray(_host);
        StartHotkeys(_host);
        if (verb is not null)
        {
            await _host.ExecuteAsync(verb, args);
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
            var menu = new TrayMenu(host.Controller, Open);
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
        }
    }

    /// <summary>Rekorder für die Einstellungen (nur mit laufendem Hook).</summary>
    internal Func<Task<string?>>? RecordHotkey { get; private set; }

    /// <summary>"stage", "history", "settings" oder "quit".</summary>
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
                    var vm = new HistoryViewModel(host.History);
                    _historyWindow = new HistoryWindow(vm);
                    _historyWindow.Closed += (_, _) => _historyWindow = null;
                    _ = vm.LoadAsync();
                }
                Activate(_historyWindow);
                break;
            case "settings":
                if (_settingsWindow is null)
                {
                    var vm = new SettingsViewModel(host.Controller, host.Secrets, new HttpAccessChecker(() => HttpClients.Create(SettingsStore.Desktop(host.Controller.Settings))), Microphones(host), host.Local, RecordHotkey);
                    _settingsWindow = new SettingsWindow(vm);
                    _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                    // Nach einem Import ist die Kopie des Fensters veraltet: neu öffnen statt sie später zurückzuspeichern.
                    vm.Sync.Applied += () => Dispatcher.UIThread.Post(() =>
                    {
                        _settingsWindow?.Close();
                        _settingsWindow = null;
                        Open("settings");
                    });
                    _ = vm.LoadKeysAsync();
                    _ = vm.Local.RefreshOllamaCommand.ExecuteAsync(null);
                }
                Activate(_settingsWindow);
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
