using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>
/// Schutz vor fremden Einstellungsdateien (Security-Review K1/R1): kein Vokabular-Pfad, kein gespeicherter Key an ein
/// neues Ziel, http nur wie in <see cref="Api.ServerUrlCheck"/>, Freigabeliste der IT auch für die Adresse.
/// </summary>
public class ImportSafetyTest
{
    private static readonly string[] AllKeys = [.. SecretNames.All];

    private static WhisperLoomSettings Groq()
    {
        var s = new WhisperLoomSettings();
        s.Stt.ProviderId = "groq";
        return s;
    }

    [Fact]
    public void Import_with_a_vocabulary_file_changes_nothing()
    {
        var preview = SyncFixtures.Import("""{"stt":{"vocabularyFile":"C:/Users/x/.ssh/id_ed25519"}}""");

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Equal("", preview.Settings!.Stt.VocabularyFile);
        Assert.Empty(preview.Changes);
    }

    [Fact]
    public void Import_keeps_the_own_vocabulary_file()
    {
        var current = new WhisperLoomSettings();
        current.Stt.VocabularyFile = "C:/daten/namen.md";

        var preview = SyncFixtures.Import("""{"stt":{"vocabularyFile":"C:/fremd/geheim.txt","model":"m2"}}""", current);

        Assert.Equal("C:/daten/namen.md", preview.Settings!.Stt.VocabularyFile);
    }

