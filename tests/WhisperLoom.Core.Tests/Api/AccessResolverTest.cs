using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Tests.Support;
using static WhisperLoom.Core.Api.AccessResolver;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/AccessResolverTest.kt</c>: an welchen Server welcher Key geht.</summary>
public class AccessResolverTest
{
    private const string SttKey = Keys.K1;
    private readonly ApiAccess _stt = ResolveStt("openai", "", SttKey, "", 45);
    private readonly ApiAccess _openAiStt = ResolveStt("openai", "", Keys.K2, "", 0);

    [Fact]
    public void NeuerNutzerBekommtOpenAiDefaults()
    {
        var a = ResolveStt("openai", "", SttKey, "", 0);
        Assert.Equal("https://api.openai.com/v1", a.BaseUrl);
        Assert.Equal("gpt-transcribe", a.Model);
        Assert.Equal(90_000, a.ReadTimeoutMs);
        Assert.Equal("openai", a.Provider.Id);
        Assert.Equal("languages[]", a.ModelOption!.LanguageField);
    }

    [Fact]
    public void BestandsnutzerBehaeltAltesModellUndUrl()
    {
        var a = ResolveStt("", "https://api.openai.com/v1", SttKey, "gpt-4o-transcribe", 0);
        Assert.Equal("openai", a.Provider.Id);
        Assert.Equal("gpt-4o-transcribe", a.Model);
        Assert.Contains("Auslauf", a.ModelOption!.Label, StringComparison.Ordinal);
        Assert.Equal("language", a.ModelOption!.LanguageField);
    }

    [Fact]
    public void WerteWerdenGetrimmt()
    {
        var a = ResolveStt("groq", "  https://api.groq.com/openai/v1/ ", $" {Keys.K3} ", " whisper-large-v3 ", 0);
        Assert.Equal("https://api.groq.com/openai/v1/", a.BaseUrl);
        Assert.Equal(Keys.K3, a.ApiKey);
        Assert.Equal("whisper-large-v3", a.Model);
        Assert.NotNull(a.ModelOption);
    }

    [Fact]
    public void EigenerServerOhneUrlBleibtLeer()
    {
        var a = ResolveStt("custom", "", "", "", 0);
        Assert.Equal("", a.BaseUrl);
        Assert.Equal("", a.Model);
        Assert.Equal(600_000, a.ReadTimeoutMs);
        Assert.Null(a.ModelOption);
        Assert.False(a.Provider.NeedsKey);
    }

    [Fact]
    public void TimeoutAusDenEinstellungenGewinnt()
    {
        Assert.Equal(120_000, ResolveStt("custom", "http://x/v1", "", "m", 120).ReadTimeoutMs);
        Assert.Equal(45_000, ResolveStt("openai", "", SttKey, "", 45).ReadTimeoutMs);
    }

    [Fact]
    public void UnbekanntesModellHatKeineOption()
    {
        var a = ResolveStt("openai", "", SttKey, "mein-eigenes", 0);
        Assert.Equal("mein-eigenes", a.Model);
        Assert.Null(a.ModelOption);
    }

    [Fact]
    public void SameUebernimmtDenTranskriptionsZugang()
    {
        var l = ResolveLlm(_stt, "same", "", "", "");
        Assert.Equal(_stt.BaseUrl, l.BaseUrl);
        Assert.Equal(SttKey, l.ApiKey);
        Assert.Equal(45_000, l.ReadTimeoutMs);
        Assert.Equal("openai", l.Provider.Id);
        Assert.Equal("gpt-6-luna", l.Model);
        Assert.False(l.ModelOption!.TemperatureSupported);
    }

    [Fact]
    public void LeererProviderZaehltAlsSame()
    {
        var l = ResolveLlm(_stt, "", "", "", "gpt-5.6-luna");
        Assert.Equal(SttKey, l.ApiKey);
        Assert.Equal("gpt-5.6-luna", l.Model);
        Assert.False(l.ModelOption!.TemperatureSupported);
        Assert.Equal("none", l.ModelOption!.ReasoningEffort);
    }

    [Fact]
    public void SameIgnoriertStaleLlmFelder()
    {
        var l = ResolveLlm(_stt, "same", "http://alt/v1", Keys.K4, "");
        Assert.Equal(_stt.BaseUrl, l.BaseUrl);
        Assert.Equal(SttKey, l.ApiKey);
    }

