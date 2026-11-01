using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;
using WhisperLoom.Core.Tests.Api;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>
/// W2-LOCAL: Import, Validierung, Richtlinie und Provisioning für eigene Anbieter (<c>custom:&lt;id&gt;</c>) und lokale
/// Server (<c>local:&lt;name&gt;</c>). Regel K1 aus Lauf 1: ein Host-Wechsel verwirft den gespeicherten Key mit Warnung.
/// </summary>
public class CustomProviderImportTest
{
    private static WhisperLoomSettings Current()
    {
        var s = ProviderDirectoryTest.WithEntries();
        s.Stt.ProviderId = "custom:lab";
        return s;
    }

    [Fact]
    public void Import_that_moves_an_entry_to_another_host_drops_its_key()
    {
        var preview = SyncFixtures.Import(
            """{"providers":{"custom":[{"id":"lab","name":"Labor","protocol":"openai","baseUrl":"https://angreifer.example/v1"}]}}""",
            Current(), storedKeys: ["custom:lab", "stt"]);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Contains(new KeyDrop("custom:lab", "https://angreifer.example"), preview.DroppedKeys);
        Assert.DoesNotContain(preview.DroppedKeys, d => d.Name == "stt"); // der Zugangs-Key ging nie an den Eintrag
    }

    [Fact]
    public void Unused_entry_moving_host_drops_its_key_too()
    {
        var current = ProviderDirectoryTest.WithEntries(); // kein Zugang nutzt „lab“, „Zugang prüfen“ aber schon

        var preview = SyncFixtures.Import(
            """{"providers":{"custom":[{"id":"lab","name":"Labor","baseUrl":"https://angreifer.example/v1"}]}}""",
            current, storedKeys: ["custom:lab"]);

        Assert.Contains(new KeyDrop("custom:lab", "https://angreifer.example"), preview.DroppedKeys);
    }

    [Fact]
    public void Import_that_removes_an_entry_drops_its_key()
    {
        // Prüfrunde 1 K-1: Ein Import ersetzt providers.custom; der Key des entfernten Eintrags darf nicht verwaisen.
        var preview = SyncFixtures.Import("""{"providers":{"custom":[]},"stt":{"providerId":"openai"}}""", Current(), storedKeys: ["custom:lab"]);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Contains(new KeyDrop("custom:lab", KeyRoutes.RemovedEntry), preview.DroppedKeys);
    }

    [Fact]
    public void Provider_key_names_of_a_file_are_read_for_the_orphan_check()
    {
        var file = SyncDocument.Parse("""{"providers":{"custom":[{"id":"lab"},{"id":"Ungültig!"},{"id":"lab"},{"name":"ohne id"}]}}""");

        Assert.Equal(["custom:lab"], SyncImport.ProviderKeyNames(file));
    }

    [Fact]
    public void Import_with_the_same_host_keeps_the_key()
    {
        var preview = SyncFixtures.Import(
            """{"providers":{"custom":[{"id":"lab","name":"Neu","protocol":"openai","baseUrl":"https://lab.example/v1","models":["m-9"]}]}}""",
            Current(), storedKeys: ["custom:lab"]);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Empty(preview.DroppedKeys);
    }

    [Fact]
    public void Switching_a_slot_from_the_cloud_to_an_entry_drops_no_cloud_key_because_none_goes_there()
    {
        var current = ProviderDirectoryTest.WithEntries();
        current.Stt.ProviderId = "openai";

        var preview = SyncFixtures.Import("""{"stt":{"providerId":"custom:lab"}}""", current, storedKeys: ["stt"]);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Empty(preview.DroppedKeys);
        Assert.Contains("https://lab.example", preview.DictationTargets);
    }

    [Fact]
    public void Local_server_from_another_device_is_a_clean_error()
    {
        var preview = SyncFixtures.Import("""{"stt":{"providerId":"local:studio"},"local":{"servers":[{"name":"studio","kind":"stt","baseUrl":"http://127.0.0.1:8000/v1"}]}}""");

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.StartsWith("stt.providerId:", StringComparison.Ordinal));
    }

    [Fact]
    public void Local_server_of_this_device_is_valid_in_a_matching_slot_only()
    {
        var current = ProviderDirectoryTest.WithEntries(servers: new LocalServer { Name = "lm", Kind = "text", BaseUrl = "http://127.0.0.1:1234/v1" });

        var ok = SyncFixtures.Import("""{"llm":{"providerId":"local:lm"}}""", current);
        var wrong = SyncFixtures.Import("""{"stt":{"providerId":"local:lm"}}""", current);

        Assert.True(ok.CanApply, string.Join("; ", ok.Errors));
        Assert.False(wrong.CanApply);
    }

    [Fact]
    public void Text_server_is_device_bound()
    {
        var current = new WhisperLoomSettings();

        var preview = SyncFixtures.Import("""{"local":{"textServer":"fremd","textEngine":"server"}}""", current);
        var export = SyncExport.ToJson(preview.Settings!);

        Assert.DoesNotContain("textServer", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fremd", export, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("custom", "custom:lab", true)]
    [InlineData("custom:lab", "custom:lab", true)]
    [InlineData("custom:andere", "custom:lab", false)]
    [InlineData("local", "local:lm", true)]
    [InlineData("groq", "local:lm", false)]
    public void Allowed_providers_cover_entries(string allowed, string used, bool expected)
    {
        var policy = PolicySettings.None with { AllowedProviders = [allowed] };

        Assert.Equal(expected, policy.IsProviderAllowed(used));
    }

    [Fact]
    public void Provisioning_stores_the_key_of_an_entry_under_its_id()
    {
        var plan = Provisioning.Plan(new WhisperLoomSettings(), SyncDocument.Parse(
            """{"providers":{"custom":[{"id":"lab","name":"Labor","baseUrl":"https://lab.example/v1"}]},"stt":{"providerId":"custom:lab"},"secrets":{"custom:lab":"test-key-not-real-3"},"policy":{"allowedProviders":["custom:lab"]}}"""));

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Equal("test-key-not-real-3", plan.Secrets["custom:lab"]);
        Assert.False(plan.Secrets.ContainsKey(SecretNames.Stt));
    }

    [Fact]
    public void Provisioning_key_for_a_missing_entry_is_an_error()
    {
        var plan = Provisioning.Plan(new WhisperLoomSettings(), SyncDocument.Parse("""{"secrets":{"custom:nix":"test-key-not-real-4"}}"""));

        Assert.False(plan.CanApply);
    }
}
