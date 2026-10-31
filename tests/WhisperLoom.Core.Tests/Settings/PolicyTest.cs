using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>Auswertung der Richtlinie: Sperren, fester Verlauf, erlaubte Anbieter, policy.json.</summary>
public class PolicyTest
{
    [Fact]
    public void A_locked_path_also_locks_everything_below()
    {
        var p = new PolicySettings { Locked = ["llm", "stt.providerId"] };

        Assert.True(p.IsLocked("llm.model"));
        Assert.True(p.IsLocked("llm.stageModels.polish"));
        Assert.True(p.IsLocked("stt.providerId"));
        Assert.False(p.IsLocked("stt.model"));
        Assert.False(p.IsLocked("llmFallback.model")); // Präfix nur an Pfadgrenzen
        Assert.False(PolicySettings.None.IsLocked("llm.model"));
        Assert.False(PolicySettings.None.IsActive);
    }

    [Fact]
    public void Import_keeps_locked_fields_and_marks_them_in_the_preview()
    {
        var current = new WhisperLoomSettings();
        current.Stt.ProviderId = "groq";
        var policy = new PolicySettings { Locked = ["stt.providerId"] };

        var preview = SyncFixtures.Import("""{"stt":{"providerId":"openai","model":"m2"}}""", current, policy);

        Assert.True(preview.CanApply);
        Assert.Equal("groq", preview.Settings!.Stt.ProviderId);
        Assert.Equal("m2", preview.Settings.Stt.Model);
        Assert.Contains(new SettingsChange("stt.providerId", "\"groq\"", "\"openai\"", Locked: true), preview.Changes);
        Assert.Contains(new SettingsChange("stt.model", "\"\"", "\"m2\""), preview.Changes);
    }

    [Theory]
    [InlineData("""{"desktop": 1}""")]
    [InlineData("""{"desktop": null}""")]
    [InlineData("""{"desktop": "weg"}""")]
    public void Replacing_a_section_by_a_non_object_keeps_its_locked_children(string json)
    {
        var current = SyncFixtures.Import("""{"desktop":{"proxyMode":"manual","proxyUrl":"http://proxy:3128"}}""").Settings!;
        var policy = new PolicySettings { Locked = ["desktop.proxyMode"] };

        var preview = SyncFixtures.Import(json, current, policy);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        var desktop = SyncFixtures.Tree(SyncExport.ToJson(preview.Settings!))["desktop"] as JsonObject;
        Assert.NotNull(desktop);
        Assert.Equal("manual", desktop["proxyMode"]!.GetValue<string>());
        Assert.Contains(preview.Changes, c => c is { Path: "desktop", Locked: true });
        Assert.DoesNotContain(preview.Changes, c => c.Path.StartsWith("desktop.", StringComparison.Ordinal) && !c.Locked);
    }

    [Fact]
    public void Replacing_a_section_without_locks_below_still_works()
    {
        var current = SyncFixtures.Import("""{"desktop":{"proxyMode":"manual"}}""").Settings!;
        var policy = new PolicySettings { Locked = ["stt.providerId"] };

        var preview = SyncFixtures.Import("""{"android": 1, "desktop": {"proxyMode": "off"}}""", current, policy);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Equal("off", SyncFixtures.Tree(SyncExport.ToJson(preview.Settings!))["desktop"]!["proxyMode"]!.GetValue<string>());
    }

    [Fact]
    public void History_policy_is_enforced_and_locks_the_switch()
    {
        var policy = new PolicySettings { HistoryEnabled = false };

        var preview = SyncFixtures.Import("""{"history":{"enabled":true,"size":25}}""", policy: policy);

        Assert.False(preview.Settings!.History.Enabled);
        Assert.Equal(25, preview.Settings.History.Size);
        Assert.True(policy.IsLocked("history.enabled"));
        Assert.Contains(preview.Changes, c => c is { Path: "history.enabled", Locked: false, New: "false" });
    }

    [Fact]
    public void Providers_outside_the_allow_list_block_the_import()
    {
        var policy = new PolicySettings { AllowedProviders = ["groq"] };

        var blocked = SyncFixtures.Import("""{"stt":{"providerId":"openai"}}""", policy: policy);
        var allowed = SyncFixtures.Import("""{"stt":{"providerId":"groq"},"llm":{"providerId":"same"}}""", policy: policy);
        // Abgeschaltete Fallbacks mit einem anderen Anbieter stören nicht.
        var fallbackOff = SyncFixtures.Import("""{"stt":{"providerId":"groq"},"sttFallback":{"enabled":false,"providerId":"openai"}}""", policy: policy);

        Assert.False(blocked.CanApply);
        Assert.Contains(blocked.Errors, e => e.StartsWith("stt.providerId", StringComparison.Ordinal));
        Assert.True(allowed.CanApply, string.Join("; ", allowed.Errors));
        Assert.True(fallbackOff.CanApply, string.Join("; ", fallbackOff.Errors));
    }

    [Fact]
    public void Lock_settings_true_locks_exactly_the_provisioned_paths()
    {
        var p = PolicySettings.FromProvisioning(SyncFixtures.Tree("""{"lockSettings":true}"""), ["stt.providerId", "refine.stage"]);
        var list = PolicySettings.FromProvisioning(SyncFixtures.Tree("""{"lockSettings":["llm"],"updatesEnabled":false}"""), ["stt.providerId"]);
        var none = PolicySettings.FromProvisioning(SyncFixtures.Tree("""{"lockSettings":false}"""), ["stt.providerId"]);

        Assert.Equal(["stt.providerId", "refine.stage"], p.Locked);
        Assert.Equal(["llm"], list.Locked);
        Assert.False(list.UpdatesEnabled);
        Assert.Empty(none.Locked);
        Assert.Throws<SyncFormatException>(() => PolicySettings.FromProvisioning(SyncFixtures.Tree("""{"lockSettings":"ja"}"""), []));
        Assert.Throws<SyncFormatException>(() => PolicySettings.FromProvisioning(SyncFixtures.Tree("""{"historyEnabled":"nein"}"""), []));
    }

    [Fact]
    public void Policy_json_roundtrip_without_empty_fields()
    {
        var p = new PolicySettings { Locked = ["stt", "secrets.stt"], HistoryEnabled = false, AllowedProviders = ["groq"] };

        var json = PolicyJson.Serialize(p);
        var back = PolicyJson.Deserialize(json);

        Assert.Equal(p.Locked, back.Locked);
        Assert.Equal(p.HistoryEnabled, back.HistoryEnabled);
        Assert.Null(back.UpdatesEnabled);
        Assert.Equal(p.AllowedProviders, back.AllowedProviders);
        var tree = JsonNode.Parse(json)!.AsObject();
        Assert.Null(tree["updatesEnabled"]);
        Assert.Null(tree["isActive"]);
    }
}
