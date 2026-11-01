using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;
using WhisperLoom.Core.Tests.Settings;

namespace WhisperLoom.Core.Tests.Contracts;

/// <summary>Neue Settings-Keys aus W2: Standardwerte in JSON, Lesen alter Dateien, Prüfung, Import-Schutz.</summary>
public class NewSettingsKeysTest
{
    [Fact]
    public void All_new_keys_serialize_with_their_defaults()
    {
        var tree = JsonNode.Parse(SettingsJson.Serialize(new WhisperLoomSettings()))!.AsObject();

        var expected = JsonNode.Parse("""
            {
              "output": {"alwaysCopy": false, "pressEnter": false},
              "selection": {"hotkeyCombo": "Ctrl+Alt+Shift+S", "floatingButton": false, "ctrlRightClick": false},
              "providers": {"custom": []},
              "management": {"serverUrl": "", "deviceId": "", "tenantName": "", "state": "none", "lastSync": 0,
                             "keyMode": "proxy", "allowLeave": true, "code": "", "overrides": []}
            }
            """)!.AsObject();
        foreach (var (name, section) in expected)
        {
            Assert.True(JsonNode.DeepEquals(section, tree[name]), $"{name}: {tree[name]?.ToJsonString()}");
        }
        Assert.True(JsonNode.DeepEquals(new JsonArray(), tree["offline"]!["customModels"]));
        Assert.Equal("small", tree["offline"]!["model"]!.GetValue<string>());
    }

