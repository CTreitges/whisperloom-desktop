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
        if (request.IsLocal)
        {
            return RunKeyCommand(request);
        }

        var endpoint = new InstanceEndpoint(request.Options.DataDir);
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

    /// <summary><c>key set &lt;name&gt;</c> (Wert von stdin) und <c>key delete &lt;name&gt;</c>. Der Wert wird nie ausgegeben.</summary>
    private static int RunKeyCommand(CliRequest request)
    {
        var (action, name) = (request.Args[0], request.Args[1]);
        try
        {
            var store = SecretStoreFactory.CreateAsync(request.Options.DataDir, request.Options.SecretPrefix, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (action == "set")
            {
                var value = Console.In.ReadToEnd().Trim();
                store.SetAsync(name, value, CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                store.DeleteAsync(name, CancellationToken.None).GetAwaiter().GetResult();
            }
            Console.WriteLine($"ok {action} {name} ({store.Kind})");
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
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
