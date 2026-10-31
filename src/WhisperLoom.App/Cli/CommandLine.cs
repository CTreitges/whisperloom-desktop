namespace WhisperLoom.App.Cli;

/// <summary>Was ein Prozessstart verlangt: ein CLI-Verb (oder keins) plus Start- und Test-Schalter.</summary>
/// <param name="Verb">null = nur starten bzw. die laufende Instanz zeigen.</param>
/// <param name="Args">Argumente des Verbs, z. B. <c>["next"]</c> bei <c>stage next</c>.</param>
/// <param name="DryRun"><c>import --dry-run</c>: nur die Vorschau ausgeben.</param>
internal sealed record CliRequest(string? Verb, IReadOnlyList<string> Args, AppOptions Options, bool Json, bool DryRun = false)
{
    /// <summary>Verben, die eine laufende Instanz voraussetzen und keine starten.</summary>
    public bool NeedsRunningInstance => Verb is "stop" or "cancel" or "stage" or "status" or "pause" or "resume" or "quit";

    /// <summary>Verben, die der aufrufende Prozess selbst erledigt (Key nie über die IPC).</summary>
    public bool IsLocal => Verb is "key" or "bench" or "provision" or "import" or "export";
}

/// <summary>Start- und Test-Schalter. Gelten nur für die erste Instanz; ein zweiter Start nutzt sie zum Finden.</summary>
/// <param name="DataDir">Datenordner (Einstellungen, Verlauf, Log). Bestimmt auch, welche Instanz gemeint ist.</param>
/// <param name="TestAudio">WAV statt Mikrofon (Tests, Smokes).</param>
/// <param name="OutputFile">Ausgabe-Senke statt Zwischenablage (Tests, Smokes).</param>
/// <param name="SecretPrefix">Präfix der Key-Ablage, Standard <c>WhisperLoom:</c>; Tests nutzen <c>WhisperLoom-Test-&lt;id&gt;:</c>.</param>
/// <param name="Hotkeys">Schalter <c>--hotkeys</c>: globale Tastenkürzel (Windows) auch im Testbetrieb. Ohne ihn sind sie mit
/// <c>--test-audio</c> bzw. <c>--output-file</c> aus, damit Smokes nie Tasten des Nutzers abfangen.</param>
internal sealed record AppOptions(string DataDir, string? TestAudio = null, string? OutputFile = null, string SecretPrefix = AppOptions.DefaultSecretPrefix, bool Hotkeys = false)
{
    /// <summary>Globale Tastenkürzel installieren: im Normalbetrieb immer, im Testbetrieb nur mit <c>--hotkeys</c>.</summary>
    public bool HotkeysActive => Hotkeys || (TestAudio is null && OutputFile is null);

    public const string DefaultSecretPrefix = "WhisperLoom:";

    /// <summary>Windows: %APPDATA%\WhisperLoom; Linux: $XDG_CONFIG_HOME/whisperloom bzw. ~/.config/whisperloom.</summary>
    public static string DefaultDataDir()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhisperLoom");
        }
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(config))
        {
            config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(config, "whisperloom");
    }
}

/// <summary>Fehler in der Befehlszeile; die Meldung geht an stderr, Exit-Code 2.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>Zerlegt die Befehlszeile. Verben: toggle, start, stop, cancel, pause, resume, stage next|&lt;name&gt;, status [--json], show, key set|delete &lt;name&gt;, quit.</summary>
internal static class CommandLine
{
    public const string Usage =
        "Aufruf: WhisperLoom [toggle|start|stop|cancel|pause|resume|stage next|stage <name>|status [--json]|show|quit|key set <name>|key delete <name>]\n" +
        "        [provision <datei>|import <datei> [--dry-run]|export <datei>]\n" +
        "        [--data-dir <pfad>] [--test-audio <wav>] [--output-file <pfad>] [--secret-prefix <präfix>] [--hotkeys]\n" +
        "        WhisperLoom bench --model <tiny|base|small|large-v3-turbo> --seconds <n> [--backend auto|cpu]";

