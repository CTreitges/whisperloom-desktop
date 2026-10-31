using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Instance;
using WhisperLoom.App.Secrets;

namespace WhisperLoom.App;

internal static class Program
{
    /// <summary>Start der ersten Instanz: Optionen, Verb und der gehaltene Lock. null in Tests.</summary>
    internal static (CliRequest Request, FileStream Lock)? Startup { get; private set; }

    // Doku: Context7 /avaloniaui/avalonia-docs, docs/fundamentals/application-lifetimes.md (abgerufen 2026-10-30).
    // Vor BuildAvaloniaApp keine Avalonia-Typen verwenden: der SynchronizationContext steht noch nicht.
    [STAThread]
    public static int Main(string[] args)
    {
        CliRequest request;
        try
        {
            request = CommandLine.Parse(args);
        }
        catch (UsageException e)
        {
            AttachParentConsole();
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(CommandLine.Usage);
            return 2;
        }

        if (request.Verb is not null)
        {
            AttachParentConsole();
        }
        var endpoint = new InstanceEndpoint(request.Options.DataDir);
        if (request.IsLocal)
        {
            return request.Verb switch
            {
                "bench" => RunBench(request),
                "key" => RunKeyCommand(request),
                _ => RunSyncCommand(request, endpoint),
            };
        }

        if (request.NeedsRunningInstance)
        {
            // Nie den Lock nehmen: sonst hielte ein status-Aufruf eine gerade startende Instanz für die zweite.
            if (Forward(endpoint, request, attempts: 1) is { } code)
            {
                return code;
            }
            Console.WriteLine(request.Json ? "{\"running\":false}" : "WhisperLoom läuft nicht.");
            return 3;
        }
        var instanceLock = endpoint.TryAcquireLock();
        if (instanceLock is null)
        {
            if (Forward(endpoint, request, attempts: 50) is { } code)
            {
                return code;
            }
            Console.Error.WriteLine("WhisperLoom antwortet nicht.");
            return 1;
        }

        Startup = (request, instanceLock);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    // Wird auch vom Designer-Previewer genutzt.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();

    /// <summary>
    /// Reicht das Verb an die laufende Instanz weiter; ohne Verb holt sie die Bühnenansicht nach vorn.
    /// </summary>
    /// <param name="attempts">Versuche im Abstand von 100 ms (die erste Instanz kann gerade erst starten).</param>
    /// <returns>Exit-Code, oder null, wenn keine Instanz antwortet.</returns>
    private static int? Forward(InstanceEndpoint endpoint, CliRequest request, int attempts)
    {
        IpcResponse? response = null;
        for (var attempt = 0; attempt < attempts && response is null; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(100);
            }
            response = SendAsync(endpoint, request).GetAwaiter().GetResult();
        }
        if (response is null)
        {
            return null;
        }
        if (request.Json)
        {
            Console.WriteLine(response.Status?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "{}");
        }
        else if (request.Verb == "status" && response.Status is { } s)
        {
            Console.WriteLine($"state={s["state"]} stage={s["stage"]} language={s["language"]}");
        }
        else
        {
            Console.WriteLine(response.Message);
        }
        return response.Ok ? 0 : 1;
    }

    private static async Task<IpcResponse?> SendAsync(InstanceEndpoint endpoint, CliRequest request)
    {
        // stop wartet auf das Ergebnis der Pipeline: großzügiges Zeitlimit.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var stream = await endpoint.TryConnectAsync(TimeSpan.FromSeconds(1), cts.Token);
        if (stream is null)
        {
            return null;
        }
        await using (stream)
        {
            await IpcProtocol.WriteAsync(stream, new IpcRequest(request.Verb ?? "show", request.Args), cts.Token);
            return await IpcProtocol.ReadAsync<IpcResponse>(stream, cts.Token);
        }
    }

    /// <summary><c>key set|delete &lt;name&gt;</c> über <see cref="KeyCommand"/>; gesperrte Keys der Richtlinie werden abgelehnt.</summary>
    private static int RunKeyCommand(CliRequest request)
    {
        var (action, name) = (request.Args[0], request.Args[1]);
        try
        {
            var store = SecretStoreFactory.CreateAsync(request.Options.DataDir, request.Options.SecretPrefix, CancellationToken.None)
                .GetAwaiter().GetResult();
            Core.Settings.PolicySettings policy;
            try
            {
                policy = new Services.PolicyStore(request.Options.DataDir).Load();
            }
            catch (JsonException)
            {
                policy = Core.Settings.PolicySettings.None; // wie beim App-Start: kaputte policy.json = keine Richtlinie
            }
            return KeyCommand.RunAsync(action, name, () => Console.In.ReadToEnd(), store, policy, Console.Out, Console.Error, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    /// <summary><c>bench</c>: Messung der lokalen Erkennung, ohne App-Instanz (Begleitprozess endet mit dem Befehl).</summary>
    private static int RunBench(CliRequest request)
    {
        try
        {
            return Platform.Sidecar.Bench.RunAsync(request, Console.Out, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is Core.Local.DownloadException or Core.Contracts.LocalNotAvailableException
                                      or Core.Contracts.ApiNetworkException or Core.Contracts.ApiHttpException or IOException)
        {
            Console.Error.WriteLine($"bench: {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// <c>provision</c>, <c>import</c>, <c>export</c> im eigenen Prozess; danach lädt eine laufende Instanz Einstellungen
    /// und Richtlinie neu (sonst überschriebe sie die Datei beim nächsten Speichern mit ihrem alten Stand).
    /// </summary>
    private static int RunSyncCommand(CliRequest request, InstanceEndpoint endpoint)
    {
        int code;
        try
        {
            var store = SecretStoreFactory.CreateAsync(request.Options.DataDir, request.Options.SecretPrefix, CancellationToken.None)
                .GetAwaiter().GetResult();
            code = new SyncCommands(request.Options.DataDir, store, Console.Out, Console.Error)
                .RunAsync(request.Verb!, request.Args[0], request.DryRun, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            Console.Error.WriteLine($"error {e.Message}");
            return 1;
        }
        if (code == 0 && request.Verb is "provision" or "import" && !request.DryRun)
        {
            Forward(endpoint, request with { Verb = "reload", Args = [], Json = false }, attempts: 1);
        }
        return code;
    }

    /// <summary>
    /// WinExe hat keine Konsole: für CLI-Aufrufe aus einer Konsole an die des Aufrufers hängen. Mit umgeleiteten
    /// Handles (Skripte, Smokes) ist das unnötig, die erbt der Prozess.
    /// </summary>
    // Doku: learn.microsoft.com/windows/console/attachconsole (abgerufen 2026-10-30).
    private static void AttachParentConsole()
    {
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
        {
            AttachConsole(-1);
        }
    }

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks.
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
#pragma warning restore SYSLIB1054
}
