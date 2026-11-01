using System.Text.Json;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>
/// W2-LOCAL: eigene Anbieter (<c>custom:&lt;id&gt;</c>) und lokale Server (<c>local:&lt;name&gt;</c>) in jedem Zugang.
/// Kern: feste Adresse des Eintrags, nur sein eigener Key — nie der Key eines Zugangs bzw. Cloud-Anbieters.
/// </summary>
public class ProviderDirectoryTest
{
    private const string CloudKey = "test-key-not-real-1";
    private const string EntryKey = "test-key-not-real-2";

    internal static WhisperLoomSettings WithEntries(string protocol = "openai", params LocalServer[] servers)
    {
        var s = new WhisperLoomSettings();
        s.Providers.Custom.Add(new CustomProvider { Id = "lab", Name = "Labor", Protocol = protocol, BaseUrl = "https://lab.example/v1", Models = ["m-1", "m-2"] });
        if (servers.Length > 0)
        {
            s.Extra = new() { ["local"] = JsonSerializer.SerializeToElement(new { servers }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        }
        return s;
    }

    private static AccessKeys Keys(bool entryKey = true) => new(CloudKey, CloudKey, CloudKey, CloudKey,
        entryKey ? new Dictionary<string, string> { ["custom:lab"] = EntryKey } : null);

    [Fact]
    public void Custom_entry_in_the_recognition_uses_its_own_address_and_key()
    {
        var s = WithEntries();
        s.Stt.ProviderId = "custom:lab";
        s.Stt.BaseUrl = "https://angreifer.example/v1"; // Adresse des Zugangs gilt für Einträge nie

        var a = SettingsAccess.Stt(s, Keys());

        Assert.Equal("https://lab.example/v1", a.BaseUrl);
        Assert.Equal(EntryKey, a.ApiKey);
        Assert.Equal("m-1", a.Model);
        Assert.True(a.Provider.IsCustom);
    }

    [Fact]
    public void A_cloud_key_never_goes_to_a_custom_entry()
    {
        var s = WithEntries();
        s.Stt.ProviderId = "custom:lab"; // vorher openai: der Key des Zugangs stammt von dort

        var a = SettingsAccess.Stt(s, Keys(entryKey: false));

        Assert.Equal("", a.ApiKey);
        Assert.Equal("https://lab.example/v1", a.BaseUrl);
    }

    [Theory]
    [InlineData("llm")]
    [InlineData("llmFallback")]
    [InlineData("sttFallback")]
    public void Every_slot_resolves_only_the_entry_key(string slot)
    {
        var s = WithEntries();
        s.SttFallback.Enabled = s.LlmFallback.Enabled = true;
        switch (slot)
        {
            case "llm": s.Llm.ProviderId = "custom:lab"; break;
            case "llmFallback": s.LlmFallback.ProviderId = "custom:lab"; break;
            default: s.SttFallback.ProviderId = "custom:lab"; break;
        }

        var a = slot switch
        {
            "llm" => SettingsAccess.Llm(s, Keys(entryKey: false)),
            "llmFallback" => SettingsAccess.LlmFallback(s, Keys(entryKey: false))!,
            _ => SettingsAccess.SttFallback(s, Keys(entryKey: false))!,
        };
        var withKey = slot switch
        {
            "llm" => SettingsAccess.Llm(s, Keys()),
            "llmFallback" => SettingsAccess.LlmFallback(s, Keys())!,
            _ => SettingsAccess.SttFallback(s, Keys())!,
        };

        Assert.Equal("", a.ApiKey);
        Assert.Equal(EntryKey, withKey.ApiKey);
        Assert.Equal("https://lab.example/v1", withKey.BaseUrl);
    }

    [Fact]
    public void Fallback_of_the_same_entry_keeps_the_entry_key()
    {
        var s = WithEntries();
        s.Stt.ProviderId = s.SttFallback.ProviderId = "custom:lab";
        s.SttFallback.Enabled = true;
        s.SttFallback.Model = "m-2";

        var a = SettingsAccess.SttFallback(s, Keys())!;

        Assert.Equal(EntryKey, a.ApiKey);
        Assert.Equal("m-2", a.Model);
        Assert.True(SetupState.SttFallbackReady(a));
    }

    [Fact]
    public void Local_server_never_gets_a_key()
    {
        var s = WithEntries(servers: new LocalServer { Name = "studio", Kind = "text", BaseUrl = "http://127.0.0.1:1234/v1", Model = "qwen" });
        s.Llm.ProviderId = "local:studio";

        var a = SettingsAccess.Llm(s, Keys());

        Assert.Equal("", a.ApiKey);
        Assert.Equal("http://127.0.0.1:1234/v1", a.BaseUrl);
        Assert.Equal("qwen", a.Model);
        Assert.True(SetupState.LlmReady(a));
    }

    [Fact]
    public void Unknown_entry_is_incomplete_and_never_openai()
    {
        var s = new WhisperLoomSettings();
        s.Stt.ProviderId = "custom:geloescht";

        var a = SettingsAccess.Stt(s, Keys());

        Assert.Equal("", a.BaseUrl);
        Assert.Equal("", a.ApiKey);
        Assert.NotEqual(ProviderCatalog.OpenAiId, a.Provider.Id);
        Assert.False(SetupState.SttComplete(a.BaseUrl, a.ApiKey, a.Provider.NeedsKey));
    }

    [Theory]
    [InlineData("openai", true, true)]
    [InlineData("anthropic", false, true)]
    [InlineData("ollama", false, true)]
    [InlineData("elevenlabs", true, false)]
    public void Protocol_decides_what_an_entry_can_do(string protocol, bool stt, bool llm)
    {
        var p = ProviderDirectory.For(WithEntries(protocol)).Find("custom:lab")!;

        Assert.Equal(stt, p.HasStt);
        Assert.Equal(llm, p.HasLlm);
        Assert.False(p.NeedsUrl);
    }

    [Fact]
    public void Local_server_kind_limits_the_slots()
    {
        var s = WithEntries(servers: [
            new LocalServer { Name = "whisper", Kind = "stt", BaseUrl = "http://127.0.0.1:8000/v1" },
            new LocalServer { Name = "lm", Kind = "text", BaseUrl = "http://127.0.0.1:1234/v1" }]);
        var dir = ProviderDirectory.For(s);

        Assert.True(dir.Find("local:whisper")!.HasStt);
        Assert.False(dir.Find("local:whisper")!.HasLlm);
        Assert.True(dir.Find("local:lm")!.HasLlm);
        Assert.False(dir.Find("local:lm")!.HasStt);
    }

    [Fact]
    public void Anthropic_entry_lists_models_like_anthropic()
    {
        var p = ProviderDirectory.For(WithEntries("anthropic")).Find("custom:lab")!;
        var access = new ApiAccess(p.BaseUrl, EntryKey, "", 90_000, p, null);

        var r = ModelLists.Request(access, ModelKind.Llm);

        Assert.Equal("https://lab.example/v1/models?limit=1000", r.Url);
        Assert.Equal(ModelLists.AnthropicVersion, r.Headers!["anthropic-version"]);
    }

    [Fact]
    public async Task Model_list_of_an_openai_entry_comes_from_v1_models()
    {
        await using var server = new FakeServer();
        server.Map("/v1/models", _ => new FakeResponse(200, """{"data":[{"id":"whisper-large-v3"},{"id":"qwen2.5:7b"}]}"""));
        var s = new WhisperLoomSettings();
        s.Providers.Custom.Add(new CustomProvider { Id = "studio", Name = "LM Studio", BaseUrl = server.Url("/v1"), KeyOptional = true });
        s.Stt.ProviderId = "custom:studio";
        using var http = new HttpClient();
        var access = SettingsAccess.Stt(s, new AccessKeys());

        var models = await ModelLists.LoadAsync(http, access, ModelKind.Stt, CancellationToken.None);
        var llm = await ModelLists.LoadAsync(http, access, ModelKind.Llm, CancellationToken.None);

        Assert.Equal(["whisper-large-v3"], models.Select(m => m.Id));
        Assert.Equal(["qwen2.5:7b"], llm.Select(m => m.Id));
    }

    [Fact]
    public async Task Model_list_of_an_ollama_entry_comes_from_api_tags()
    {
        await using var server = new FakeServer();
        server.Map("/api/tags", _ => new FakeResponse(200, """{"models":[{"name":"gemma4:e4b"},{"name":"nomic-embed-text"}]}"""));
        var s = new WhisperLoomSettings();
        s.Providers.Custom.Add(new CustomProvider { Id = "nas", Name = "NAS", Protocol = "ollama", BaseUrl = server.Base, KeyOptional = true });
        s.Llm.ProviderId = "custom:nas";
        using var http = new HttpClient();

        var models = await ModelLists.LoadAsync(http, SettingsAccess.Llm(s, new AccessKeys()), ModelKind.Llm, CancellationToken.None);

        Assert.Equal(["gemma4:e4b"], models.Select(m => m.Id));
    }

    [Fact]
    public async Task Server_error_of_a_model_list_keeps_the_cached_list()
    {
        await using var server = new FakeServer();
        server.Map("/v1/models", _ => new FakeResponse(200, """{"data":[{"id":"a"}]}"""));
        var s = new WhisperLoomSettings();
        s.Providers.Custom.Add(new CustomProvider { Id = "studio", Name = "S", BaseUrl = server.Url("/v1"), KeyOptional = true });
        s.Llm.ProviderId = "custom:studio";
        using var http = new HttpClient();
        var cache = new ModelCache();
        var access = SettingsAccess.Llm(s, new AccessKeys());
        await cache.RefreshAsync(http, access, ModelKind.Llm, CancellationToken.None);
        server.Map("/v1/models", _ => new FakeResponse(500, "kaputt"));

        await Assert.ThrowsAsync<ApiHttpException>(() => cache.RefreshAsync(http, access, ModelKind.Llm, CancellationToken.None));

        Assert.Equal(["a"], cache.Get("custom:studio", ModelKind.Llm, access.BaseUrl)!.Models.Select(m => m.Id));
    }
}