    [Fact]
    public void Export_has_no_vocabulary_file()
    {
        var s = new WhisperLoomSettings();
        s.Stt.VocabularyFile = "C:/daten/namen.md";

        var json = SyncExport.ToJson(s);

        Assert.DoesNotContain("vocabularyFile", json, StringComparison.Ordinal);
        Assert.DoesNotContain("namen.md", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"stt":{"baseUrl":"https://angreifer.example/openai/v1"}}""", "stt", "stt", "https://angreifer.example")]
    [InlineData("""{"stt":{"providerId":"openai"}}""", "stt", "stt", "https://api.openai.com")]
    // Ohne eigenen Key erbt ein Zugang desselben Anbieters den der Erkennung — und schickte ihn an die neue Adresse.
    [InlineData("""{"sttFallback":{"providerId":"groq","baseUrl":"https://angreifer.example/v1"}}""", "stt", "stt", "https://angreifer.example")]
    [InlineData("""{"llm":{"providerId":"groq","baseUrl":"https://angreifer.example/v1"}}""", "stt", "stt", "https://angreifer.example")]
    [InlineData("""{"llm":{"providerId":"groq","baseUrl":"https://angreifer.example/v1"}}""", "stt,llm", "llm", "https://angreifer.example")]
    // Ein abgeschalteter Fallback zählt mit: der Schalter lässt sich später einfach umlegen.
    [InlineData("""{"llmFallback":{"enabled":false,"providerId":"openai","baseUrl":"https://angreifer.example/v1"}}""", "llm_fallback", "llm_fallback", "https://angreifer.example")]
    [InlineData("""{"sttFallback":{"providerId":"mistral"}}""", "stt_fallback", "stt_fallback", "https://api.mistral.ai")]
    [InlineData("""{"desktop":{"proxyMode":"manual","proxyUrl":"http://angreifer.example:3128"}}""", "stt,llm", "stt", "Proxy http://angreifer.example:3128")]
    // Die App liest desktop ohne Rücksicht auf Groß-/Kleinschreibung (JsonSerializerDefaults.Web).
    [InlineData("""{"desktop":{"ProxyMode":"manual","PROXYURL":"http://10.0.0.5:3128"}}""", "stt", "stt", "Proxy http://10.0.0.5:3128")]
    // „wie Erkennung“: der Prüfknopf schickt den llm-Key an die Adresse der Erkennung.
    [InlineData("""{"stt":{"baseUrl":"https://angreifer.example/openai/v1"}}""", "stt,llm", "llm", "https://angreifer.example")]
    public void Import_that_moves_a_stored_key_to_another_host_drops_it(string json, string stored, string key, string target)
    {
        var preview = SyncFixtures.Import(json, Groq(), storedKeys: stored.Split(','));

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Contains(new KeyDrop(key, target), preview.DroppedKeys);
        Assert.All(preview.DroppedKeys, d => Assert.Contains(d.Name, stored.Split(',')));
    }

    [Theory]
    [InlineData("""{"stt":{"model":"whisper-large-v3"}}""")]
    [InlineData("""{"stt":{"baseUrl":"https://API.groq.com/openai/v1/"}}""")] // gleicher Host
    [InlineData("""{"desktop":{"proxyMode":"off"}}""")]
    public void Import_that_keeps_the_host_keeps_the_keys(string json)
    {
        var preview = SyncFixtures.Import(json, Groq(), storedKeys: AllKeys);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Empty(preview.DroppedKeys);
    }

    [Fact]
    public void Same_manual_proxy_in_another_spelling_keeps_the_keys()
    {
        var current = SettingsJson.Deserialize("""{"stt":{"providerId":"groq"},"desktop":{"proxyMode":"manual","proxyUrl":"http://proxy.example:3128"}}""");

        var preview = SyncFixtures.Import("""{"desktop":{"proxyUrl":"HTTP://Proxy.example:3128/"}}""", current, storedKeys: AllKeys);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Empty(preview.DroppedKeys);
    }

    [Theory]
    [InlineData("""{"local":{"ollamaUrl":"http://angreifer.example:11434"}}""")]
    [InlineData("""{"local":{"OllamaUrl":"http://angreifer.example:11434"}}""")]
    public void Import_with_an_ollama_address_changes_nothing(string json)
    {
        var current = SettingsJson.Deserialize("""{"local":{"ollamaModel":"gemma3"}}""");

        var preview = SyncFixtures.Import(json, current);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Empty(preview.Changes);
        Assert.DoesNotContain("angreifer", SettingsJson.Serialize(preview.Settings!), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_locked_by_it_is_never_dropped_the_import_is_refused()
    {
        var policy = new PolicySettings { Locked = ["secrets.stt"] };

        var preview = SyncFixtures.Import("""{"stt":{"baseUrl":"https://angreifer.example/openai/v1"}}""", Groq(), policy, [SecretNames.Stt]);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.StartsWith("secrets.stt:", StringComparison.Ordinal));
        Assert.Empty(preview.DroppedKeys);
    }

    [Fact]
    public void Without_a_stored_key_nothing_is_dropped()
    {
        var preview = SyncFixtures.Import("""{"stt":{"baseUrl":"https://angreifer.example/openai/v1"}}""", Groq(), storedKeys: []);

        Assert.Empty(preview.DroppedKeys);
    }

    [Theory]
    [InlineData("""{"stt":{"providerId":"groq","baseUrl":"http://api.example.com/openai/v1"}}""", "stt.baseUrl")] // braucht https
    [InlineData("""{"stt":{"providerId":"custom","baseUrl":"http://server.example.com/v1"}}""", "stt.baseUrl")] // öffentlich, unverschlüsselt
    [InlineData("""{"llm":{"providerId":"ollama","baseUrl":"http://ollama.example.com:11434"}}""", "llm.baseUrl")]
    [InlineData("""{"sttFallback":{"providerId":"openai","baseUrl":"http://127.0.0.1:9/v1"}}""", "sttFallback.baseUrl")]
    public void Validator_rejects_http_like_the_server_url_check(string json, string path)
    {
        var preview = SyncFixtures.Import(json);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.StartsWith(path, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"stt":{"providerId":"custom","baseUrl":"http://192.168.1.5:8000/v1"}}""")]
    [InlineData("""{"stt":{"providerId":"custom","baseUrl":"https://server.example.com/v1"}}""")]
    [InlineData("""{"llm":{"providerId":"ollama","baseUrl":"http://localhost:11434"}}""")]
    public void Validator_accepts_https_and_private_http(string json)
    {
        var preview = SyncFixtures.Import(json);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
    }

    [Theory]
    [InlineData("""{"stt":{"baseUrl":"https://privat-server.example/v1"}}""", "stt.baseUrl")]
    [InlineData("""{"llm":{"providerId":"openai","baseUrl":"https://privat-server.example/v1"}}""", "llm.baseUrl")]
    [InlineData("""{"sttFallback":{"enabled":true,"providerId":"openai","baseUrl":"https://privat-server.example/v1"}}""", "sttFallback.baseUrl")]
    public void Allowed_providers_also_pin_the_catalog_host(string json, string path)
    {
        var current = new WhisperLoomSettings();
        current.Stt.ProviderId = "openai";
        var policy = new PolicySettings { AllowedProviders = ["openai"] };

        var preview = SyncFixtures.Import(json, current, policy);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.StartsWith(path, StringComparison.Ordinal));
    }

    [Fact]
    public void Allowed_providers_accept_the_catalog_host_and_a_custom_server()
    {
        var current = new WhisperLoomSettings();
        current.Stt.ProviderId = "openai";
        var policy = new PolicySettings { AllowedProviders = ["openai", "custom"] };

        var preview = SyncFixtures.Import(
            """{"stt":{"baseUrl":"https://api.openai.com/v1/"},"llm":{"providerId":"custom","baseUrl":"https://intern.example/v1"}}""",
            current, policy);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
    }
}
