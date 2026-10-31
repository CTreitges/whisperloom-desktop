using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>Provisioning: Keys je Anbieter → Zugänge, Richtlinie, Sperren, keine Key-Werte in Meldungen.</summary>
public class ProvisioningTest
{
    private static ProvisioningPlan Plan(string json, WhisperLoomSettings? current = null) =>
        Provisioning.Plan(current ?? new WhisperLoomSettings(), SyncDocument.Parse(json));

    [Fact]
    public void Fixture_maps_keys_to_accesses_and_applies_the_policy()
    {
        var plan = Plan(SyncFixtures.Read("settings-v1-provisioning.json"));

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Equal("groq", plan.Settings!.Stt.ProviderId);
        Assert.Equal("openai", plan.Settings.Llm.ProviderId);
        Assert.False(plan.Settings.History.Enabled);
        Assert.Equal("test-key-not-real-1", plan.Secrets[SecretNames.Stt]);
        Assert.Equal("test-key-not-real-2", plan.Secrets[SecretNames.Llm]);
        Assert.False(plan.Policy.UpdatesEnabled);
        Assert.Equal(["groq", "openai"], plan.Policy.AllowedProviders);
        Assert.True(plan.Policy.IsLocked("stt.providerId"));
        Assert.True(plan.Policy.IsLocked("llm.stageModels.polish"));
        Assert.True(plan.Policy.IsLocked("secrets.stt"));
        Assert.True(plan.Policy.IsLocked("history.enabled"));
        Assert.False(plan.Policy.IsLocked("stt.vocabulary")); // nicht in der Datei → frei
    }

    [Fact]
    public void Provisioning_overrides_old_locks()
    {
        var plan = Plan("""{"stt":{"providerId":"openai"}}""");
        Assert.True(plan.CanApply);

        var current = plan.Settings!;
        current.Stt.ProviderId = "groq";
        // Die neue Datei ändert den Anbieter, auch wenn er bisher gesperrt war: das Provisioning ersetzt die Richtlinie.
        var again = Plan("""{"stt":{"providerId":"mistral"},"policy":{"lockSettings":["stt"]}}""", current);

        Assert.Equal("mistral", again.Settings!.Stt.ProviderId);
        Assert.Equal(["stt"], again.Policy.Locked);
    }

    [Fact]
    public void Access_names_win_over_provider_ids()
    {
        var plan = Plan("""
            {"stt":{"providerId":"custom","baseUrl":"http://stt.local/v1"},"llm":{"providerId":"custom","baseUrl":"http://llm.local/v1"},
             "secrets":{"custom":"test-key-not-real-1","llm":"test-key-not-real-2"}}
            """);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Equal("test-key-not-real-1", plan.Secrets[SecretNames.Stt]);
        Assert.Equal("test-key-not-real-2", plan.Secrets[SecretNames.Llm]);
        Assert.False(plan.Policy.IsActive);
    }

    [Fact]
    public void Same_llm_gets_no_own_key_and_unused_providers_warn()
    {
        var plan = Plan("""{"stt":{"providerId":"groq"},"llm":{"providerId":"same"},"secrets":{"groq":"test-key-not-real-1","mistral":"test-key-not-real-2"}}""");

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Equal([SecretNames.Stt], plan.Secrets.Keys);
        Assert.Contains(plan.Warnings, w => w.StartsWith("secrets.mistral", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"secrets":{"irgendwas":"test-key-not-real-1"}}""", "secrets.irgendwas")]
    [InlineData("""{"policy":{"lockSettings":["sst.providerId"]}}""", "sst.providerId")]
    [InlineData("""{"policy":{"allowedProviders":["kein-anbieter"]}}""", "kein-anbieter")]
    [InlineData("""{"stt":{"providerId":"mistral"},"policy":{"allowedProviders":["groq"]}}""", "stt.providerId")]
    [InlineData("""{"policy":{"lockSettings":7}}""", "policy.lockSettings")]
    public void Errors_block_everything_and_never_contain_key_values(string json, string expected)
    {
        var plan = Plan(json);

        Assert.False(plan.CanApply);
        Assert.Null(plan.Settings);
        Assert.Contains(plan.Errors, e => e.Contains(expected, StringComparison.Ordinal));
        Assert.All(plan.Errors.Concat(plan.Warnings), m => Assert.DoesNotContain("test-key-not-real", m, StringComparison.Ordinal));
    }

    [Fact]
    public void Summary_names_paths_and_accesses_but_no_values()
    {
        var plan = Plan(SyncFixtures.Read("settings-v1-provisioning.json"));

        var lines = plan.Summary().ToList();

        Assert.Contains("key stt", lines);
        Assert.Contains("locked stt.providerId", lines);
        Assert.Contains("policy historyEnabled=false", lines);
        Assert.Contains(lines, l => l.StartsWith("setting stt.providerId", StringComparison.Ordinal));
        Assert.All(lines, l => Assert.DoesNotContain("test-key-not-real", l, StringComparison.Ordinal));
    }
}