    [Fact]
    public void AndererAnbieterBekommtNieDenSttKey()
    {
        var l = ResolveLlm(_stt, "groq", "", "", "");
        Assert.Equal("groq", l.Provider.Id);
        Assert.Equal("https://api.groq.com/openai/v1", l.BaseUrl);
        Assert.Equal("", l.ApiKey);
        Assert.Equal("openai/gpt-oss-20b", l.Model);
        Assert.Equal(90_000, l.ReadTimeoutMs);
    }

    [Fact]
    public void AndererAnbieterMitEigenenFeldern()
    {
        var l = ResolveLlm(_stt, "groq", " https://proxy/v1 ", $" {Keys.K3} ", " qwen/qwen3.8-27b ");
        Assert.Equal("https://proxy/v1", l.BaseUrl);
        Assert.Equal(Keys.K3, l.ApiKey);
        Assert.Equal("qwen/qwen3.8-27b", l.Model);
        Assert.Equal("none", l.ModelOption!.ReasoningEffort);
    }

    [Fact]
    public void GleicherAnbieterExplizitErbtLeereFelder()
    {
        var l = ResolveLlm(_stt, "openai", "", "", "gpt-4.1-mini");
        Assert.Equal(_stt.BaseUrl, l.BaseUrl);
        Assert.Equal(SttKey, l.ApiKey);
        Assert.Equal(45_000, l.ReadTimeoutMs);
        Assert.Equal("gpt-4.1-mini", l.Model);
    }

    [Fact]
    public void EigenerServerErbtUrlVomEigenenSttServer()
    {
        var sttCustom = ResolveStt("custom", "http://192.168.1.50:8000/v1", "", "whisper-1", 0);
        var l = ResolveLlm(sttCustom, "custom", "", "", "qwen3:8b");
        Assert.Equal("http://192.168.1.50:8000/v1", l.BaseUrl);
        Assert.Equal("", l.ApiKey);
        Assert.Equal(600_000, l.ReadTimeoutMs);
        Assert.Equal("qwen3:8b", l.Model);
        Assert.Null(l.ModelOption);
    }

    [Fact]
    public void OllamaCloudNutztDenKatalogUndDenEigenenKey()
    {
        var l = ResolveLlm(_stt, "ollama-cloud", "", Keys.K3, "");
        Assert.Equal("https://ollama.com", l.BaseUrl);
        Assert.Equal(Keys.K3, l.ApiKey);
        Assert.Equal("gemma4:31b", l.Model);
        Assert.Equal(90_000, l.ReadTimeoutMs);
        Assert.Equal("", ResolveLlm(_stt, "ollama-cloud", "", "", "").ApiKey);
    }

    [Fact]
    public void OllamaLokalOhneAdresseBleibtLeer()
    {
        var l = ResolveLlm(_stt, "ollama", "", "", "");
        Assert.Equal("", l.BaseUrl);
        Assert.Equal("", l.Model);
        var mitAdresse = ResolveLlm(_stt, "ollama", "http://192.168.1.10:11434", "", "gemma3");
        Assert.Equal("http://192.168.1.10:11434", mitAdresse.BaseUrl);
        Assert.Equal(600_000, mitAdresse.ReadTimeoutMs);
    }

    [Fact]
    public void OllamaNebenCloudStt()
    {
        var l = ResolveLlm(_stt, "custom", "http://192.168.1.50:11434/v1", "", "qwen3:8b");
        Assert.Equal("http://192.168.1.50:11434/v1", l.BaseUrl);
        Assert.Equal("", l.ApiKey);
        Assert.Equal(600_000, l.ReadTimeoutMs);
        Assert.True(l.Provider.IsCustom);
    }

    private string Anthropic(string model = "", string stageModel = "", RefineMode? stage = null) =>
        ResolveLlm(_stt, "anthropic", "", Keys.K3, model, stageModel: stageModel, stage: stage).Model;

    [Fact]
    public void OhneModellGiltDieEmpfehlungJeStufe()
    {
        foreach (var mode in new[] { RefineMode.Polish, RefineMode.Readable }) Assert.Equal("claude-haiku-5-5", Anthropic(stage: mode));
        foreach (var mode in new[] { RefineMode.Beautify, RefineMode.Summarize, RefineMode.Prompt }) Assert.Equal("claude-sonnet-5-5", Anthropic(stage: mode));
        Assert.Equal("claude-haiku-5-5", Anthropic(stage: null));
        Assert.Equal("claude-haiku-5-5", Anthropic(stage: RefineMode.Off));
    }

