using WhisperLoom.App.Secrets;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tests;

public class SecretStoreTest
{
    private const string Key = "test-key-not-real-7";

    [Fact]
    public async Task File_store_round_trip_with_owner_only_rights()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-sec-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "secrets.json");
        var store = new FileSecretStore(path, "WhisperLoom-Test-unit:");
        try
        {
            Assert.Null(await store.GetAsync(SecretNames.Stt, CancellationToken.None));
            await store.SetAsync(SecretNames.Stt, Key, CancellationToken.None);
            Assert.Equal(Key, await store.GetAsync(SecretNames.Stt, CancellationToken.None));
            Assert.Contains("WhisperLoom-Test-unit:stt", await File.ReadAllTextAsync(path));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
            await store.DeleteAsync(SecretNames.Stt, CancellationToken.None);
            await store.DeleteAsync(SecretNames.Stt, CancellationToken.None);
            Assert.Null(await store.GetAsync(SecretNames.Stt, CancellationToken.None));
            Assert.Equal("Datei", store.Kind);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task File_store_tightens_loosened_rights_and_replaces_a_stale_tmp()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix-Rechte nur unter Linux");
            return;
        }
        var dir = Path.Combine(Path.GetTempPath(), "wl-sec-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "secrets.json");
        var store = new FileSecretStore(path, "WhisperLoom-Test-unit:");
        const UnixFileMode loose = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        try
        {
            await store.SetAsync(SecretNames.Stt, Key, CancellationToken.None);
            File.SetUnixFileMode(path, loose);
            Assert.Equal(Key, await store.GetAsync(SecretNames.Stt, CancellationToken.None));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));

            await File.WriteAllTextAsync(path + ".tmp", "{}");
            File.SetUnixFileMode(path + ".tmp", loose);
            await store.SetAsync(SecretNames.Llm, Key, CancellationToken.None);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_unknown_names_and_empty_values_without_echoing_the_value()
    {
        var store = new FileSecretStore(Path.Combine(Path.GetTempPath(), "wl-unused", "s.json"), "WhisperLoom-Test-unit:");
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("openai", Key, CancellationToken.None));
        var e = await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(SecretNames.Llm, "  ", CancellationToken.None));
        Assert.DoesNotContain(Key, e.Message);
    }

    [Fact]
    public async Task Secret_tool_gets_value_via_stdin_not_arguments()
    {
        var runner = new RecordingRunner();
        var store = new SecretToolStore("WhisperLoom-Test-unit:", runner);
        await store.SetAsync(SecretNames.Llm, Key, CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("secret-tool", call.File);
        Assert.Equal(["store", "--label=WhisperLoom-Test-unit:llm", "service", "WhisperLoom-Test-unit", "account", "llm"], call.Args);
        Assert.Equal(Key, call.Stdin);
        Assert.DoesNotContain(Key, string.Join(' ', call.Args));
    }

    [Fact]
    public async Task Secret_tool_lookup_miss_is_null_and_missing_service_is_unavailable()
    {
        var runner = new RecordingRunner { Result = new ProcessResult(1, "", "") };
        Assert.Null(await new SecretToolStore("WhisperLoom:", runner).GetAsync(SecretNames.Stt, CancellationToken.None));
        Assert.True(await SecretToolStore.IsAvailableAsync(runner, CancellationToken.None));

        runner.Result = new ProcessResult(1, "", "The name org.freedesktop.secrets was not provided");
        Assert.False(await SecretToolStore.IsAvailableAsync(runner, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => new SecretToolStore("WhisperLoom:", runner).GetAsync(SecretNames.Stt, CancellationToken.None));
    }

    [Fact]
    public async Task Windows_credential_manager_round_trip()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("nur Windows");
            return;
        }
        var store = new WindowsCredentialStore("WhisperLoom-Test-WLD-P2-unit:");
        try
        {
            await store.SetAsync(SecretNames.SttFallback, Key, CancellationToken.None);
            Assert.Equal(Key, await store.GetAsync(SecretNames.SttFallback, CancellationToken.None));
        }
        finally
        {
            await store.DeleteAsync(SecretNames.SttFallback, CancellationToken.None);
        }
        Assert.Null(await store.GetAsync(SecretNames.SttFallback, CancellationToken.None));
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<(string File, IReadOnlyList<string> Args, string? Stdin)> Calls { get; } = [];

        public ProcessResult Result { get; set; } = new(0, "", "");

        public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
        {
            Calls.Add((file, args.ToList(), stdin));
            return Task.FromResult(Result);
        }

        public Task<int> RunUntilExitAsync(string file, IReadOnlyList<string> args, string stdin, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException("secret-tool braucht die Ausgabe");
    }
}
