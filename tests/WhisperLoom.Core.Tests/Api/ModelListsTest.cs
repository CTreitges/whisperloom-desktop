using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/ModelListsTest.kt</c>: Adressen/Header je Anbieter und Filterregeln der Modell-Listen.</summary>
public class ModelListsTest
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static ProviderInfo P(string id) => ProviderCatalog.ById(id);

    private static List<string> Ids(string provider, ModelKind kind, string body) =>
        [.. ModelLists.Parse(P(provider), kind, body, Today).Select(m => m.Id)];

    private static string Data(params string[] entries) => "{\"object\":\"list\",\"data\":[" + string.Join(",", entries) + "]}";

    private static ApiAccess Stt(string provider, string url = "") => AccessResolver.ResolveStt(provider, url, Keys.K1, "");

    private static ApiAccess Llm(string provider, string url = "") => AccessResolver.ResolveLlm(Stt("groq"), provider, url, Keys.K2, "");

    [Fact]
    public void OpenAiFormNutztBaseUrlPlusModelsMitBearer()
    {
        var r = ModelLists.Request(Stt("openai"), ModelKind.Stt);
        Assert.Equal("https://api.openai.com/v1/models", r.Url);
        Assert.Null(r.AuthHeader);
        Assert.Empty(r.Headers ?? new Dictionary<string, string>());
        Assert.True(r.FollowRedirects);
        Assert.Equal("https://api.deepinfra.com/v1/openai/models", ModelLists.Request(Stt("deepinfra"), ModelKind.Stt).Url);
        Assert.Equal("https://api.deepseek.com/models", ModelLists.Request(Llm("deepseek"), ModelKind.Llm).Url);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/models", ModelLists.Request(Llm("gemini"), ModelKind.Llm).Url);
        Assert.Equal("http://h:8000/v1/models", ModelLists.Request(Stt("custom", "http://h:8000/v1/"), ModelKind.Stt).Url);
    }

    [Fact]
    public void AnthropicMitLimitUndVersionOhneWeiterleitung()
    {
        var r = ModelLists.Request(Llm("anthropic"), ModelKind.Llm);
        Assert.Equal("https://api.anthropic.com/v1/models?limit=1000", r.Url);
        Assert.Equal(new Dictionary<string, string> { ["anthropic-version"] = "2023-06-01" }, r.Headers);
        Assert.Null(r.AuthHeader);
        Assert.False(r.FollowRedirects);
    }

    [Fact]
    public void OpenRouterFiltertErkennungsModelleSchonAmServer()
    {
        Assert.Equal("https://openrouter.ai/api/v1/models?output_modalities=transcription", ModelLists.Request(Stt("openrouter"), ModelKind.Stt).Url);
        Assert.Equal("https://openrouter.ai/api/v1/models", ModelLists.Request(Llm("openrouter"), ModelKind.Llm).Url);
    }

    [Fact]
    public void ElevenLabsMitEigenemKeyHeaderOhneWeiterleitung()
    {
        var r = ModelLists.Request(Stt("elevenlabs"), ModelKind.Stt);
        Assert.Equal("https://api.elevenlabs.io/v1/models", r.Url);
        Assert.Equal("xi-api-key", r.AuthHeader);
        Assert.False(r.FollowRedirects);
    }

    [Fact]
    public void OllamaNutztApiTags()
    {
        Assert.Equal("https://ollama.com/api/tags", ModelLists.Request(Llm("ollama-cloud"), ModelKind.Llm).Url);
        Assert.Equal("http://h:11434/api/tags", ModelLists.Request(Llm("ollama", "http://h:11434/v1"), ModelKind.Llm).Url);
    }

    private static readonly string OpenAi = Data(
        """{"id":"gpt-transcribe","object":"model","created":1,"owned_by":"system","shutdown_date":null}""",
        """{"id":"gpt-4o-transcribe","object":"model","owned_by":"system","shutdown_date":"2027-02-26"}""",
        """{"id":"gpt-4o-mini-transcribe","object":"model","owned_by":"system"}""",
        """{"id":"gpt-4o-mini-transcribe-2025-03-20","object":"model","shutdown_date":"2026-03-01"}""",
        """{"id":"gpt-4o-transcribe-diarize","object":"model"}""",
        """{"id":"gpt-live-transcribe","object":"model"}""",
        """{"id":"gpt-realtime-whisper","object":"model"}""",
        """{"id":"whisper-1","object":"model"}""",
        """{"id":"gpt-6-astra","object":"model"}""",
        """{"id":"gpt-6.1-sol","object":"model"}""",
        """{"id":"gpt-5.4-mini","object":"model"}""",
        """{"id":"gpt-5-mini","object":"model","shutdown_date":"2026-12-11"}""",
        """{"id":"gpt-4o-mini","object":"model"}""",
        """{"id":"o4-mini","object":"model"}""",
        """{"id":"chatgpt-4o-latest","object":"model"}""",
        """{"id":"gpt-4.5-preview","object":"model","shutdown_date":1752451200}""",
        """{"id":"gpt-5-pro","object":"model"}""",
        """{"id":"gpt-5-codex","object":"model"}""",
        """{"id":"gpt-5.6-cyber","object":"model"}""",
        """{"id":"gpt-daybreak-red-latest","object":"model"}""",
        """{"id":"gpt-4o-mini-search-preview","object":"model"}""",
        """{"id":"gpt-3.5-turbo-instruct","object":"model"}""",
        """{"id":"gpt-4o-mini-tts","object":"model"}""",
        """{"id":"gpt-4o-audio-preview","object":"model"}""",
        """{"id":"gpt-realtime","object":"model"}""",
        """{"id":"gpt-image-1","object":"model"}""",
        """{"id":"text-embedding-3-small","object":"model"}""",
        """{"id":"omni-moderation-latest","object":"model"}""",
        """{"id":"dall-e-3","object":"model"}""",
        """{"id":"davinci-002","object":"model"}""",
        """{"id":"computer-use-preview","object":"model"}""");

    [Fact]
    public void OpenAiErkennungNurUploadTauglicheTranskription() =>
        Assert.Equal(["gpt-4o-mini-transcribe", "gpt-4o-transcribe", "gpt-transcribe", "whisper-1"], Ids("openai", ModelKind.Stt, OpenAi));

    [Fact]
    public void OpenAiChatOhneResponsesOnlyUndOhneAbgeschaltete() =>
        Assert.Equal(
            ["chatgpt-4o-latest", "gpt-4o-mini", "gpt-5-mini", "gpt-5.4-mini", "gpt-6-astra", "gpt-6.1-sol", "o4-mini"],
            Ids("openai", ModelKind.Llm, OpenAi));

    [Fact]
    public void KuenftigesAbschaltDatumWirdHinweis()
    {
        var models = ModelLists.Parse(P("openai"), ModelKind.Llm, OpenAi, Today);
        Assert.Equal("Auslauf 2026-12-11", models.First(m => m.Id == "gpt-5-mini").Note);
        Assert.Null(models.First(m => m.Id == "gpt-4o-mini").Note);
        Assert.Equal("Auslauf 2027-02-26", ModelLists.Parse(P("openai"), ModelKind.Stt, OpenAi, Today).First(m => m.Id == "gpt-4o-transcribe").Note);
    }

    [Fact]
    public void GroqNurAktiveUndNachName()
    {
        var body = Data(
            """{"id":"whisper-large-v3","object":"model","owned_by":"OpenAI","active":true,"context_window":448}""",
            """{"id":"whisper-large-v3-turbo","object":"model","owned_by":"OpenAI","active":true}""",
            """{"id":"openai/gpt-oss-20b","object":"model","active":true,"context_window":131072}""",
            """{"id":"openai/gpt-oss-120b","object":"model"}""",
            """{"id":"qwen/qwen3.8-27b","object":"model","active":true,"public_apps":null}""",
            """{"id":"llama-3.1-8b-instant","object":"model","active":false}""",
            """{"id":"canopylabs/orpheus-v1-english","object":"model","active":true}""",
            """{"id":"meta-llama/llama-prompt-guard-2-22m","object":"model","active":true}""",
            """{"id":"openai/gpt-oss-safeguard-20b","object":"model","active":true}""",
            """{"id":"groq/compound","object":"model","active":true}""");
        Assert.Equal(["whisper-large-v3", "whisper-large-v3-turbo"], Ids("groq", ModelKind.Stt, body));
        Assert.Equal(["openai/gpt-oss-120b", "openai/gpt-oss-20b", "qwen/qwen3.8-27b"], Ids("groq", ModelKind.Llm, body));
    }

    private static string Mistral(string id, string caps, string aliases = "[]", string deprecation = "null", string extra = "") =>
        "{\"id\":\"" + id + "\",\"object\":\"model\",\"type\":\"base\",\"capabilities\":{" + caps + "},\"aliases\":" + aliases + ",\"deprecation\":" + deprecation + extra + "}";

    private static readonly string MistralBody = Data(
        Mistral("voxtral-mini-2602", "\"audio_transcription\":true", "[\"voxtral-mini-latest\"]"),
        Mistral("voxtral-mini-latest", "\"audio_transcription\":true", "[\"voxtral-mini-2602\"]"),
        Mistral("voxtral-mini-transcribe-realtime-2602", "\"audio_transcription\":true,\"audio_transcription_realtime\":true"),
        Mistral("voxtral-mini-tts-2603", "\"audio_speech\":true"),
        Mistral("voxtral-small-2507", "\"completion_chat\":true,\"audio\":true", "[\"voxtral-small-latest\"]"),
        Mistral("mistral-small-2603", "\"completion_chat\":true,\"function_calling\":true", "[\"mistral-small-latest\"]"),
        Mistral("mistral-small-latest", "\"completion_chat\":true", "[\"mistral-small-2603\"]"),
        Mistral("mistral-medium-3-5", "\"completion_chat\":true", deprecation: "\"2027-01-31T00:00:00Z\""),
        Mistral("open-mistral-7b", "\"completion_chat\":true", deprecation: "\"2026-03-30T00:00:00Z\""),
        Mistral("mistral-ocr-4-1", "\"completion_chat\":true,\"ocr\":true"),
        Mistral("mistral-moderation-2603", "\"moderation\":true"),
        Mistral("ft:mistral-small:abc", "\"completion_chat\":true", extra: ",\"archived\":true"));

    [Fact]
    public void MistralNachCapabilitiesUndJeAliasGruppeEinmalMitLatest()
    {
        Assert.Equal(["voxtral-mini-latest"], Ids("mistral", ModelKind.Stt, MistralBody));
        Assert.Equal(["mistral-medium-3-5", "mistral-small-latest", "voxtral-small-latest"], Ids("mistral", ModelKind.Llm, MistralBody));
        Assert.Equal("Auslauf 2027-01-31", ModelLists.Parse(P("mistral"), ModelKind.Llm, MistralBody, Today).First(m => m.Id == "mistral-medium-3-5").Note);
    }

    [Fact]
    public void MistralOhneCapabilitiesNachName()
    {
        var body = Data("""{"id":"voxtral-mini-latest"}""", """{"id":"voxtral-mini-tts-2603"}""", """{"id":"voxtral-mini-transcribe-realtime-2602"}""", """{"id":"mistral-small-latest"}""");
        Assert.Equal(["voxtral-mini-latest"], Ids("mistral", ModelKind.Stt, body));
        Assert.Equal(["mistral-small-latest", "voxtral-mini-latest"], Ids("mistral", ModelKind.Llm, body));
    }

    [Fact]
    public void TogetherTopLevelArrayNachTypOhneDeepgram()
    {
        const string body = """
            [
              {"id":"openai/whisper-large-v3","object":"model","type":"transcribe","display_name":"Whisper large-v3","pricing":{"base":0}},
              {"id":"nvidia/parakeet-tdt-0.6b-v3","object":"model","display_name":"Parakeet TDT"},
              {"id":"deepgram/nova-3-multi","object":"model","type":"transcribe"},
              {"id":"moonshotai/Kimi-K3","object":"model","type":"chat"},
              {"id":"cartesia/sonic-3","object":"model","type":"audio"},
              {"id":"BAAI/bge-large-en-v1.5","object":"model","type":"embedding"}
            ]
            """;
        var models = ModelLists.Parse(P("together"), ModelKind.Stt, body, Today);
        Assert.Equal(["nvidia/parakeet-tdt-0.6b-v3", "openai/whisper-large-v3"], models.Select(m => m.Id));
        Assert.Equal("Whisper large-v3", models[1].Label);
        Assert.Empty(Ids("together", ModelKind.Llm, body));
        Assert.Equal(["openai/whisper-large-v3"], Ids("together", ModelKind.Stt, Data("""{"id":"openai/whisper-large-v3","type":"transcribe"}""")));
    }

    [Fact]
    public void DeepInfraNachMetadataTags()
    {
        static string E(string id, string tags) =>
            "{\"id\":\"" + id + "\",\"object\":\"model\",\"created\":0,\"owned_by\":\"deepinfra\",\"root\":\"" + id + "\",\"parent\":null,\"metadata\":{\"description\":\"x\",\"context_length\":0,\"pricing\":{\"input_seconds\":0.0001},\"tags\":" + tags + "}}";
        var body = Data(
            E("openai/whisper-large-v3-turbo", "[\"stt\"]"),
            E("Qwen/Qwen3-ASR-1.7B", "[\"stt\"]"),
            E("mistralai/Voxtral-Mini-3B-2507", "[\"stt\"]"),
            E("moonshotai/Kimi-K3", "[\"chat\",\"reasoning\",\"reasoning_effort\"]"),
            E("hexgrad/Kokoro-82M", "[\"tts\"]"),
            E("BAAI/bge-m3", "[\"embed\"]"),
            """{"id":"openai/whisper-large-v3","object":"model"}""");
        Assert.Equal(
            ["mistralai/Voxtral-Mini-3B-2507", "openai/whisper-large-v3", "openai/whisper-large-v3-turbo", "Qwen/Qwen3-ASR-1.7B"],
            Ids("deepinfra", ModelKind.Stt, body));
    }

    private static string OrModel(string id, string input = "[\"text\"]", string output = "[\"text\"]",
        string? parameters = "[\"temperature\",\"max_tokens\"]", string? reasoning = null, string expires = "null") =>
        $$"""{"id":"{{id}}","canonical_slug":"{{id}}","name":"Name {{id}}","created":1,"context_length":1000,"expiration_date":{{expires}},"architecture":{"modality":"text->text","input_modalities":{{input}},"output_modalities":{{output}},"tokenizer":"Other"},"pricing":{"prompt":"0.000001","completion":"0.000005"}""" +
        (parameters is null ? "" : $",\"supported_parameters\":{parameters}") +
        (reasoning is null ? "" : $",\"reasoning\":{reasoning}") + "}";

    [Fact]
    public void OpenRouterErkennungAusDemGefiltertenEndpunkt()
    {
        var body = "{\"data\":[" +
            OrModel("openai/whisper-1", "[\"audio\"]", "[\"transcription\"]", "[]") + "," +
            OrModel("mistralai/voxtral-mini-transcribe", "[\"audio\"]", "[\"transcription\"]", "[]") +
            "],\"links\":{\"next\":null},\"total_count\":2}";
        var models = ModelLists.Parse(P("openrouter"), ModelKind.Stt, body, Today);
        Assert.Equal(["mistralai/voxtral-mini-transcribe", "openai/whisper-1"], models.Select(m => m.Id));
        Assert.Equal("Name openai/whisper-1", models[1].Label);
        Assert.Null(models[1].TemperatureSupported);
    }

    [Fact]
    public void OpenRouterChatNurTextOhneBatchRouterGuardUndMitFlags()
    {
        string[] entries =
        [
            OrModel("anthropic/claude-haiku-4.5", input: "[\"text\",\"image\"]", reasoning: """{"mandatory":false,"supported_efforts":["low","high"]}"""),
            OrModel("mistralai/mistral-small-2603", reasoning: """{"mandatory":false,"default_enabled":false,"supported_efforts":["none","low"],"default_effort":"none"}"""),
            OrModel("openai/gpt-6.1-sol", parameters: "[\"reasoning\",\"max_tokens\"]", reasoning: """{"mandatory":true,"supported_efforts":["low","medium","high"]}"""),
            OrModel("qwen/qwen3.8-27b:free", parameters: null),
            OrModel("~anthropic/claude-haiku-latest"),
            OrModel("google/gemini-2.5-flash-lite", expires: "\"2026-10-20\""),
            OrModel("google/gemini-2.0-flash-001", expires: "\"2026-06-01\""),
            OrModel("google/gemini-3.1-flash-image", output: "[\"image\",\"text\"]"),
            OrModel("openai/gpt-audio", output: "[\"text\",\"audio\"]"),
            OrModel("x/vision-only", input: "[\"image\"]"),
            OrModel("anthropic/claude-sonnet-5.5:batch"),
            OrModel("openrouter/auto"),
            OrModel("meta-llama/llama-guard-4-12b"),
            OrModel("openai/gpt-oss-safeguard-20b"),
        ];
        var body = "{\"data\":[" + string.Join(",", entries) + "],\"links\":{\"next\":null},\"total_count\":14}";
        var models = ModelLists.Parse(P("openrouter"), ModelKind.Llm, body, Today).ToDictionary(m => m.Id);
        Assert.Equal(
            new[] { "~anthropic/claude-haiku-latest", "anthropic/claude-haiku-4.5", "google/gemini-2.5-flash-lite", "mistralai/mistral-small-2603", "openai/gpt-6.1-sol", "qwen/qwen3.8-27b:free" }
                .OrderBy(s => s.ToLowerInvariant(), StringComparer.Ordinal),
            models.Keys);
        Assert.True(models["anthropic/claude-haiku-4.5"].TemperatureSupported);
        Assert.Null(models["anthropic/claude-haiku-4.5"].ReasoningEffort);
        Assert.Equal("none", models["mistralai/mistral-small-2603"].ReasoningEffort);
        Assert.False(models["openai/gpt-6.1-sol"].TemperatureSupported);
        Assert.Null(models["openai/gpt-6.1-sol"].ReasoningEffort);
        Assert.Null(models["qwen/qwen3.8-27b:free"].TemperatureSupported);
        Assert.Equal("Auslauf 2026-10-20", models["google/gemini-2.5-flash-lite"].Note);
        Assert.Equal("Name anthropic/claude-haiku-4.5", models["anthropic/claude-haiku-4.5"].Label);
    }

    [Fact]
    public void AnthropicBehaeltDieReihenfolgeUndNutztDisplayName()
    {
        const string body = """
            {"data":[
              {"type":"model","id":"claude-opus-5-5","display_name":"Claude Opus 5.5","created_at":"2026-09-01T00:00:00Z","capabilities":{"effort":{"low":{"supported":true}}}},
              {"type":"model","id":"claude-sonnet-5","display_name":"Claude Sonnet 5","created_at":"2026-05-01T00:00:00Z"},
              {"type":"model","id":"claude-haiku-4-5","display_name":"Claude Haiku 4.5","created_at":"1970-01-01T00:00:00Z"}
            ],"has_more":false,"first_id":"claude-opus-5-5","last_id":"claude-haiku-4-5"}
            """;
        var models = ModelLists.Parse(P("anthropic"), ModelKind.Llm, body, Today);
        Assert.Equal(["claude-opus-5-5", "claude-sonnet-5", "claude-haiku-4-5"], models.Select(m => m.Id));
        Assert.Equal("Claude Opus 5.5", models[0].Label);
        Assert.Empty(Ids("anthropic", ModelKind.Stt, body));
    }

    [Fact]
    public void GeminiOhnePraefixUndOhneNichtChatModelle()
    {
        var body = Data(
            """{"id":"models/gemini-3.8-flash","object":"model","owned_by":"google"}""",
            """{"id":"models/gemini-3.5-flash-lite","object":"model"}""",
            """{"id":"models/gemini-flash-latest","object":"model"}""",
            """{"id":"gemini-2.5-flash-lite","object":"model"}""",
            """{"id":"models/gemini-embedding-001"}""",
            """{"id":"models/gemini-3.8-flash-tts"}""",
            """{"id":"models/gemini-3.1-flash-image"}""",
            """{"id":"models/gemini-3.8-live"}""",
            """{"id":"models/gemini-2.5-flash-native-audio-preview-12-2025"}""",
            """{"id":"models/gemini-3.5-transcribe"}""",
            """{"id":"models/veo-3.1-generate-preview"}""",
            """{"id":"models/imagen-4.0-generate"}""",
            """{"id":"models/lyria-3.5"}""",
            """{"id":"models/gemini-robotics-er-2-preview"}""",
            """{"id":"models/gemini-2.5-computer-use-preview-10-2025"}""",
            """{"id":"models/deep-research-preview-04-2026"}""",
            """{"id":"models/antigravity-preview-09-2026"}""",
            """{"id":"models/gemini-omni-1.1-flash"}""",
            """{"id":"models/aqa"}""");
        Assert.Equal(["gemini-2.5-flash-lite", "gemini-3.5-flash-lite", "gemini-3.8-flash", "gemini-flash-latest"], Ids("gemini", ModelKind.Llm, body));
    }

    [Fact]
    public void DeepSeekAlleMitName()
    {
        var body = Data(
            """{"id":"deepseek-flash","object":"model","owned_by":"deepseek","name":"DeepSeek-V4.1-Flash","input_modalities":["text","image"],"output_modalities":["text"],"effort":{"supported_levels":["low","high","max"],"default_level":"high"}}""",
            """{"id":"deepseek-v4-pro","object":"model","owned_by":"deepseek"}""");
        var models = ModelLists.Parse(P("deepseek"), ModelKind.Llm, body, Today);
        Assert.Equal(["deepseek-flash", "deepseek-v4-pro"], models.Select(m => m.Id));
        Assert.Equal("DeepSeek-V4.1-Flash", models[0].Label);
        Assert.Null(models[1].Label);
    }

    [Fact]
    public void OllamaOhneEinbettungsModelle()
    {
        const string body = """{"models":[{"name":"gemma4:31b","model":"gemma4:31b","details":{"family":""}},{"name":"nomic-embed-text:latest"},{"name":"qwen3:8b"}]}""";
        Assert.Equal(["gemma4:31b", "qwen3:8b"], Ids("ollama", ModelKind.Llm, body));
        Assert.Equal(["gemma4:31b", "qwen3:8b"], Ids("ollama-cloud", ModelKind.Llm, body));
        Assert.Empty(Ids("ollama", ModelKind.Stt, body));
    }

    [Fact]
    public void EigenerServerMitTaskWieSpeaches()
    {
        var body = Data(
            """{"id":"Systran/faster-whisper-small","object":"model","owned_by":"Systran","language":["de","en"],"task":"automatic-speech-recognition"}""",
            """{"id":"speaches-ai/Kokoro-82M-v1.0-ONNX","object":"model","task":"text-to-speech"}""");
        Assert.Equal(["Systran/faster-whisper-small"], Ids("custom", ModelKind.Stt, body));
        Assert.Equal(2, Ids("custom", ModelKind.Llm, body).Count);
    }

    [Fact]
    public void EigenerServerOhneFelderNachNameUndLeerHeisstAlle()
    {
        var body = Data(
            """{"id":"Qwen/Qwen3-8B","object":"model","owned_by":"vllm","max_model_len":32768}""",
            """{"id":"whisper-large-v3-turbo","object":"model"}""",
            """{"id":"BAAI/bge-m3-embed","object":"model"}""");
        Assert.Equal(["whisper-large-v3-turbo"], Ids("custom", ModelKind.Stt, body));
        Assert.Equal(["Qwen/Qwen3-8B"], Ids("custom", ModelKind.Llm, body));
        Assert.Equal(["gemma4", "llama3.2"], Ids("custom", ModelKind.Stt, Data("""{"id":"llama3.2"}""", """{"id":"gemma4"}""")));
    }

    [Fact]
    public void ElevenLabsNurScribeOhneRealtime()
    {
        const string body = """
            [
              {"model_id":"eleven_multilingual_v2","name":"Eleven Multilingual v2","can_do_text_to_speech":true,"languages":[{"language_id":"de","name":"German"}]},
              {"model_id":"eleven_flash_v2_5","name":"Eleven Flash v2.5","can_do_text_to_speech":true},
              {"model_id":"scribe_v2","name":"Scribe v2"},
              {"model_id":"scribe_v2_realtime","name":"Scribe v2 Realtime"},
              {"model_id":"scribe_v2_medical","name":"Scribe v2 Medical"}
            ]
            """;
        var models = ModelLists.Parse(P("elevenlabs"), ModelKind.Stt, body, Today);
        Assert.Equal(["scribe_v2", "scribe_v2_medical"], models.Select(m => m.Id));
        Assert.Equal("Scribe v2", models[0].Label);
        Assert.Empty(Ids("elevenlabs", ModelKind.Stt, """[{"model_id":"eleven_v3","can_do_text_to_speech":true}]"""));
        Assert.Empty(Ids("elevenlabs", ModelKind.Llm, body));
    }

    [Fact]
    public void KaputteAntwortIstEinKlarerFehler()
    {
        (string Provider, string Body)[] cases =
        [
            ("openai", "<html>Bad Gateway</html>"), ("openai", """{"data":[{"id":"gpt-4o"""), ("openai", "{}"),
            ("groq", """{"data":"keine Liste"}"""), ("openrouter", ""), ("elevenlabs", """{"detail":"Not Found"}"""), ("ollama", "not json"),
        ];
        foreach (var (provider, body) in cases)
        {
            var kind = provider == "ollama" ? ModelKind.Llm : ModelKind.Stt;
            var e = Assert.Throws<ModelListException>(() => ModelLists.Parse(P(provider), kind, body, Today));
            Assert.Equal("Antwort des Servers ist keine Modell-Liste", e.Message);
        }
    }

    [Fact]
    public void UnbekannteFelderUndLeereEintraegeStoerenNicht()
    {
        var body = Data("""{"id":"whisper-1","neu":{"x":[1,2]},"shutdown_date":"kein Datum"}""", """{"id":""}""", """{"object":"model"}""", "42", """{"id":"whisper-1"}""");
        Assert.Equal(["whisper-1"], Ids("openai", ModelKind.Stt, body));
    }

    [Fact]
    public void DatumAusTextZeitstempelOderSekunden()
    {
        Assert.Equal(new DateOnly(2027, 2, 26), ModelLists.Date(JsonValue.Create("2027-02-26")));
        Assert.Equal(new DateOnly(2027, 1, 31), ModelLists.Date(JsonValue.Create("2027-01-31T00:00:00Z")));
        Assert.Equal(new DateOnly(2025, 7, 14), ModelLists.Date(JsonValue.Create(1752451200)));
        Assert.Null(ModelLists.Date(JsonValue.Create("bald")));
        Assert.Null(ModelLists.Date(null));
    }

    /// <summary>Desktop: Flags für Server-Modelle ohne exakten Katalog-Treffer (optionFor/catalogBase, in Kotlin über AccessResolverTest).</summary>
    [Fact]
    public void FlagsFuerSnapshotsUndUnbekannteModelle()
    {
        var gpt5 = ModelLists.OptionFor(P("openai"), ModelKind.Llm, "gpt-5-mini-2025-08-07", null)!;
        Assert.False(gpt5.TemperatureSupported);
        Assert.Equal("minimal", gpt5.ReasoningEffort);
        Assert.Equal("claude-haiku-4-5", ModelLists.CatalogBase(P("anthropic").LlmModels!, "claude-haiku-4-5-20251001")!.Id);
        Assert.Equal("low", ModelLists.OptionFor(P("openai"), ModelKind.Llm, "o9-neu", null)!.ReasoningEffort);
        Assert.Equal("none", ModelLists.OptionFor(P("groq"), ModelKind.Llm, "qwen3-32b", null)!.ReasoningEffort);
        Assert.Null(ModelLists.OptionFor(P("openai"), ModelKind.Llm, "irgendwas", null));
    }
}