    private static readonly HashSet<string> Verbs =
        ["toggle", "start", "stop", "cancel", "pause", "resume", "stage", "status", "show", "key", "quit", "bench", "provision", "import", "export"];

    /// <summary>Schalter des Verbs <c>bench</c>; sie landen samt Wert in <see cref="CliRequest.Args"/>.</summary>
    private static readonly HashSet<string> BenchSwitches = ["--model", "--seconds", "--backend"];

    public static CliRequest Parse(IReadOnlyList<string> args)
    {
        string? dataDir = null, testAudio = null, outputFile = null, prefix = null;
        var json = false;
        var hotkeys = false;
        var dryRun = false;
        var positional = new List<string>();
        var benchArgs = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (BenchSwitches.Contains(a))
            {
                benchArgs.Add(a);
                benchArgs.Add(Value(args, ref i, a));
                continue;
            }
            switch (a)
            {
                case "--data-dir": dataDir = Value(args, ref i, a); break;
                case "--test-audio": testAudio = Value(args, ref i, a); break;
                case "--output-file": outputFile = Value(args, ref i, a); break;
                case "--secret-prefix": prefix = Value(args, ref i, a); break;
                case "--json": json = true; break;
                case "--hotkeys": hotkeys = true; break;
                case "--dry-run": dryRun = true; break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new UsageException($"Unbekannter Schalter: {a}");
                    }
                    positional.Add(a);
                    break;
            }
        }

        if (prefix is not null && !prefix.StartsWith("WhisperLoom", StringComparison.Ordinal))
        {
            throw new UsageException("--secret-prefix muss mit \"WhisperLoom\" beginnen.");
        }
        var options = new AppOptions(
            Path.GetFullPath(dataDir ?? AppOptions.DefaultDataDir()),
            testAudio is null ? null : Path.GetFullPath(testAudio),
            outputFile is null ? null : Path.GetFullPath(outputFile),
            prefix ?? AppOptions.DefaultSecretPrefix,
            hotkeys);

        if (positional.Count == 0)
        {
            return new CliRequest(null, [], options, json);
        }
        var verb = positional[0];
        if (!Verbs.Contains(verb))
        {
            throw new UsageException($"Unbekannter Befehl: {verb}");
        }
        var rest = positional.Skip(1).ToList();
        var expected = verb switch
        {
            "stage" => 1,
            "key" => 2,
            "provision" or "import" or "export" => 1,
            _ => 0,
        };
        if (rest.Count != expected)
        {
            throw new UsageException($"Falsche Argumente für {verb}.");
        }
        if (verb == "key" && rest[0] is not ("set" or "delete"))
        {
            throw new UsageException("key kennt nur set und delete.");
        }
        if (benchArgs.Count > 0 && verb != "bench")
        {
            throw new UsageException($"{benchArgs[0]} gilt nur für bench.");
        }
        if (verb == "bench")
        {
            if (!double.TryParse(Arg(benchArgs, "--seconds") ?? "10", System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds is <= 0 or > 3600)
            {
                throw new UsageException("--seconds braucht eine Zahl zwischen 0 und 3600.");
            }
            if (Arg(benchArgs, "--backend") is { } backend && backend is not ("auto" or "cpu"))
            {
                throw new UsageException("--backend kennt nur auto und cpu.");
            }
            return new CliRequest(verb, benchArgs, options, json);
        }
        if (dryRun && verb != "import")
        {
            throw new UsageException("--dry-run gibt es nur für import.");
        }
        if (verb is "provision" or "import" or "export")
        {
            rest = [Path.GetFullPath(rest[0])];
        }
        return new CliRequest(verb, rest, options, json, dryRun);
    }

    private static string? Arg(List<string> args, string name)
    {
        var i = args.IndexOf(name);
        return i >= 0 ? args[i + 1] : null;
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string name)
    {
        if (i + 1 >= args.Count)
        {
            throw new UsageException($"{name} braucht einen Wert.");
        }
        i++;
        return args[i];
    }
}
