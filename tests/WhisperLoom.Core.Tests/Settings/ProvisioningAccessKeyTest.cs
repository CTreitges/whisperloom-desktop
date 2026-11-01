using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>
/// W2-FIX K-1/M-1/M-2 (Security-Review H8, Sonde P8): Provisioning und Server-Konfiguration verwerfen auch die Keys der
/// Zugänge (<c>stt</c>, <c>stt_fallback</c>, <c>llm</c>, <c>llm_fallback</c>), wenn sich Anbieter, Host oder der manuelle Proxy
/// ändern und die Datei keinen eigenen Key dafür liefert — wie der Import.
/// </summary>
public class ProvisioningAccessKeyTest
{
    private const string Attacker = """{"schemaVersion":1,"stt":{"providerId":"custom","baseUrl":"https://angreifer.example.test/v1"}}""";

    private static WhisperLoomSettings OpenAi() => new() { Stt = { ProviderId = "openai" } };

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // ohne Blick in die Ablage: alle Zugänge gelten als gespeichert
    public void P8_Host_change_drops_the_stt_key_like_the_import(bool knownStore)
    {
        var file = SyncDocument.Parse(Attacker);
        var plan = Provisioning.Plan(OpenAi(), file, knownStore ? [SecretNames.Stt] : null);
        var import = SyncImport.Preview(OpenAi(), file, PolicySettings.None, [SecretNames.Stt]);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Contains(new KeyDrop(SecretNames.Stt, "https://angreifer.example.test"), plan.DroppedKeys);
        Assert.Equal(import.DroppedKeys.Where(d => d.Name == SecretNames.Stt), plan.DroppedKeys.Where(d => d.Name == SecretNames.Stt));
        Assert.Contains("key-drop stt", plan.Summary());
        Assert.Contains(plan.Warnings, w => w.StartsWith("secrets.stt: Adresse geändert", StringComparison.Ordinal));
    }

    [Fact]
    public void Same_provider_other_host_drops_the_key()
    {
        var plan = Provisioning.Plan(OpenAi(),
            SyncDocument.Parse("""{"schemaVersion":1,"stt":{"providerId":"openai","baseUrl":"https://angreifer.example.test/v1"}}"""), [SecretNames.Stt]);

        Assert.Equal([SecretNames.Stt], plan.DroppedKeys.Select(d => d.Name));
    }

    [Fact]
    public void Fallback_keys_are_dropped_too()
    {
        var plan = Provisioning.Plan(OpenAi(), SyncDocument.Parse(
            """{"schemaVersion":1,"sttFallback":{"providerId":"custom","baseUrl":"https://angreifer.example.test/v1"},"llmFallback":{"providerId":"custom","baseUrl":"https://angreifer.example.test/v1"}}"""),
            [SecretNames.SttFallback, SecretNames.LlmFallback]);

        Assert.Equal([SecretNames.LlmFallback, SecretNames.SttFallback], plan.DroppedKeys.Select(d => d.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Key_supplied_by_the_file_is_kept()
    {
        var plan = Provisioning.Plan(OpenAi(), SyncDocument.Parse(
            """{"schemaVersion":1,"stt":{"providerId":"custom","baseUrl":"https://angreifer.example.test/v1"},"secrets":{"stt":"test-key-not-real-1"}}"""),
            [SecretNames.Stt]);

        Assert.Empty(plan.DroppedKeys);
        Assert.Equal("test-key-not-real-1", plan.Secrets[SecretNames.Stt]);
    }

    [Fact]
    public void Unchanged_target_keeps_the_key()
    {
        var plan = Provisioning.Plan(OpenAi(), SyncDocument.Parse("""{"schemaVersion":1,"language":"de"}"""), [SecretNames.Stt, SecretNames.Llm]);

        Assert.Empty(plan.DroppedKeys);
    }

    [Fact]
    public void M1_Manual_proxy_from_the_file_drops_every_routed_key()
    {
        var plan = Provisioning.Plan(OpenAi(), SyncDocument.Parse(
            """{"schemaVersion":1,"desktop":{"proxyMode":"manual","proxyUrl":"http://angreifer.example.test:3128"}}"""), [SecretNames.Stt]);

        Assert.Contains(new KeyDrop(SecretNames.Stt, "Proxy http://angreifer.example.test:3128"), plan.DroppedKeys);
    }

    [Fact]
    public void OriginsOf_follows_the_dictation_route()
    {
        var s = OpenAi();
        Assert.Equal(["https://api.openai.com"], KeyRoutes.OriginsOf(s, SecretNames.Stt));
        s.Stt.ProviderId = "custom";
        s.Stt.BaseUrl = "https://Angreifer.example.test:8443/v1";
        Assert.Equal(["https://angreifer.example.test:8443"], KeyRoutes.OriginsOf(s, SecretNames.Stt));
        // „wie Erkennung“: „Zugang prüfen“ schickt den llm-Key an die Erkennung.
        Assert.Contains("https://angreifer.example.test:8443", KeyRoutes.OriginsOf(s, SecretNames.Llm));
    }

    [Fact]
    public void OriginsOf_counts_a_manual_proxy_as_target()
    {
        var s = OpenAi();
        var plain = KeyRoutes.OriginsOf(s, SecretNames.Stt);
        var proxied = SyncImport.Preview(s, SyncDocument.Parse(
            """{"schemaVersion":1,"desktop":{"proxyMode":"manual","proxyUrl":"http://proxy.example.test:3128"}}"""), PolicySettings.None, []).Settings!;

        Assert.DoesNotContain(plain, o => o.StartsWith("Proxy ", StringComparison.Ordinal));
        Assert.Contains("Proxy http://proxy.example.test:3128", KeyRoutes.OriginsOf(proxied, SecretNames.Stt));
    }
}
