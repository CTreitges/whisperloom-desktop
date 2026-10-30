using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/OllamaApiTest.kt</c>: Adressen, Payload, Parser und die Modell-Liste über /api/tags.</summary>
public sealed class OllamaApiTest : IAsyncLifetime
{
    private readonly FakeServer _server = new();
    private readonly HttpClient _http = Http.CreateClient();
    private string? _tagsAuth = "unset";
    private int _tagsStatus = 200;
    private string _tagsResponse = """{"models":[{"name":"qwen3:8b","model":"qwen3:8b"},{"name":"Gemma4:31b"},{"model":"llama3.2"},{"name":"qwen3:8b"}]}""";

    public ValueTask InitializeAsync()
    {
        _server.Map("/api/tags", r =>
        {
            _tagsAuth = r.Header("Authorization");
            return new FakeResponse(_tagsStatus, _tagsResponse);
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private static ApiAccess Access(string provider, string url, string key = "") =>
        AccessResolver.ResolveLlm(AccessResolver.ResolveStt("groq", "", Keys.K1, "", 0), provider, url, key, "");

    private async Task<List<string>> ListModels(ApiAccess access) =>
        [.. (await ModelLists.LoadAsync(_http, access, ModelKind.Llm, TestContext.Current.CancellationToken)).Select(m => m.Id)];

    [Fact]
    public void WurzelOhneApiUndV1()
    {
        Assert.Equal("http://h:11434", OllamaApi.Root("http://h:11434"));
        Assert.Equal("http://h:11434", OllamaApi.Root(" http://h:11434/ "));
        Assert.Equal("http://h:11434", OllamaApi.Root("http://h:11434/v1"));
        Assert.Equal("http://h:11434", OllamaApi.Root("http://h:11434/api/"));
        Assert.Equal("http://h:11434", OllamaApi.Root("http://h:11434/v1/api"));
        Assert.Equal("https://ollama.com/api/chat", OllamaApi.ChatUrl("https://ollama.com"));
        Assert.Equal("https://ollama.com/api/tags", OllamaApi.TagsUrl("https://ollama.com/api"));
    }

    [Fact]
    public void PayloadOhneThinkUndOhneStreaming()
    {
        var json = JsonNode.Parse(OllamaApi.ChatPayload("gemma4:31b", "SYSTEM", "hallo welt"))!.AsObject();
        Assert.Equal("gemma4:31b", (string?)json["model"]);
        Assert.False((bool)json["stream"]!);
        Assert.False(json.ContainsKey("think"));
        Assert.Equal(0, (int)json["options"]!["temperature"]!);
        var messages = json["messages"]!.AsArray();
        Assert.Equal("system", (string?)messages[0]!["role"]);
        Assert.Equal("SYSTEM", (string?)messages[0]!["content"]);
        Assert.Equal("user", (string?)messages[1]!["role"]);
        Assert.Equal("hallo welt", (string?)messages[1]!["content"]);
    }

    [Fact]
    public void GptOssBekommtThinkLow()
    {
        Assert.Equal("low", (string?)JsonNode.Parse(OllamaApi.ChatPayload("gpt-oss:20b", "S", "U"))!["think"]);
        Assert.Equal("low", (string?)JsonNode.Parse(OllamaApi.ChatPayload("GPT-OSS:120b", "S", "U"))!["think"]);
    }

    [Fact]
    public void AntwortParser()
    {
        Assert.Equal("Hallo Welt.", OllamaApi.ParseChat("""{"message":{"role":"assistant","content":"Hallo Welt.","thinking":"lang"},"done":true}"""));
        Assert.Null(OllamaApi.ParseChat("""{"message":{"role":"assistant","content":"  "}}"""));
        Assert.Null(OllamaApi.ParseChat("""{"done":true}"""));
    }

    [Fact]
    public void TagsParserSortiertUndEntferntDuplikate()
    {
        Assert.Equal(["Gemma4:31b", "llama3.2", "qwen3:8b"], OllamaApi.ParseTags(_tagsResponse));
        Assert.Empty(OllamaApi.ParseTags("""{"models":[]}"""));
        Assert.Empty(OllamaApi.ParseTags("{}"));
    }

    [Fact]
    public async Task ModellListeVomLokalenServerOhneKey()
    {
        Assert.Equal(["Gemma4:31b", "llama3.2", "qwen3:8b"], await ListModels(Access("ollama", _server.Url("/v1"))));
        Assert.Null(_tagsAuth);
    }

    [Fact]
    public async Task ModellListeDerCloudMitKey()
    {
        await ListModels(Access("ollama-cloud", _server.Base, Keys.K2));
        Assert.Equal($"Bearer {Keys.K2}", _tagsAuth);
    }

    [Fact]
    public async Task OhneAdresseKeineAnfrage()
    {
        await Assert.ThrowsAsync<ApiNotConfiguredException>(() => ListModels(Access("ollama", "")));
        Assert.Equal("unset", _tagsAuth);
    }

    [Fact]
    public async Task FehlerDerOllamaFormWirdLesbar()
    {
        _tagsStatus = 401;
        _tagsResponse = """{"error":"Unauthorized"}""";
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => ListModels(Access("ollama-cloud", _server.Base, Keys.K3)));
        Assert.Equal(401, e.StatusCode);
        Assert.StartsWith("API-Fehler 401: Unauthorized", e.Message, StringComparison.Ordinal);
        Assert.Contains("API-Key", e.Message, StringComparison.Ordinal);
    }
}
