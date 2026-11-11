using Avalonia.Headless.XUnit;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Management;
using WhisperLoom.App.Secrets;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests;

/// <summary>
/// TS-WLINUX (WLD-P2-24 Detail 2): <c>status</c> meldet Keys als vorhanden/fehlt, ohne den Wert zu lesen
/// (<see cref="ISecretStore.ExistsAsync"/>). Unter Linux hieß das vorher je Aufruf vier <c>secret-tool lookup</c>.
/// </summary>
public sealed class StatusKeyPresenceTest
{
    /// <summary>Zählt Lese- und Vorhanden-Aufrufe getrennt; Vorhanden prüft ohne den Wert.</summary>
    private sealed class CountingSecretStore : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public int Gets { get; private set; }

        public int Exists { get; private set; }

        public string Kind => "Zähler";

        public Task<string?> GetAsync(string name, CancellationToken ct)
        {
            Gets++;
            return Task.FromResult(Values.GetValueOrDefault(name));
        }

        public Task<bool> ExistsAsync(string name, CancellationToken ct)
        {
            Exists++;
            return Task.FromResult(Values.ContainsKey(name));
        }

        public Task SetAsync(string name, string value, CancellationToken ct)
        {
            Values[name] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string name, CancellationToken ct)
        {
            Values.Remove(name);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Func<string, ProcessResult> Answer { get; set; } = _ => new ProcessResult(0, "", "");

        public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
        {
            var call = file + " " + string.Join(" ", args);
            Calls.Add(call);
            return Task.FromResult(Answer(call));
        }

        public Task<int> RunUntilExitAsync(string file, IReadOnlyList<string> args, string stdin, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [AvaloniaFact]
    public async Task Status_asks_only_whether_a_key_exists_never_for_its_value()
    {
        var data = Path.Combine(Path.GetTempPath(), "wl-status-" + Guid.NewGuid().ToString("N"));
        var store = new CountingSecretStore();
        store.Values[SecretNames.Stt] = "test-key-not-real-91";
        try
        {
            var host = new AppHost(new AppOptions(data), store, _ => new FakeDictationPipeline(), new FakeRecorder(), new CaptureSink());

            var status = await host.StatusAsync();

            Assert.Equal(0, store.Gets);
            Assert.Equal(SecretNames.All.Count, store.Exists);
            var keys = status["keys"]!.AsObject();
            Assert.True(keys[SecretNames.Stt]!.GetValue<bool>());
            Assert.False(keys[SecretNames.SttFallback]!.GetValue<bool>());
            Assert.False(keys[SecretNames.Llm]!.GetValue<bool>());
            Assert.False(keys[SecretNames.LlmFallback]!.GetValue<bool>());
            Assert.DoesNotContain("test-key-not-real-91", status.ToJsonString());
        }
        finally
        {
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
            }
        }
    }

    // Firmen-Regeln gelten für „vorhanden“ genauso wie für den Wert, aber ohne die inneren Ablagen zu lesen.
    [Theory]
    [InlineData("none", "user", true)]
    [InlineData("none", "", false)]
    [InlineData("distribute-on-target", "", true)]
    [InlineData("distribute-off-target", "user", false)]
    [InlineData("pending-not-recorded", "user", false)]
    public async Task Company_store_exists_matches_get_without_reading_values(string scenario, string ownKey, bool expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new WhisperLoomSettings();
            var user = new CountingSecretStore();
            var company = new CountingSecretStore();
            var targets = new Dictionary<string, KeyTarget>();
            if (ownKey == "user")
            {
                user.Values[SecretNames.Stt] = "test-key-not-real-92";
            }
            if (scenario.StartsWith("distribute", StringComparison.Ordinal))
            {
                settings.Management.ServerUrl = "https://wl.example.com";
                settings.Management.State = "approved";
                settings.Management.KeyMode = "distribute";
                company.Values[SecretNames.Stt] = "test-key-not-real-93";
                var (providerId, baseUrl, _) = ConfigApply.Access(settings, ConfigApply.Section(SecretNames.Stt));
                targets[SecretNames.Stt] = scenario == "distribute-on-target"
                    ? new KeyTarget(providerId, baseUrl)
                    : new KeyTarget(providerId, "http://127.0.0.1:9");
            }
            if (scenario == "pending-not-recorded")
            {
                settings.Management.ServerUrl = "https://wl.example.com";
                settings.Management.State = "pending";
            }
            var store = new CompanySecretStore(user, company, () => settings, () => PolicySettings.None, () => targets, new OwnKeyTargets(dir));

            var exists = await ((ISecretStore)store).ExistsAsync(SecretNames.Stt, CancellationToken.None);

            Assert.Equal(expected, exists);
            Assert.Equal(0, user.Gets + company.Gets);
            Assert.Equal(expected, await store.GetAsync(SecretNames.Stt, CancellationToken.None) is not null);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Secret_tool_store_checks_existence_over_dbus_without_lookup()
    {
        var runner = new RecordingRunner
        {
            Answer = call => call.Contains("'account': 'stt'", StringComparison.Ordinal)
                ? new ProcessResult(0, "([objectpath '/org/freedesktop/secrets/collection/login/3'], @ao [])\n", "")
                : new ProcessResult(0, "(@ao [], @ao [])\n", ""),
        };
        var store = new SecretToolStore("WhisperLoom-Test-unit:", runner);

        Assert.True(await ((ISecretStore)store).ExistsAsync(SecretNames.Stt, CancellationToken.None));
        Assert.False(await ((ISecretStore)store).ExistsAsync(SecretNames.Llm, CancellationToken.None));

        Assert.All(runner.Calls, c => Assert.DoesNotContain("lookup", c));
        Assert.All(runner.Calls, c => Assert.StartsWith("gdbus call --session --dest org.freedesktop.secrets", c));
        Assert.Contains("org.freedesktop.Secret.Service.SearchItems {'service': 'WhisperLoom-Test-unit', 'account': 'stt'}", runner.Calls[0]);
    }

    [Fact]
    public async Task Secret_tool_store_reports_a_dbus_error_as_io_error()
    {
        var runner = new RecordingRunner { Answer = _ => new ProcessResult(1, "", "Error: GDBus.Error:org.freedesktop.DBus.Error.ServiceUnknown") };
        var store = new SecretToolStore("WhisperLoom-Test-unit:", runner);

        var e = await Assert.ThrowsAsync<IOException>(() => ((ISecretStore)store).ExistsAsync(SecretNames.Stt, CancellationToken.None));
        Assert.DoesNotContain("lookup", string.Join(" ", runner.Calls));
        Assert.Contains("Exit 1", e.Message);
    }
}
