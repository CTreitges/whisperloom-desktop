using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Output;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests;

/// <summary>CLI provision/import/export (SyncCommands), Befehlszeile und das Neuladen der laufenden Instanz.</summary>
public sealed class SyncCommandsTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-sync-" + Guid.NewGuid().ToString("N"));
    private readonly MemorySecretStore _secrets = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public SyncCommandsTest() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private Task<int> Run(string verb, string path, bool dryRun = false) =>
        new SyncCommands(_dir, _secrets, _out, _err).RunAsync(verb, path, dryRun, CancellationToken.None);

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task Provision_puts_keys_into_the_store_settings_into_the_file_and_leaves_the_input_untouched()
    {
        var file = Path.Combine(_dir, "it.json");
        File.Copy(Fixture("settings-v1-provisioning.json"), file);
        var hash = SHA256.HashData(File.ReadAllBytes(file));

        Assert.Equal(0, await Run("provision", file));

        Assert.Equal("test-key-not-real-1", _secrets.Values[SecretNames.Stt]);
        Assert.Equal("test-key-not-real-2", _secrets.Values[SecretNames.Llm]);
        var settingsJson = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.DoesNotContain("test-key-not-real", settingsJson, StringComparison.Ordinal);
        Assert.Equal("groq", new SettingsStore(_dir).Load().Stt.ProviderId);
        var policy = new PolicyStore(_dir).Load();
        Assert.True(policy.IsLocked("stt.providerId"));
        Assert.False(policy.HistoryEnabled);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(file)));
        Assert.True(File.Exists(file));
        Assert.Contains("ok provision (Speicher)", _out.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-key-not-real", _out.ToString() + _err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_provisioning_file_writes_nothing()
    {
        var file = Write("bad.json", """{"stt":{"providerId":"mistral"},"secrets":{"mistral":"test-key-not-real-1"},"policy":{"allowedProviders":["groq"]}}""");

        Assert.Equal(1, await Run("provision", file));

        Assert.Empty(_secrets.Values);
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "policy.json")));
        Assert.Contains("error stt.providerId", _err.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-key-not-real", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_respects_locks_and_dry_run_writes_nothing()
    {
        Assert.Equal(0, await Run("provision", Fixture("settings-v1-provisioning.json")));
        var user = Write("user.json", """{"stt":{"providerId":"openai","vocabulary":"Kubernetes"}}""");

        Assert.Equal(0, await Run("import", user, dryRun: true));
        Assert.Equal("", new SettingsStore(_dir).Load().Stt.Vocabulary);
        Assert.Contains("ok import dry-run", _out.ToString(), StringComparison.Ordinal);

        Assert.Equal(0, await Run("import", user));
        var s = new SettingsStore(_dir).Load();
        Assert.Equal("groq", s.Stt.ProviderId);
        Assert.Equal("Kubernetes", s.Stt.Vocabulary);
        Assert.Contains("locked stt.providerId: \"groq\" -> \"openai\"", _out.ToString(), StringComparison.Ordinal);
        Assert.Contains("ok import 1 changes", _out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_after_provision_has_settings_but_no_keys_and_no_policy()
    {
        Assert.Equal(0, await Run("provision", Fixture("settings-v1-provisioning.json")));
        var target = Path.Combine(_dir, "export.json");

        Assert.Equal(0, await Run("export", target));

        var text = File.ReadAllText(target);
        var tree = JsonNode.Parse(text)!.AsObject();
        Assert.Null(tree["secrets"]);
        Assert.Null(tree["policy"]);
        Assert.DoesNotContain("test-key-not-real", text, StringComparison.Ordinal);
        Assert.Equal("groq", tree["stt"]!["providerId"]!.GetValue<string>());
        Assert.Equal(1, tree["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task Broken_json_and_newer_schema_exit_with_1()
    {
        Assert.Equal(1, await Run("import", Write("a.json", "{ nope")));
        Assert.Equal(1, await Run("import", Write("b.json", """{"schemaVersion": 9}""")));
        Assert.Equal(1, await Run("provision", Path.Combine(_dir, "fehlt.json")));
    }

    [Fact]
    public void Command_line_knows_the_sync_verbs()
    {
        var p = CommandLine.Parse(["provision", "it.json", "--data-dir", "d"]);
        var i = CommandLine.Parse(["import", "x.json", "--dry-run"]);

        Assert.True(p.IsLocal);
        Assert.Equal(Path.GetFullPath("it.json"), p.Args[0]);
        Assert.True(i.DryRun);
        Assert.True(CommandLine.Parse(["export", "y.json"]).IsLocal);
        Assert.Throws<UsageException>(() => CommandLine.Parse(["export", "y.json", "--dry-run"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["provision"]));
    }

    // AppHost läuft auf dem UI-Thread (setzt die UI-Sprache, an der Bindungen hängen).
    [AvaloniaFact]
    public async Task Running_instance_reloads_policy_and_refuses_locked_stage_changes()
    {
        var host = new AppHost(new AppOptions(_dir), _secrets, _ => new FakeDictationPipeline(), new FakeRecorder(), new CaptureSink());
        Assert.Equal("stage=polish", (await host.ExecuteAsync("stage", ["next"])).Message);

        Assert.Equal(0, await Run("provision", Write("p.json", """{"refine":{"stage":"beautify"},"policy":{"lockSettings":true,"historyEnabled":false}}""")));
        var reload = await host.ExecuteAsync("reload", []);
        var stage = await host.ExecuteAsync("stage", ["next"]);

        Assert.Equal("reloaded", reload.Message);
        Assert.False(stage.Ok);
        Assert.Equal("stage-locked", stage.Message);
        Assert.Equal("beautify", stage.Status!["stage"]!.GetValue<string>());
        Assert.Equal(RefineMode.Beautify, host.Controller.SetStage("off")); // auch Tray/Hotkey ändern nichts
        var policy = stage.Status["policy"]!;
        Assert.True(policy["active"]!.GetValue<bool>());
        Assert.Contains("refine.stage", policy["locked"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(stage.Status["history"]!["enabled"]!.GetValue<bool>());
    }

    // AppHost läuft auf dem UI-Thread (setzt die UI-Sprache, an der Bindungen hängen).
    [AvaloniaFact]
    public void Broken_policy_file_counts_as_no_policy()
    {
        File.WriteAllText(Path.Combine(_dir, "policy.json"), "{ kaputt");

        var host = new AppHost(new AppOptions(_dir), _secrets, _ => new FakeDictationPipeline(), new FakeRecorder(), new CaptureSink());

        Assert.False(host.Controller.Policy.IsActive);
    }
}

public class DesktopSectionTest
{
    [Fact]
    public void Unknown_desktop_fields_survive_a_save_from_the_settings_window()
    {
        var s = SettingsJson.Deserialize("""{"desktop":{"proxyMode":"off","hotkeys":{"toggle":"Ctrl+Alt+Space"}}}""");

        var desktop = SettingsStore.Desktop(s);
        desktop.ProxyMode = "manual";
        SettingsStore.SetDesktop(s, desktop);

        var tree = JsonNode.Parse(SettingsJson.Serialize(s))!["desktop"]!;
        Assert.Equal("manual", tree["proxyMode"]!.GetValue<string>());
        Assert.Equal("Ctrl+Alt+Space", tree["hotkeys"]!["toggle"]!.GetValue<string>());
    }
}
