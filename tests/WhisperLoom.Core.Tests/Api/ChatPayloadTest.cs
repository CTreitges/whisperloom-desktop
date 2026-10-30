using WhisperLoom.Core.Api;
using WhisperLoom.Core.Tests.Support;
using static WhisperLoom.Core.Api.ChatPayload;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/ChatPayloadTest.kt</c>: Sampling-Entscheidung des Chat-Bodys.</summary>
public class ChatPayloadTest
{
    private static ApiAccess Llm(string sttProvider, string llmProvider, string model, string url = "")
    {
        var stt = AccessResolver.ResolveStt(sttProvider, url, Keys.K1, "", 0);
        return AccessResolver.ResolveLlm(stt, llmProvider, "", "", model);
    }

    [Fact]
    public void KlassischesModellBekommtNurTemperature() =>
        Assert.Equal(new Sampling(Temperature: 0), SamplingFor(Llm("openai", "same", "gpt-4o-mini")));

    [Fact]
    public void OpenAiReasoningModellOhneTemperature()
    {
        var s = SamplingFor(Llm("openai", "same", "gpt-5.6-luna"));
        Assert.Null(s.Temperature);
        Assert.Equal("none", s.ReasoningEffort);
        Assert.Equal(MaxCompletionTokens, s.MaxCompletionTokens);
    }

    [Fact]
    public void AltesGpt5MiniNimmtMinimal() =>
        Assert.Equal(new Sampling(null, "minimal", 16384), SamplingFor(Llm("openai", "same", "gpt-5-mini")));

    [Fact]
    public void GroqGptOssBekommtTemperatureUndLow() =>
        Assert.Equal(new Sampling(0, "low"), SamplingFor(Llm("groq", "same", "openai/gpt-oss-20b")));

    [Fact]
    public void GeminiFlashLiteSchaltetDenkenAb() =>
        Assert.Equal(new Sampling(0, "none"), SamplingFor(Llm("openai", "gemini", "gemini-2.5-flash-lite")));

    [Fact]
    public void EigenerServerImmerReasoningNone() =>
        Assert.Equal(new Sampling(0, "none"), SamplingFor(Llm("custom", "same", "qwen3:8b", "http://s:11434/v1")));

    [Fact]
    public void UnbekanntesCloudModellBleibtKlassisch() =>
        Assert.Equal(new Sampling(Temperature: 0), SamplingFor(Llm("openai", "same", "irgendwas-neues")));

    [Fact]
    public void Gemini3OhneTemperature()
    {
        foreach (var model in new[] { "", "gemini-3.5-flash-lite" })
        {
            Assert.Equal(new Sampling(MaxCompletionTokens: MaxCompletionTokens), SamplingFor(Llm("openai", "gemini", model)));
        }
        Assert.Equal(new Sampling(null, "low", MaxCompletionTokens), SamplingFor(Llm("openai", "gemini", "gemini-3.8-flash")));
        Assert.Null(SamplingFor(Llm("openai", "openrouter", "google/gemini-3.8-flash")).Temperature);
        Assert.Equal(0, SamplingFor(Llm("openai", "gemini", "gemini-2.5-flash-lite")).Temperature);
    }

    [Fact]
    public void ClaudeOhneTemperatureMitThinkingJeModell()
    {
        var haiku = new Sampling(MaxCompletionTokens: 16384, ThinkingType: "disabled");
        Assert.Equal(haiku, SamplingFor(Llm("openai", "anthropic", "")));
        Assert.Equal(haiku, SamplingFor(Llm("openai", "anthropic", "claude-haiku-5-5")));
        Assert.Equal("between_tools", SamplingFor(Llm("openai", "anthropic", "claude-sonnet-5-5")).ThinkingType);
        Assert.Equal(new Sampling(MaxCompletionTokens: 16384), SamplingFor(Llm("openai", "anthropic", "claude-opus-5-5")));
        Assert.Equal(new Sampling(MaxCompletionTokens: 16384), SamplingFor(Llm("openai", "openrouter", "anthropic/claude-sonnet-5.5")));
    }

    [Fact]
    public void DeepSeekSchaltetDasDenkenAbUndBehaeltTemperature()
    {
        foreach (var model in new[] { "", "deepseek-flash", "deepseek-v4-pro" })
        {
            Assert.Equal(new Sampling(0, ThinkingType: "disabled"), SamplingFor(Llm("openai", "deepseek", model)));
        }
    }

    /// <summary>Desktop: der Body selbst (Kotlin prüft ihn in ChatPayloadJsonTest, hier als Ergänzung).</summary>
    [Fact]
    public void BodyEnthaeltNurGesetzteFelder()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Build(Llm("openai", "anthropic", "claude-haiku-5-5"), "S", "U"))!;
        Assert.Equal("claude-haiku-5-5", (string?)json["model"]);
        Assert.Null(json["temperature"]);
        Assert.Equal(16384, (int?)json["max_completion_tokens"]);
        Assert.Equal("disabled", (string?)json["thinking"]!["type"]);
        Assert.Equal("system", (string?)json["messages"]![0]!["role"]);
        Assert.Equal("U", (string?)json["messages"]![1]!["content"]);
    }
}
