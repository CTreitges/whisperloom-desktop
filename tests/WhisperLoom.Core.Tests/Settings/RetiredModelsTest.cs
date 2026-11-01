using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>
/// TD-A4 (Port von <c>PrefsTest.v8*</c>): abgeschaltete OpenAI-Modelle gehen beim Laden auf den Nachfolger — in jeder
/// Datei, die <see cref="SyncDocument.Parse"/> liest (Import, Provisioning, Firmen-Server), idempotent; Fest-Werte mit Warnung.
/// </summary>
public class RetiredModelsTest
{
    private static WhisperLoomSettings Load(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        RetiredModels.ApplyFull(root);
        return SettingsJson.Deserialize(root.ToJsonString());
    }

    [Fact]
    public void V8StelltAbgeschalteteOpenAiModelleAufDenNachfolgerUm()
    {
        var s = Load("""
            {"stt":{"providerId":"openai","model":"whisper-1"},
             "llm":{"providerId":"openai","stageModels":{"polish":"gpt-5-nano","beautify":"gpt-5-mini-2025-08-07","summarize":"gpt-6-sol"}},
             "sttFallback":{"model":"gpt-4o-mini-transcribe"},
             "llmFallback":{"model":"gpt-5-mini"}}
            """);
        Assert.Equal("gpt-transcribe", s.Stt.Model);
        Assert.Equal("gpt-5.6-luna", s.Llm.StageModels["polish"]);
        Assert.Equal("gpt-5.6-terra", s.Llm.StageModels["beautify"]);
        Assert.Equal("gpt-6-sol", s.Llm.StageModels["summarize"]); // nicht abgeschaltet: bleibt
        Assert.False(s.Llm.StageModels.ContainsKey("prompt"));
        Assert.Equal("gpt-transcribe", s.SttFallback.Model); // Fallback ohne Anbieter = OpenAI
        Assert.Equal("gpt-5.6-terra", s.LlmFallback.Model);
        // Der Nachfolger steht im Katalog: kein temperature, sonst lehnt OpenAI ab.
        Assert.False(ProviderCatalog.OpenAi.LlmModel(s.Llm.StageModels["beautify"])!.TemperatureSupported);
    }

    [Fact]
    public void V8LaesstAndereAnbieterUndEigeneServerInRuhe()
    {
        // Ein eigener Server darf sein Modell "whisper-1" nennen; jeder Fallback zählt mit seinem Anbieter.
        var s = Load("""
            {"stt":{"providerId":"custom","baseUrl":"http://192.168.1.5:8000/v1","model":"whisper-1"},
             "llm":{"providerId":"same","stageModels":{"polish":"gpt-5-mini"}},
             "sttFallback":{"providerId":"groq","model":"whisper-1"},
             "llmFallback":{"providerId":"openrouter","model":"gpt-5-nano"}}
            """);
        Assert.Equal("whisper-1", s.Stt.Model);
        Assert.Equal("gpt-5-mini", s.Llm.StageModels["polish"]); // „wie Erkennung“ = eigener Server
        Assert.Equal("whisper-1", s.SttFallback.Model);
        Assert.Equal("gpt-5-nano", s.LlmFallback.Model);
    }

    [Fact]
    public void V8NimmtFuerWieErkennungDenAnbieterDerErkennungUndAuchLlmModel()
    {
        // Desktop-Gegenstück zu v8VonV6…: das alte llm.model und die Stufen bekommen den Nachfolger.
        var s = Load("""{"llm":{"model":"gpt-5-nano","stageModels":{"polish":"gpt-5-nano"}}}""");
        Assert.Equal("gpt-5.6-luna", s.Llm.Model);
        Assert.Equal("gpt-5.6-luna", s.Llm.StageModels["polish"]);
    }

    [Fact]
    public void ZweiterLaufAendertNichtsUndMeldetNurGeaenderte()
    {
        var root = JsonNode.Parse("""{"stt":{"model":"gpt-4o-transcribe"},"llm":{"stageModels":{"polish":"gpt-6-luna"}}}""")!.AsObject();
        Assert.Equal(["stt.model"], RetiredModels.ApplyFull(root));
        var after = root.ToJsonString();
        Assert.Empty(RetiredModels.ApplyFull(root));
        Assert.Equal(after, root.ToJsonString());
        Assert.Empty(RetiredModels.ApplyFull(new JsonObject()));
    }