    [Fact]
    public void Old_files_without_new_sections_get_defaults_and_full_values_roundtrip()
    {
        var old = SettingsJson.Deserialize("""{"schemaVersion":1,"stt":{"providerId":"groq"}}""");
        Assert.False(old.Output.PressEnter);
        Assert.Equal(SelectionSettings.DefaultHotkey, old.Selection.HotkeyCombo);
        Assert.Equal("none", old.Management.State);
        Assert.True(old.Management.AllowLeave);

        var s = new WhisperLoomSettings();
        s.Output.AlwaysCopy = true;
        s.Selection.FloatingButton = true;
        s.Providers.Custom.Add(new CustomProvider { Id = "lan", Name = "LAN", Protocol = "ollama", BaseUrl = "http://192.168.1.5:11434", KeyOptional = true });
        s.Offline.CustomModels.Add(new CustomWhisperModel { Name = "de-medium", Path = "C:/models/ggml-de-medium.bin" });
        s.Offline.Model = "custom:de-medium";
        s.Management.KeyMode = "distribute";
        s.Management.LastSync = 1_793_350_800_000;

        var back = SettingsJson.Deserialize(SettingsJson.Serialize(s));

        Assert.True(back.Output.AlwaysCopy);
        Assert.True(back.Selection.FloatingButton);
        Assert.Equal(("lan", "ollama", true, false), (back.Providers.Custom[0].Id, back.Providers.Custom[0].Protocol, back.Providers.Custom[0].HasLlm, back.Providers.Custom[0].HasStt));
        Assert.Equal("custom:de-medium", back.Offline.Model);
        Assert.Equal(("distribute", 1_793_350_800_000L), (back.Management.KeyMode, back.Management.LastSync));
        Assert.Empty(SyncValidator.Validate(back));
        Assert.DoesNotContain("hasLlm", SettingsJson.Serialize(s), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"providers":{"custom":[{"id":"A B","name":"x","protocol":"openai","baseUrl":"https://a.example.com"}]}}""", "providers.custom[0].id")]
    [InlineData("""{"providers":{"custom":[{"id":"a","name":"x","protocol":"grpc","baseUrl":"https://a.example.com"}]}}""", "providers.custom[0].protocol")]
    [InlineData("""{"providers":{"custom":[{"id":"a","name":"x","protocol":"openai","baseUrl":"http://8.8.8.8"}]}}""", "providers.custom[0].baseUrl")]
    [InlineData("""{"providers":{"custom":[{"id":"a","name":"x","baseUrl":"https://a.example.com"},{"id":"a","name":"y","baseUrl":"https://b.example.com"}]}}""", "providers.custom[1].id")]
    [InlineData("""{"offline":{"model":"custom:fehlt"}}""", "offline.model")]
    [InlineData("""{"management":{"keyMode":"mixed"}}""", "management.keyMode")]
    [InlineData("""{"local":{"textEngine":"cloud"}}""", "local.textEngine")]
    [InlineData("""{"local":{"servers":[{"name":"x","kind":"tts","baseUrl":"http://127.0.0.1:8080"}]}}""", "local.servers[0].kind")]
    [InlineData("""{"stt":{"providerId":"custom:fehlt"}}""", "stt.providerId")]
    public void Validator_rejects_bad_new_values(string json, string path)
    {
        var s = SettingsJson.Deserialize(json);

        Assert.Contains(SyncValidator.Validate(s), e => e.StartsWith(path, StringComparison.Ordinal));
    }

    [Fact]
    public void Custom_provider_can_be_referenced_by_matching_access()
    {
        var s = SettingsJson.Deserialize("""
            {"providers":{"custom":[{"id":"lan","name":"LAN","protocol":"ollama","baseUrl":"http://192.168.1.5:11434"}]},
             "llm":{"providerId":"custom:lan"},"stt":{"providerId":"custom:lan"}}
            """);

        var errors = SyncValidator.Validate(s);

        Assert.DoesNotContain(errors, e => e.StartsWith("llm.providerId", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("stt.providerId", StringComparison.Ordinal)); // ollama kann nicht erkennen
    }

    [Fact]
    public void Import_takes_global_enter_only_if_it_was_already_on()
    {
        var off = SyncFixtures.Import("""{"output":{"pressEnter":true,"alwaysCopy":true}}""");
        var on = new WhisperLoomSettings();
        on.Output.PressEnter = true;
        var kept = SyncFixtures.Import("""{"output":{"PressEnter":true}}""", on);

        Assert.False(off.Settings!.Output.PressEnter);
        Assert.True(off.Settings.Output.AlwaysCopy);
        Assert.Contains("output.pressEnter", off.EnterDisabled);
        Assert.True(kept.Settings!.Output.PressEnter);
        Assert.Empty(kept.EnterDisabled);
    }

    [Fact]
    public void Device_bound_lists_are_neither_imported_nor_exported()
    {
        var current = new WhisperLoomSettings();
        current.Offline.CustomModels.Add(new CustomWhisperModel { Name = "mine", Path = "C:/m.bin" });

        var preview = SyncFixtures.Import("""
            {"offline":{"customModels":[{"name":"evil","path":"\\\\server\\share\\x.bin"}]},
             "local":{"servers":[{"name":"x","kind":"text","baseUrl":"http://10.0.0.66:8080"}],"backend":"cpu"}}
            """, current);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Equal("mine", Assert.Single(preview.Settings!.Offline.CustomModels).Name);
        var exported = SyncFixtures.Tree(SyncExport.ToJson(preview.Settings));
        Assert.Null(exported["offline"]!["customModels"]);
        Assert.Null(exported["local"]!["servers"]);
        Assert.Equal("cpu", exported["local"]!["backend"]!.GetValue<string>());
    }

    [Fact]
    public void Device_token_name_is_not_an_access_key()
    {
        Assert.Equal("device", SecretNames.Device);
        Assert.DoesNotContain(SecretNames.Device, SecretNames.All);
        var plan = Provisioning.Plan(new WhisperLoomSettings(), SyncDocument.Parse("""{"secrets":{"device":"test-key-not-real-9"}}"""));
        Assert.False(plan.CanApply); // ein Geräte-Token kommt nie aus einer Datei
    }

    [Fact]
    public void Managed_state_keys()
    {
        Assert.Equal(["free", "default", "fixed"], Enum.GetValues<ManagedState>().Select(s => s.Key()));
        Assert.Equal(ManagedState.Default, ManagedStates.FromKey("default"));
        Assert.Null(ManagedStates.FromKey("Fixed"));
    }
}
