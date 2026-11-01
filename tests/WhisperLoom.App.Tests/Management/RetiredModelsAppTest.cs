using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Management;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;
using WhisperLoom.Core.Testing;
using WhisperLoom.Core.Tests.Support;

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

    /// <summary>
    /// Prüfrunde 1, K1: Gerät vor dem Update provisioniert (Fest stt.model = whisper-1 in policy.json + policy-fixed.json,
    /// Vorgabe sttFallback.model). Der Start darf das abgeschaltete Modell nicht aus policy-fixed.json zurückholen.
    /// </summary>
    [AvaloniaFact]
    public void Start_holt_ein_festes_abgeschaltetes_Modell_nicht_aus_policy_fixed_zurueck()
    {
        var dir = Directory.CreateTempSubdirectory("wl-retired-host-");
        try
        {
            var data = dir.FullName;
            File.WriteAllText(Path.Combine(data, "settings.json"),
                """{"schemaVersion":1,"engine":"online","stt":{"providerId":"openai","model":"whisper-1"},"sttFallback":{"providerId":"openai","model":"gpt-4o-mini-transcribe"}}""");
            var policies = new PolicyStore(data);
            policies.Save(new PolicySettings
            {
                Locked = ["stt.model"],
                Defaults = new Dictionary<string, JsonElement> { ["sttFallback.model"] = JsonSerializer.SerializeToElement("gpt-4o-mini-transcribe") },
            });
            File.WriteAllText(policies.FixedPath, """{"stt.model":"whisper-1"}""");

            var host = new AppHost(new AppOptions(data), new MemorySecretStore(), _ => new FakeDictationPipeline(), new FakeRecorder(), new CaptureSink());

            Assert.Equal("gpt-transcribe", host.Controller.Settings.Stt.Model);
            Assert.Equal("gpt-transcribe", (string?)JsonNode.Parse(File.ReadAllText(policies.FixedPath))!["stt.model"]);
            Assert.Equal("gpt-transcribe", policies.Load().Defaults!["sttFallback.model"].GetString());
            var log = File.ReadAllText(host.Log.FilePath);
            Assert.Contains("management.retired_model", log, StringComparison.Ordinal);
            Assert.DoesNotContain("whisper-1", log, StringComparison.Ordinal); // nur Pfade, keine Werte

            // Zweiter Start (idempotent): nichts mehr umzustellen, nichts zurückgeholt.
            var (enforced, changed, retired) = policies.EnforceFixed(new SettingsStore(data).Load(), policies.Load());
            Assert.Equal((0, 0), (changed, retired.Count));
            Assert.Equal("gpt-transcribe", enforced.Stt.Model);
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
