using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/ProviderCatalogTest.kt</c>: der Katalog (hier aus providers.json) ist reine Daten.</summary>
public class ProviderCatalogTest
{
    private static ProviderInfo P(string id) => ProviderCatalog.ById(id);

    private static IReadOnlyList<ModelOption> Llm(string id) => P(id).LlmModels ?? [];

    private static IReadOnlyList<ModelOption> AllLlm => [.. ProviderCatalog.Providers.SelectMany(p => p.LlmModels ?? [])];

    [Fact]
    public void IdsSindEindeutig()
    {
        var ids = ProviderCatalog.Providers.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ModellIdsSindJeAnbieterEindeutig()
    {
        foreach (var p in ProviderCatalog.Providers)
        {
            var stt = (p.SttModels ?? []).Select(m => m.Id).ToList();
            var llm = (p.LlmModels ?? []).Select(m => m.Id).ToList();
            Assert.Equal(stt.Count, stt.Distinct().Count());
            Assert.Equal(llm.Count, llm.Distinct().Count());
        }
    }

    [Fact]
    public void CloudAnbieterNutzenHttpsUndVerlangenEinenKey()
    {
        foreach (var p in ProviderCatalog.Providers.Where(p => !p.NeedsUrl))
        {
            Assert.StartsWith("https://", p.BaseUrl, StringComparison.Ordinal);
            Assert.True(p.NeedsKey, p.Id);
            Assert.StartsWith("https://", p.KeyUrl, StringComparison.Ordinal);
            Assert.False(p.AllowsHttp, p.Id);
            Assert.Equal(90, p.DefaultReadTimeoutSec);
            if (p.SttPathOverride is { } path) Assert.StartsWith("https://", path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EigenerServerIstOffenKonfigurierbar()
    {
        var c = ProviderCatalog.Custom;
        Assert.Equal("", c.BaseUrl);
        Assert.False(c.NeedsKey);
        Assert.True(c.AllowsHttp);
        Assert.Equal(600, c.DefaultReadTimeoutSec);
        Assert.True(c.HasStt);
        Assert.True(c.HasLlm);
        Assert.Equal("", c.DefaultSttModel);
    }

    /// <summary>Kotlin: Prefs.DEFAULT_API_URL/-MODEL/DEFAULT_LLM_MODEL; Desktop: Standard-Anbieter der Einstellungen.</summary>
    [Fact]
    public void DefaultsExistierenImKatalog()
    {
        var openai = ProviderCatalog.OpenAi;
        Assert.Equal(new WhisperLoomSettings().Stt.ProviderId, openai.Id);
        Assert.Equal("https://api.openai.com/v1", openai.BaseUrl);
        Assert.Equal("gpt-transcribe", openai.DefaultSttModel);
        Assert.Equal("gpt-6-luna", openai.DefaultLlmModel);
        Assert.NotNull(openai.SttModel("gpt-transcribe"));
        Assert.NotNull(openai.LlmModel("gpt-6-luna"));
    }

    [Fact]
    public void GroqDefaultsSindDieGratisModelle()
    {
        Assert.Equal("whisper-large-v3-turbo", P("groq").DefaultSttModel);
        Assert.Equal("openai/gpt-oss-20b", P("groq").DefaultLlmModel);
    }

    [Fact]
    public void GroqQwenIstDer38erNachfolger()
    {
        var groq = P("groq");
        Assert.Null(groq.LlmModel("qwen/qwen3.6-27b"));
        Assert.Equal("none", groq.LlmModel("qwen/qwen3.8-27b")!.ReasoningEffort);
        Assert.Equal("Qwen 3.8 27B (Preview)", groq.LlmModel("qwen/qwen3.8-27b")!.Label);
    }

    [Fact]
    public void GptTranscribeSendetLanguagesArray()
    {
        Assert.Equal("languages[]", ProviderCatalog.OpenAi.SttModel("gpt-transcribe")!.LanguageField);
        Assert.Equal("language", ProviderCatalog.OpenAi.SttModel("gpt-4o-transcribe")!.LanguageField);
    }

    [Fact]
    public void AltesDefaultModellIstAlsAuslaufGekennzeichnet() =>
        Assert.Contains("Auslauf", ProviderCatalog.OpenAi.SttModel("gpt-4o-transcribe")!.Label, StringComparison.Ordinal);

    [Fact]
    public void ReasoningModelleOhneTemperatureHabenEinenEffort()
    {
        foreach (var p in ProviderCatalog.Providers)
        {
            foreach (var m in (p.LlmModels ?? []).Where(m => !m.TemperatureSupported && !m.Id.Contains("gemini-3", StringComparison.Ordinal) && !m.Id.Contains("claude-", StringComparison.Ordinal)))
            {
                Assert.NotNull(m.ReasoningEffort);
            }
        }
    }

    [Fact]
    public void AnbieterFlagsAusDerRecherche()
    {
        Assert.False(P("mistral").SttSendsPrompt);
        Assert.False(P("mistral").SttSendsResponseFormat);
        Assert.False(P("openrouter").SttSendsPrompt);
        Assert.True(P("openrouter").SttSendsResponseFormat);
        Assert.Equal("https://api.deepinfra.com/v1/audio/transcriptions", P("deepinfra").SttPathOverride);
        Assert.Null(ProviderCatalog.OpenAi.SttPathOverride);
    }

    [Fact]
    public void AnzeigeReihenfolgeDerDropdowns()
    {
        Assert.Equal(
            ["openai", "groq", "mistral", "elevenlabs", "together", "deepinfra", "openrouter", "custom"],
            ProviderCatalog.SttProviders.Select(p => p.Id));
        Assert.Equal(
            ["openai", "groq", "mistral", "openrouter", "anthropic", "gemini", "deepseek", "ollama", "ollama-cloud", "custom"],
            ProviderCatalog.LlmProviders.Select(p => p.Id));
    }

    [Fact]
    public void OllamaLokalBrauchtAdresseAberKeinenKey()
    {
        var o = P(ProviderCatalog.OllamaId);
        Assert.True(o.IsOllama);
        Assert.True(o.NeedsUrl);
        Assert.False(o.NeedsKey);
        Assert.True(o.AllowsHttp);
        Assert.Equal(600, o.DefaultReadTimeoutSec);
        Assert.True(o.HasLlm);
        Assert.False(o.HasStt);
        Assert.Equal("", o.DefaultLlmModel);
    }

    [Fact]
    public void OllamaCloudMitKeyUndEmpfohlenemModell()
    {
        var c = P(ProviderCatalog.OllamaCloudId);
        Assert.True(c.IsOllama);
        Assert.Equal("https://ollama.com", c.BaseUrl);
        Assert.True(c.NeedsKey);
        Assert.Equal("https://ollama.com/settings/keys", c.KeyUrl);
        Assert.False(c.HasStt);
        Assert.Equal("gemma4:31b", c.DefaultLlmModel);
    }

    [Fact]
    public void NurOllamaSprichtDieNativeApi() =>
        Assert.Equal(["ollama", "ollama-cloud"], ProviderCatalog.Providers.Where(p => p.Api == ApiStyle.Ollama).Select(p => p.Id));

    [Fact]
    public void ElevenLabsIstNurErkennungMitScribe()
    {
        var e = P(ProviderCatalog.ElevenLabsId);
        Assert.Equal(ApiStyle.ElevenLabs, e.Api);
        Assert.Equal("ElevenLabs (Scribe)", e.Name);
        Assert.Equal("https://api.elevenlabs.io/v1", e.BaseUrl);
        Assert.Equal("https://elevenlabs.io/app/settings/api-keys", e.KeyUrl);
        Assert.True(e.HasStt);
        Assert.False(e.HasLlm);
        Assert.True(e.SttSendsPrompt);
        Assert.Equal(["scribe_v2", "scribe_v2_medical"], (e.SttModels ?? []).Select(m => m.Id));
        Assert.Equal("scribe_v2", e.DefaultSttModel);
        Assert.Contains("$0.22/h", e.SttModel("scribe_v2")!.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void KatalogStand20260930()
    {
        Assert.Equal("deepseek-flash", P("deepseek").DefaultLlmModel);
        Assert.Contains("alter Name", P("deepseek").LlmModel("deepseek-v4-flash")!.Label, StringComparison.Ordinal);
        var gemini = P("gemini");
        Assert.Equal(["gemini-3.5-flash-lite", "gemini-3.8-flash", "gemini-2.5-flash-lite", "gemini-2.5-flash"], Llm("gemini").Select(m => m.Id));
        Assert.Null(gemini.LlmModel("gemini-3.5-flash-lite")!.ReasoningEffort);
        var gemini3 = Llm("gemini").Concat(Llm("openrouter")).Where(m => m.Id.Contains("gemini-3", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, gemini3.Count);
        Assert.DoesNotContain(gemini3, m => m.TemperatureSupported);
        Assert.True(gemini.LlmModel("gemini-2.5-flash-lite")!.TemperatureSupported);
        var openrouter = Llm("openrouter").Select(m => m.Id).ToList();
        Assert.DoesNotContain(openrouter, id => id.StartsWith("google/gemini-2.5-", StringComparison.Ordinal));
        Assert.Contains("google/gemini-3.8-flash", openrouter);
    }

    [Fact]
    public void KatalogStand20261008() => Assert.Equal("2026-10-08", ProviderCatalog.CatalogDate);

    [Fact]
    public void EmpfehlungenZumGlaettenUndUmformulieren()
    {
        var expected = new Dictionary<string, (string Polish, string Rewrite)>
        {
            ["openai"] = ("gpt-6-luna", "gpt-6-sol"),
            ["anthropic"] = ("claude-haiku-5-5", "claude-sonnet-5-5"),
            ["groq"] = ("openai/gpt-oss-20b", "openai/gpt-oss-120b"),
            ["mistral"] = ("mistral-small-latest", "mistral-large-2512"),
            ["gemini"] = ("gemini-3.5-flash-lite", "gemini-3.8-flash"),
            ["deepseek"] = ("deepseek-flash", "deepseek-v4-pro"),
            ["openrouter"] = ("anthropic/claude-haiku-5.5", "anthropic/claude-sonnet-5.5"),
            ["ollama-cloud"] = ("gemma4:31b", "mistral-large-3:675b"),
        };
        foreach (var (id, models) in expected)
        {
            Assert.Equal(models.Polish, P(id).DefaultLlmModel);
            Assert.Equal(models.Rewrite, P(id).RewriteLlmModel);
        }
        Assert.Equal("", P("ollama").RewriteLlmModel);
        Assert.Equal("", ProviderCatalog.Custom.RewriteLlmModel);
    }

    [Fact]
    public void UmformulierenEmpfehlungStehtImKatalog()
    {
        foreach (var p in ProviderCatalog.Providers.Where(p => p.RewriteLlmModel.Length > 0))
        {
            Assert.NotNull(p.LlmModel(p.RewriteLlmModel));
            Assert.NotEqual(p.DefaultLlmModel, p.RewriteLlmModel);
        }
    }

    [Fact]
    public void Claude5OhneTemperature()
    {
        var claude = Llm("anthropic").Concat(Llm("openrouter"))
            .Where(m => m.Id.Contains("claude-", StringComparison.Ordinal) && !m.Id.Contains("-4", StringComparison.Ordinal)).ToList();
        Assert.Equal(6, claude.Count);
        Assert.DoesNotContain(claude, m => m.TemperatureSupported);
        Assert.DoesNotContain(claude, m => m.ReasoningEffort is not null);
        Assert.True(P("anthropic").LlmModel("claude-haiku-4-5")!.TemperatureSupported);
    }

    [Fact]
    public void ClaudeDenkenNurWoDasModellEsZulaesst()
    {
        var anthropic = P("anthropic");
        Assert.Equal("disabled", anthropic.LlmModel("claude-haiku-5-5")!.ThinkingType);
        Assert.Equal("disabled", anthropic.LlmModel("claude-sonnet-5")!.ThinkingType);
        Assert.Equal("between_tools", anthropic.LlmModel("claude-sonnet-5-5")!.ThinkingType);
        Assert.Null(anthropic.LlmModel("claude-opus-5-5")!.ThinkingType);
        Assert.DoesNotContain(Llm("openrouter"), m => m.ThinkingType is not null);
    }

    [Fact]
    public void DeepSeekDenktNichtUndGeminiFlashNurKurz()
    {
        Assert.All(Llm("deepseek"), m => Assert.Equal("disabled", m.ThinkingType));
        Assert.Equal("low", P("gemini").LlmModel("gemini-3.8-flash")!.ReasoningEffort);
        Assert.Equal("low", P("openrouter").LlmModel("google/gemini-3.8-flash")!.ReasoningEffort);
        var others = ProviderCatalog.Providers.Where(p => p.Id is not "anthropic" and not "deepseek");
        Assert.DoesNotContain(others.SelectMany(p => p.LlmModels ?? []), m => m.ThinkingType is not null);
    }

    [Fact]
    public void AlteStandardmodelleBleibenWaehlbar()
    {
        Assert.NotNull(ProviderCatalog.OpenAi.LlmModel("gpt-4o-mini"));
        Assert.Contains("2027-04-01", ProviderCatalog.OpenAi.LlmModel("gpt-5.4-nano")!.Note, StringComparison.Ordinal);
        Assert.Contains("Legacy", P("anthropic").LlmModel("claude-haiku-4-5")!.Label, StringComparison.Ordinal);
        Assert.Contains("Legacy", P("openrouter").LlmModel("anthropic/claude-haiku-4.5")!.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void GespeicherteModellIdsBleibenUnveraendert()
    {
        var stt = AccessResolver.ResolveStt("groq", "", Keys.K1, "");
        Assert.Equal("gemini-2.5-flash-lite", AccessResolver.ResolveLlm(stt, "gemini", "", Keys.K2, "gemini-2.5-flash-lite").Model);
        Assert.Equal("deepseek-v4-flash", AccessResolver.ResolveLlm(stt, "deepseek", "", Keys.K2, "deepseek-v4-flash").Model);
        Assert.Equal("google/gemini-2.5-flash-lite", AccessResolver.ResolveLlm(stt, "openrouter", "", Keys.K2, "google/gemini-2.5-flash-lite").Model);
        Assert.Equal("gemini-3.5-flash-lite", AccessResolver.ResolveLlm(stt, "gemini", "", Keys.K2, "").Model);
        Assert.Equal("deepseek-flash", AccessResolver.ResolveLlm(stt, "deepseek", "", Keys.K2, "").Model);
    }

    [Fact]
    public void UnbekannteIdFaelltAufOpenAiZurueck()
    {
        Assert.Equal("openai", ProviderCatalog.ById("").Id);
        Assert.Equal("openai", ProviderCatalog.ById("gibtsnicht").Id);
        Assert.Null(ProviderCatalog.Find("gibtsnicht"));
        Assert.Equal("groq", ProviderCatalog.ById("groq").Id);
    }

    /// <summary>Desktop: Werte des Katalogs kommen aus der eingebetteten providers.json (Parität: check_provider_parity.py).</summary>
    [Fact]
    public void KatalogKommtAusDerEingebettetenRessource()
    {
        Assert.Equal(13, ProviderCatalog.Providers.Count);
        Assert.Equal(26_214_400, ProviderCatalog.OpenAi.SttMaxBytes);
        Assert.Equal(83_886_080, P("together").SttMaxBytes);
        Assert.Null(P("mistral").SttMaxBytes);
    }
}
