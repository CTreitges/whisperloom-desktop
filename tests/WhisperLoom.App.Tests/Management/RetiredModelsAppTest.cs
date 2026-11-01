using System.Text.Json.Nodes;
using WhisperLoom.App.Management;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Management;

/// <summary>TD-A4: abgeschaltete OpenAI-Modelle beim Laden von <c>settings.json</c> und in der Firmen-Konfiguration.</summary>
public sealed class RetiredModelsAppTest
{
    [Fact]
    public void Gespeicherte_Einstellungen_gehen_beim_Laden_auf_den_Nachfolger()
    {
        var dir = Directory.CreateTempSubdirectory("wl-retired-");
        try
        {
            var store = new SettingsStore(dir.FullName);
            File.WriteAllText(store.FilePath, """{"schemaVersion":1,"engine":"online","stt":{"providerId":"openai","model":"gpt-4o-transcribe"},"llmFallback":{"model":"gpt-5-nano"}}""");
            var s = store.Load();
            Assert.Equal("gpt-transcribe", s.Stt.Model);
            Assert.Equal("gpt-5.6-luna", s.LlmFallback.Model);
            store.Save(s);
            Assert.Equal("gpt-transcribe", store.Load().Stt.Model); // idempotent
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Firmen_Konfiguration_meldet_feste_abgeschaltete_Modelle_nur_als_Pfad()
    {
        var current = new WhisperLoomSettings();
        current.Management.ServerUrl = "https://firma.example";
        var config = JsonNode.Parse("""
            {"keyMode":"proxy",
             "settings":{"schemaVersion":1,"stt":{"providerId":"openai","model":"whisper-1"},"sttFallback":{"providerId":"openai","model":"gpt-4o-mini-transcribe"}},
             "policy":{"levels":{"stt.model":"fixed","sttFallback.model":"default"}}}
            """)!.AsObject();

        var plan = ConfigApply.Plan(current, config, 1_000);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Equal("gpt-transcribe", plan.Settings!.Stt.Model);
        Assert.Equal("gpt-transcribe", plan.Settings.SttFallback.Model);
        Assert.Equal(["stt.model"], plan.RetiredFixed);
    }
}