    [Fact]
    public void ReihenfolgeStufenModellDannZugangsModellDannEmpfehlung()
    {
        Assert.Equal("claude-opus-5-5", Anthropic("claude-sonnet-5", " claude-opus-5-5 ", RefineMode.Summarize));
        Assert.Equal("claude-sonnet-5", Anthropic("claude-sonnet-5", stage: RefineMode.Beautify));
        Assert.Equal("claude-sonnet-5", Anthropic("claude-sonnet-5", stage: RefineMode.Polish));
        Assert.Equal("claude-sonnet-5-5", Anthropic(stageModel: "  ", stage: RefineMode.Beautify));
    }

    [Fact]
    public void OhneUmformulierenEmpfehlungDasErsteKatalogmodell()
    {
        var p = new ProviderInfo("x", "X", "https://x", LlmModels: [new ModelOption("a", "A"), new ModelOption("b", "B")]);
        Assert.Equal("a", p.RecommendedLlmModel(RefineMode.Beautify));
        Assert.Equal("b", (p with { RewriteLlmModel = "b" }).RecommendedLlmModel(RefineMode.Prompt));
        Assert.Equal("a", (p with { RewriteLlmModel = "b" }).RecommendedLlmModel(RefineMode.Readable));
        Assert.Equal("", ProviderCatalog.Custom.RecommendedLlmModel(RefineMode.Beautify));
    }

    [Fact]
    public void WieErkennungNutztDieEmpfehlungDesErkennungsAnbieters()
    {
        var l = ResolveLlm(_stt, "same", "", "", "", stage: RefineMode.Beautify);
        Assert.Equal("gpt-6-sol", l.Model);
        Assert.Equal(SttKey, l.ApiKey);
        Assert.Equal("gpt-4.1-mini", ResolveLlm(_stt, "same", "", "", "", stageModel: "gpt-4.1-mini", stage: RefineMode.Beautify).Model);
        var offline = ResolveLlm(_stt, "same", "", "", "", sttOffline: true, stageModel: "gpt-6-sol", stage: RefineMode.Beautify);
        Assert.Equal(RefineBlock.Offline, offline.RefineBlock);
        Assert.Equal("", offline.Model);
    }

    [Fact]
    public void OllamaLokalOhneKatalogNimmtDasModellDesZugangs()
    {
        const string url = "http://192.168.1.10:11434";
        Assert.Equal("gemma4:12b", ResolveLlm(_stt, "ollama", url, "", "gemma4:12b", stage: RefineMode.Beautify).Model);
        Assert.Equal("gemma4:26b", ResolveLlm(_stt, "ollama", url, "", "gemma4:12b", stageModel: "gemma4:26b", stage: RefineMode.Beautify).Model);
        Assert.Equal("", ResolveLlm(_stt, "ollama", url, "", "", stage: RefineMode.Beautify).Model);
    }

    [Fact]
    public void StufenModellZaehltNurMitModellDesZugangs()
    {
        var together = ResolveStt("together", "", Keys.K3, "", 0);
        var same = ResolveLlm(together, "same", "", "", "", stageModel: "Y", stage: RefineMode.Beautify);
        Assert.Equal(RefineBlock.NoModel, same.RefineBlock);
        Assert.False(SetupState.LlmReady(same));
        foreach (var id in new[] { "ollama", "custom" })
        {
            var l = ResolveLlm(_stt, id, "http://192.168.1.10:11434", "", "", stageModel: "gemma4:26b", stage: RefineMode.Beautify);
            Assert.Equal("", l.Model);
            Assert.False(SetupState.LlmReady(l));
        }
    }

    private sealed class FakeLookup(Func<string, RemoteModel?> model) : IServerModelLookup
    {
        public List<string> Asked { get; } = [];

        public RemoteModel? Find(string providerId, ModelKind kind, string baseUrl, string id)
        {
            Asked.Add($"{providerId}|{kind.Key()}|{baseUrl}|{id}");
            return model(id);
        }
    }

    [Fact]
    public void GespeichertesServerModellBekommtFlagsAusDemCache()
    {
        var lookup = new FakeLookup(id => new RemoteModel(id, Label: "OpenAI: GPT-6.1 Sol", TemperatureSupported: false));
        var l = ResolveLlm(_stt, "openrouter", "", Keys.K3, "openai/gpt-6.1-sol", lookup);
        Assert.Equal(["openrouter|llm|https://openrouter.ai/api/v1|openai/gpt-6.1-sol"], lookup.Asked);
        Assert.False(l.ModelOption!.TemperatureSupported);
        Assert.Equal("OpenAI: GPT-6.1 Sol", l.ModelOption!.Label);
        Assert.Null(ChatPayload.SamplingFor(l).Temperature);
    }