    /// <summary>Prüfrunde 1, R1: eine Teil-Datei ohne providerId gilt nicht als OpenAI — der Anbieter des Geräts zählt.</summary>
    [Theory]
    [InlineData("custom", "whisper-1")]
    [InlineData("groq", "whisper-1")]
    [InlineData("openai", "gpt-transcribe")]
    public void TeilImportOhneAnbieterNimmtDenAnbieterDesGeraets(string device, string expected)
    {
        var current = new WhisperLoomSettings();
        current.Stt.ProviderId = device;
        current.Stt.BaseUrl = device == "custom" ? "http://192.168.1.5:8000/v1" : "";
        var file = SyncDocument.Parse("""{"schemaVersion":1,"stt":{"model":"whisper-1"},"llm":{"stageModels":{"polish":"gpt-5-mini"}}}""");
        Assert.Empty(file.RetiredModels!); // ohne Stand kein Anbieter bekannt: Parse fasst es nicht an
        Assert.Equal("whisper-1", (string?)file.Settings["stt"]!["model"]);

        var preview = SyncImport.Preview(current, file, PolicySettings.None, []);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Equal(expected, preview.Settings!.Stt.Model);
        // „wie Erkennung“: die Stufe folgt demselben Anbieter.
        Assert.Equal(device == "openai" ? "gpt-5.6-terra" : "gpt-5-mini", preview.Settings.Llm.StageModels["polish"]);
    }

    /// <summary>Prüfrunde 1, R2: Vorgaben der Richtlinie gehen auf den Nachfolger (sonst setzte „Zurücksetzen“ das alte Modell).</summary>
    [Fact]
    public void VorgabenDerRichtlinieGehenAufDenNachfolger()
    {
        var policy = new PolicySettings
        {
            Defaults = new Dictionary<string, JsonElement>
            {
                ["stt.model"] = JsonSerializer.SerializeToElement("whisper-1"),
                ["llmFallback.model"] = JsonSerializer.SerializeToElement("gpt-5-nano"),
                ["polish.trailingSpace"] = JsonSerializer.SerializeToElement(true),
            },
        };
        var (migrated, changed) = RetiredModels.ApplyDefaults(policy, new WhisperLoomSettings());
        Assert.Equal(["stt.model", "llmFallback.model"], changed);
        Assert.Equal("gpt-transcribe", migrated.Defaults!["stt.model"].GetString());
        Assert.Equal("gpt-5.6-luna", migrated.Defaults["llmFallback.model"].GetString());
        Assert.True(migrated.Defaults["polish.trailingSpace"].GetBoolean());

        // Eigener Server als Vorgabe-Anbieter: whisper-1 bleibt; ohne Modell-Vorgaben dieselbe Instanz.
        var custom = policy with { Defaults = new Dictionary<string, JsonElement>(policy.Defaults!) { ["stt.providerId"] = JsonSerializer.SerializeToElement("custom") } };
        Assert.Equal("whisper-1", RetiredModels.ApplyDefaults(custom, new WhisperLoomSettings()).Policy.Defaults!["stt.model"].GetString());
        Assert.Same(PolicySettings.None, RetiredModels.ApplyDefaults(PolicySettings.None, new WhisperLoomSettings()).Policy);
    }

    [Fact]
    public void ProvisioningMitVorgabeStelltDieVorgabeUm()
    {
        var plan = Provisioning.Plan(new WhisperLoomSettings(), SyncDocument.Parse("""
            {"schemaVersion":1,"stt":{"model":"whisper-1"},"policy":{"levels":{"stt.model":"default"}}}
            """));
        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Equal("gpt-transcribe", plan.Settings!.Stt.Model);
        Assert.Equal("gpt-transcribe", plan.Policy.Defaults!["stt.model"].GetString());
        Assert.Empty(plan.RetiredFixed); // Vorgabe ist nicht Fest
    }

    [Fact]
    public void ImportDateiWirdBeimLesenUmgestellt()
    {
        var file = SyncDocument.Parse("""{"schemaVersion":1,"stt":{"providerId":"openai","model":"whisper-1"}}""");
        Assert.Equal("gpt-transcribe", (string?)file.Settings["stt"]!["model"]);
        Assert.Equal(["stt.model"], file.RetiredModels);
    }

    [Fact]
    public void FestesAbgeschaltetesModellGehtAufDenNachfolgerMitWarnungOhneWert()
    {
        var plan = Provisioning.Plan(new WhisperLoomSettings(), SyncDocument.Parse("""
            {"schemaVersion":1,"stt":{"providerId":"openai","model":"whisper-1"},"llm":{"providerId":"openai","stageModels":{"polish":"gpt-5-mini"}},
             "policy":{"lockSettings":["stt.model"]}}
            """));
        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Equal("gpt-transcribe", plan.Settings!.Stt.Model);
        Assert.Equal("gpt-5.6-terra", plan.Settings.Llm.StageModels["polish"]);
        Assert.Equal(["stt.model"], plan.RetiredFixed); // nur der feste Pfad, die Vorgabe/freie Stufe nicht
        var warning = Assert.Single(plan.Warnings, w => w.StartsWith("stt.model:", StringComparison.Ordinal));
        Assert.DoesNotContain("whisper-1", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("gpt-transcribe", warning, StringComparison.Ordinal);
    }
}
