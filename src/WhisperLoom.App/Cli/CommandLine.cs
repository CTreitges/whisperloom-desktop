namespace WhisperLoom.App.Cli;

/// <summary>Was ein Prozessstart verlangt: ein CLI-Verb (oder keins) plus Start- und Test-Schalter.</summary>
/// <param name="Verb">null = nur starten bzw. die laufende Instanz zeigen.</param>
/// <param name="Args">Argumente des Verbs, z. B. <c>["next"]</c> bei <c>stage next</c>.</param>
internal sealed record CliRequest(string? Verb, IReadOnlyList<string> Args, AppOptions Options, bool Json)
{
    /// <summary>Verben, die eine laufende Instanz voraussetzen und keine starten.</summary>
    public bool NeedsRunningInstance => Verb is "stop" or "cancel" or "stage" or "status" or "pause" or "resume" or "quit";

    /// <summary>Verben, die der aufrufende Prozess selbst erledigt (Key nie über die IPC).</summary>
    public bool IsLocal => Verb == "key";
}

/// <summary>Start- und Test-Schalter. Gelten nur für die erste Instanz; ein zweiter Start nutzt sie zum Finden.</summary>
/// <param name="DataDir">Datenordner (Einstellungen, Verlauf, Log). Bestimmt auch, welche Instanz gemeint ist.</param>
/// <param name="TestAudio">WAV statt Mikrofon (Tests, Smokes).</param>
/// <param name="OutputFile">Ausgabe-Senke statt Zwischenablage (Tests, Smokes).</param>
/// <param name="SecretPrefix">Präfix der Key-Ablage, Standard <c>WhisperLoom:</c>; Tests nutzen <c>WhisperLoom-Test-&lt;id&gt;:</c>.</param>
internal sealed record AppOptions(string DataDir, string? TestAudio = null, string? OutputFile = null, string SecretPrefix = AppOptions.DefaultSecretPrefix)
{
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
        "        [--data-dir <pfad>] [--test-audio <wav>] [--output-file <pfad>] [--secret-prefix <präfix>]";

    private static readonly HashSet<string> Verbs =
        ["toggle", "start", "stop", "cancel", "pause", "resume", "stage", "status", "show", "key", "quit"];

    public static CliRequest Parse(IReadOnlyList<string> args)
    {
        string? dataDir = null, testAudio = null, outputFile = null, prefix = null;
        var json = false;
        var positional = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--data-dir": dataDir = Value(args, ref i, a); break;
                case "--test-audio": testAudio = Value(args, ref i, a); break;
                case "--output-file": outputFile = Value(args, ref i, a); break;
                case "--secret-prefix": prefix = Value(args, ref i, a); break;
                case "--json": json = true; break;
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
            prefix ?? AppOptions.DefaultSecretPrefix);

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
        return new CliRequest(verb, rest, options, json);
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