    [Fact]
    public void SameFragtDenCacheMitDemErkennungsZugang()
    {
        var lookup = new FakeLookup(_ => null);
        ResolveLlm(_stt, "same", "", "", "gpt-6-astra", lookup);
        Assert.Equal(["openai|llm|https://api.openai.com/v1|gpt-6-astra"], lookup.Asked);
    }

    [Fact]
    public void KatalogTrefferFragtDenCacheNicht()
    {
        var lookup = new FakeLookup(_ => throw new InvalidOperationException("darf nicht gefragt werden"));
        ResolveStt("openai", "", SttKey, "gpt-transcribe", 0, lookup);
        ResolveLlm(_stt, "same", "", "", "gpt-4o-mini", lookup);
        ResolveStt("custom", "http://h/v1", "", "", 0, lookup);
        Assert.Empty(lookup.Asked);
    }

    private sealed class RejectingLookup(params string[] rejected) : IServerModelLookup
    {
        public RemoteModel? Find(string providerId, ModelKind kind, string baseUrl, string id) => null;

        public bool RejectsTemperature(string providerId, string baseUrl, string id) => rejected.Contains($"{providerId}|{baseUrl}|{id}");
    }

    [Fact]
    public void AbgelehnteTemperatureGiltAuchFuerKatalogModelle()
    {
        var lookup = new RejectingLookup("openai|https://api.openai.com/v1|gpt-4o-mini", "openai|https://api.openai.com/v1|gpt-neu");
        var katalog = ResolveLlm(_stt, "same", "", "", "gpt-4o-mini", lookup);
        Assert.False(katalog.ModelOption!.TemperatureSupported);
        Assert.Equal("GPT-4o mini", katalog.ModelOption!.Label);
        Assert.Null(ChatPayload.SamplingFor(katalog).Temperature);
        Assert.Equal(new ModelOption("gpt-neu", "gpt-neu", TemperatureSupported: false), ResolveLlm(_stt, "same", "", "", "gpt-neu", lookup).ModelOption);
        Assert.True(ResolveLlm(_stt, "same", "", "", "gpt-4.1-mini", lookup).ModelOption!.TemperatureSupported);
        Assert.Null(ResolveStt("openai", "", SttKey, "gpt-neu", 0, lookup).ModelOption);
    }

    [Fact]
    public void ErkennungsModellVomServerMitHeuristik()
    {
        var lookup = new FakeLookup(id => new RemoteModel(id));
        var a = ResolveStt("openai", "", SttKey, "gpt-transcribe-2026-08-01", 0, lookup);
        Assert.Equal(["openai|stt|https://api.openai.com/v1|gpt-transcribe-2026-08-01"], lookup.Asked);
        Assert.Equal("languages[]", a.ModelOption!.LanguageField);
        Assert.Equal("languages[]", ResolveStt("openai", "", SttKey, "gpt-transcribe-2026-08-01").ModelOption!.LanguageField);
    }

    [Fact]
    public void ElevenLabsWieErkennungKannKeinenTextVerbessernAuchMitAltemModell()
    {
        var stt = ResolveStt("elevenlabs", "", SttKey, "", 0);
        Assert.Equal(RefineBlock.NoChat, ResolveLlm(stt, "same", "", "", "").RefineBlock);
        Assert.Equal(RefineBlock.NoChat, ResolveLlm(stt, "same", "", "", "gpt-4o-mini").RefineBlock);
    }

    [Fact]
    public void TogetherUndDeepInfraWieErkennungBrauchenNurEinModell()
    {
        foreach (var id in new[] { "together", "deepinfra" })
        {
            var stt = ResolveStt(id, "", SttKey, "", 0);
            Assert.Equal(RefineBlock.NoModel, ResolveLlm(stt, "same", "", "", " ").RefineBlock);
            Assert.Null(ResolveLlm(stt, "same", "", "", "meta-llama/Llama-3.3-70B-Instruct-Turbo").RefineBlock);
        }
    }

    [Fact]
    public void AnbieterMitTextmodellenSindNieGesperrt()
    {
        var stt = ResolveStt("groq", "", SttKey, "", 0);
        Assert.Null(ResolveLlm(stt, "same", "", "", "").RefineBlock);
        Assert.Null(ResolveLlm(stt, "custom", "http://h:1/v1", "", "").RefineBlock);
        Assert.Null(ResolveLlm(stt, "ollama", "http://h:11434", "", "").RefineBlock);
    }

