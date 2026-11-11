using System.ComponentModel;
using WhisperLoom.App.Secrets;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tests;

/// <summary>
/// TS-WLINUX Nacharbeit (Review Befund 2): Randfälle von <see cref="ISecretStore.ExistsAsync"/> in den Linux-Ablagen —
/// Datei ohne Secret Service sowie Secret Service per gdbus (gesperrtes Item, gdbus fehlt, Maskierung).
/// </summary>
public sealed class SecretStoreExistsTest : IDisposable
{
    private const string Prefix = "WhisperLoom-Test-exists:";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-exists-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "secrets.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    /// <summary>Fake der Prozess-Naht: protokolliert jeden Aufruf; je Programm eine Antwort oder „nicht installiert“.</summary>
    private sealed class Runner : IProcessRunner
    {
        public Dictionary<string, Func<IReadOnlyList<string>, ProcessResult>> Installed { get; } = [];

        public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
        {
            Calls.Add((file, args.ToList()));
            return Installed.TryGetValue(file, out var answer)
                ? Task.FromResult(answer(args))
                : throw new Win32Exception(2, "nicht gefunden");
        }

        public Task<int> RunUntilExitAsync(string file, IReadOnlyList<string> args, string stdin, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static Task<bool> Exists(ISecretStore store, string name) => store.ExistsAsync(name, CancellationToken.None);

    // --- Datei-Ablage (Linux ohne Secret Service) -------------------------------------------------------------------

    [Fact]
    public async Task File_store_reports_a_stored_key_as_present_and_others_as_missing()
    {
        var store = new FileSecretStore(FilePath, Prefix);
        await store.SetAsync(SecretNames.Stt, "test-key-not-real-101", CancellationToken.None);
        // Gleicher Name unter anderem Präfix zählt nicht (andere Instanz bzw. Test-Präfix).
        await new FileSecretStore(FilePath, "WhisperLoom-Test-other:").SetAsync(SecretNames.Llm, "test-key-not-real-102", CancellationToken.None);

        Assert.True(await Exists(store, SecretNames.Stt));
        Assert.False(await Exists(store, SecretNames.Llm));
        Assert.False(await Exists(store, SecretNames.SttFallback));
    }

    [Fact]
    public async Task File_store_without_a_file_reports_missing_and_creates_nothing()
    {
        var store = new FileSecretStore(FilePath, Prefix);

        Assert.False(await Exists(store, SecretNames.Stt));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task File_store_with_an_unreadable_file_throws_io_error_without_the_content()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(FilePath, "{\"WhisperLoom-Test-exists:stt\": \"test-key-not-real-103\"");
        var store = new FileSecretStore(FilePath, Prefix);

        var e = await Assert.ThrowsAsync<IOException>(() => Exists(store, SecretNames.Stt));
        Assert.DoesNotContain("test-key-not-real-103", e.Message);
    }

    [Fact]
    public async Task File_store_tightens_a_loosened_file_before_checking()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix-Rechte nur unter Linux");
        var store = new FileSecretStore(FilePath, Prefix);
        await store.SetAsync(SecretNames.Stt, "test-key-not-real-104", CancellationToken.None);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            Assert.True(await Exists(store, SecretNames.Stt));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
        }
    }

    // --- Secret Service per gdbus ----------------------------------------------------------------------------------

    [Fact]
    public async Task Secret_service_counts_a_locked_item_as_present_without_unlocking()
    {
        var runner = new Runner { Installed = { ["gdbus"] = _ => new ProcessResult(0, "(@ao [], [objectpath '/org/freedesktop/secrets/collection/login/4'])\n", "") } };
        var store = new SecretToolStore(Prefix, runner);

        Assert.True(await Exists(store, SecretNames.Stt));
        Assert.Equal(new[] { "gdbus" }, runner.Calls.Select(c => c.File));
        Assert.DoesNotContain(runner.Calls, c => c.Args.Any(a => a.Contains("Unlock", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Secret_service_without_gdbus_falls_back_to_lookup()
    {
        var runner = new Runner
        {
            Installed =
            {
                ["secret-tool"] = args => args[^1] == SecretNames.Stt
                    ? new ProcessResult(0, "test-key-not-real-105\n", "")
                    : new ProcessResult(1, "", ""),
            },
        };
        var store = new SecretToolStore(Prefix, runner);

        Assert.True(await Exists(store, SecretNames.Stt));
        Assert.False(await Exists(store, SecretNames.Llm));
        Assert.Equal(new[] { "gdbus", "secret-tool", "gdbus", "secret-tool" }, runner.Calls.Select(c => c.File));
        Assert.Equal(new[] { "lookup", "service", "WhisperLoom-Test-exists", "account", SecretNames.Stt }, runner.Calls[1].Args);
    }

    [Fact]
    public async Task Secret_service_query_escapes_quotes_and_backslashes_in_the_prefix()
    {
        var runner = new Runner { Installed = { ["gdbus"] = _ => new ProcessResult(0, "(@ao [], @ao [])\n", "") } };
        var store = new SecretToolStore(@"WhisperLoom-Test-o'k\x:", runner);

        Assert.False(await Exists(store, SecretNames.Stt));
        // GVariant-Text: ' → \' und \ → \\; sonst endete der String vorzeitig und der Rest würde als Syntax gelesen.
        Assert.Equal(@"{'service': 'WhisperLoom-Test-o\'k\\x', 'account': 'stt'}", runner.Calls.Single().Args[^1]);
    }

    [Fact]
    public async Task Secret_service_query_passes_provider_key_names_verbatim()
    {
        var runner = new Runner { Installed = { ["gdbus"] = _ => new ProcessResult(0, "([objectpath '/org/freedesktop/secrets/collection/login/9'], @ao [])\n", "") } };
        var store = new SecretToolStore(Prefix, runner);

        Assert.True(await Exists(store, "custom:acme_1"));
        Assert.Equal("{'service': 'WhisperLoom-Test-exists', 'account': 'custom:acme_1'}", runner.Calls.Single().Args[^1]);
    }
}