    [Fact]
    public async Task WieErkennungBeiOfflineErkennungSchicktNichtsAnDenAltenZugang()
    {
        var stt = ResolveStt("openai", "", SttKey, "", 0);
        var llm = ResolveLlm(stt, "same", "", "", "gpt-4o-mini", sttOffline: true);
        Assert.Equal(RefineBlock.Offline, llm.RefineBlock);
        Assert.Equal("", llm.BaseUrl);
        Assert.Equal("", llm.ApiKey);
        using var http = Http.CreateClient();
        var refiner = new TextRefiner(llm, http, new FakePromptBuilder(), new TextCleanup([]));
        var e = await Assert.ThrowsAsync<ApiNotConfiguredException>(() =>
            refiner.RefineAsync("hallo welt", "de", new Refinement(RefineMode.Polish), CancellationToken.None));
        Assert.Equal(TextRefiner.MsgOffline, e.Message);
        Assert.Null(ResolveLlm(stt, "same", "", "", "gpt-4o-mini").RefineBlock);
    }

    [Fact]
    public void EigenerTextZugangGiltAuchBeiOfflineErkennung()
    {
        var stt = ResolveStt("openai", "", SttKey, "", 0);
        var llm = ResolveLlm(stt, "groq", "", Keys.K3, "", sttOffline: true);
        Assert.Null(llm.RefineBlock);
        Assert.Equal("groq", llm.Provider.Id);
        Assert.Equal(Keys.K3, llm.ApiKey);
        Assert.Equal("https://api.groq.com/openai/v1", llm.BaseUrl);
    }

    [Fact]
    public void SttFallbackAnderenAnbietersBekommtNieDenKeyDesErsten()
    {
        var fb = ResolveSttFallback(_openAiStt, "groq", "", "", "");
        Assert.Equal("groq", fb.Provider.Id);
        Assert.Equal("https://api.groq.com/openai/v1", fb.BaseUrl);
        Assert.Equal("", fb.ApiKey);
        Assert.Equal(fb.Provider.DefaultSttModel, fb.Model);
        Assert.False(SetupState.SttFallbackReady(fb));
        Assert.True(SetupState.SttFallbackReady(ResolveSttFallback(_openAiStt, "groq", "", Keys.K3, "")));
    }

    [Fact]
    public void SttFallbackDesselbenAnbietersNimmtDenKeyDesErsten()
    {
        var fb = ResolveSttFallback(_openAiStt, "openai", "", "", "whisper-1");
        Assert.Equal(Keys.K2, fb.ApiKey);
        Assert.Equal("whisper-1", fb.Model);
        Assert.Equal(Keys.K4, ResolveSttFallback(_openAiStt, "openai", "", Keys.K4, "").ApiKey);
    }

    [Fact]
    public void SttFallbackEigenerServerBrauchtAdresseUndModell()
    {
        var ohne = ResolveSttFallback(_openAiStt, "custom", "", "", "");
        Assert.Equal("", ohne.BaseUrl);
        Assert.False(SetupState.SttFallbackReady(ohne));
        Assert.False(SetupState.SttFallbackReady(ResolveSttFallback(_openAiStt, "custom", "http://192.168.1.5:8000/v1", "", "")));
        var bereit = ResolveSttFallback(_openAiStt, "custom", "http://192.168.1.5:8000/v1", "", "whisper-1");
        Assert.True(SetupState.SttFallbackReady(bereit));
        Assert.Equal(600_000, bereit.ReadTimeoutMs);
    }

    [Fact]
    public void LlmFallbackNimmtDenKeyDesKiZugangsDesselbenAnbieters()
    {
        var primary = ResolveLlm(_openAiStt, "anthropic", "", Keys.K3, "");
        var fb = ResolveLlmFallback(_openAiStt, primary, "anthropic", "", "", "claude-haiku-5-5");
        Assert.Equal(Keys.K3, fb.ApiKey);
        Assert.Equal(primary.BaseUrl, fb.BaseUrl);
    }

    [Fact]
    public void LlmFallbackNimmtDenKeyDerErkennungDesselbenAnbieters()
    {
        var primary = ResolveLlm(_openAiStt, "anthropic", "", Keys.K3, "");
        var fb = ResolveLlmFallback(_openAiStt, primary, "openai", "", "", "");
        Assert.Equal(Keys.K2, fb.ApiKey);
        Assert.Equal("https://api.openai.com/v1", fb.BaseUrl);
        Assert.True(SetupState.LlmReady(fb));
    }

    [Fact]
    public void LlmFallbackEinesFremdenAnbietersBleibtOhneKey()
    {
        var primary = ResolveLlm(_openAiStt, "anthropic", "", Keys.K3, "");
        var fb = ResolveLlmFallback(_openAiStt, primary, "groq", "", "", "");
        Assert.Equal("", fb.ApiKey);
        Assert.False(SetupState.LlmReady(fb));
    }

    [Fact]
    public void LlmFallbackOfflineNimmtNieDenAbgewaehltenKeyDerErkennung()
    {
        var primary = ResolveLlm(_openAiStt, "anthropic", "", Keys.K3, "", sttOffline: true);
        Assert.Equal("", ResolveLlmFallback(_openAiStt, primary, "openai", "", "", "", sttOffline: true).ApiKey);
        var same = ResolveLlm(_openAiStt, LlmSame, "", "", "", sttOffline: true);
        Assert.Equal("", ResolveLlmFallback(_openAiStt, same, "openai", "", "", "", sttOffline: true).ApiKey);
    }

    [Fact]
    public void LlmFallbackOhneModellNimmtDieEmpfehlungJeStufe()
    {
        var primary = ResolveLlm(_openAiStt, "anthropic", "", Keys.K3, "", stageModel: "claude-opus-5-5", stage: RefineMode.Polish);
        var polish = ResolveLlmFallback(_openAiStt, primary, "anthropic", "", "", "", stage: RefineMode.Polish);
        var beautify = ResolveLlmFallback(_openAiStt, primary, "anthropic", "", "", "", stage: RefineMode.Beautify);
        Assert.Equal(polish.Provider.RecommendedLlmModel(RefineMode.Polish), polish.Model);
        Assert.Equal(beautify.Provider.RecommendedLlmModel(RefineMode.Beautify), beautify.Model);
        Assert.Equal("gpt-6", ResolveLlmFallback(_openAiStt, primary, "openai", "", "", " gpt-6 ", stage: RefineMode.Prompt).Model);
    }

    [Fact]
    public void LlmFallbackOllamaLokalBrauchtAdresseUndModell()
    {
        var primary = ResolveLlm(_openAiStt, LlmSame, "", "", "");
        var ohne = ResolveLlmFallback(_openAiStt, primary, "ollama", "", "", "");
        Assert.Equal("", ohne.BaseUrl);
        Assert.Equal("", ohne.Model);
        Assert.False(SetupState.LlmReady(ohne));
        var bereit = ResolveLlmFallback(_openAiStt, primary, "ollama", "http://192.168.1.5:11434", "", "qwen3:8b");
        Assert.True(SetupState.LlmReady(bereit));
        Assert.Equal(600_000, bereit.ReadTimeoutMs);
    }

    [Fact]
    public void SttFallbackEigenerServerMitAndererAdresseErbtKeinenKey()
    {
        var primary = ResolveStt("custom", "https://a.example/v1", Keys.K3, "whisper-1", 0);
        var andere = ResolveSttFallback(primary, "custom", "https://b.example/v1", "", "whisper-1");
        Assert.Equal("https://b.example/v1", andere.BaseUrl);
        Assert.Equal("", andere.ApiKey);
        Assert.Equal(Keys.K3, ResolveSttFallback(primary, "custom", "https://A.example/v1/", "", "m").ApiKey);
        Assert.Equal(Keys.K3, ResolveSttFallback(primary, "custom", "", "", "m").ApiKey);
    }

    [Fact]
    public void LlmFallbackOllamaMitAndererAdresseErbtKeinenKey()
    {
        var primary = ResolveLlm(_openAiStt, "ollama", "http://192.168.1.5:11434", Keys.K3, "qwen3:8b");
        Assert.Equal("", ResolveLlmFallback(_openAiStt, primary, "ollama", "http://192.168.1.9:11434", "", "gemma4:12b").ApiKey);
        Assert.Equal(Keys.K3, ResolveLlmFallback(_openAiStt, primary, "ollama", "http://192.168.1.5:11434", "", "gemma4:12b").ApiKey);
    }

    /// <summary>Desktop: ToString eines Zugangs zeigt nie den Key (Vertrag „Datenschutz“).</summary>
    [Fact]
    public void ToStringZeigtKeinenKey() => Assert.DoesNotContain(SttKey, _stt.ToString(), StringComparison.Ordinal);
}
