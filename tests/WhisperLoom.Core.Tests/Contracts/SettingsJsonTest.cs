using System.Text.Json;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Tests.Contracts;

public class SettingsJsonTest
{
    [Fact]
    public void StandardwerteWieAndroidPrefs()
    {
        var s = new WhisperLoomSettings();

        Assert.Equal(1, s.SchemaVersion);
        Assert.Equal("de", s.Language);
        Assert.Null(s.Engine);
        Assert.Equal("openai", s.Stt.ProviderId);
        Assert.Equal(LlmSettings.Same, s.Llm.ProviderId);
        Assert.False(s.SttFallback.Enabled);
        Assert.False(s.LlmFallback.Enabled);
        Assert.Equal(RefineMode.Off, RefineModes.FromKey(s.Refine.Stage));
        Assert.Equal(PolishCleanup.Plain, RefineKeys.CleanupFromKey(s.Refine.PolishCleanup));
        Assert.Equal(SummarizeForm.Auto, RefineKeys.FormFromKey(s.Refine.SummarizeForm));
        Assert.True(s.Refine.ParagraphsPolish && s.Refine.ParagraphsBeautify);
        Assert.True(s.Polish.RemoveFillers && s.Polish.AutoCapitalize && s.Polish.TrailingSpace);
        Assert.Equal("small", s.Offline.Model);
        Assert.Equal("local", s.Offline.Refine);
        Assert.True(s.History.Enabled);
        Assert.Equal(50, s.History.Size);
    }

    [Fact]
    public void RundreiseBehaeltAlleWerte()
    {
        var s = new WhisperLoomSettings { Language = "en", Engine = "online" };
        s.Refine.Stage = RefineMode.Prompt.Key();
        s.Refine.PromptEnabled = true;
        s.Llm.StageModels["prompt"] = "model-x";
        s.Polish.CustomFillers.Add("halt");
        s.Stt.Vocabulary = "WhisperLoom\nAvalonia";

        var back = SettingsJson.Deserialize(SettingsJson.Serialize(s));

        Assert.Equal(SettingsJson.Serialize(s), SettingsJson.Serialize(back));
        Assert.Equal("model-x", back.Llm.StageModels["prompt"]);
        Assert.Equal(["halt"], back.Polish.CustomFillers);
    }

    [Fact]
    public void JsonIstCamelCaseMitSchemaVersion()
    {
        var json = SettingsJson.Serialize(new WhisperLoomSettings());

        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"sttFallback\"", json);
        Assert.Contains("\"polishCleanup\"", json);
    }

    [Fact]
    public void FehlendeFelderBekommenStandardwerteUnbekannteWerdenIgnoriert()
    {
        var s = SettingsJson.Deserialize("""{ "language": "en", "zukunft": { "x": 1 }, "refine": { "stage": "beautify" } }""");

        Assert.Equal("en", s.Language);
        Assert.Equal(1, s.SchemaVersion);
        Assert.Equal("beautify", s.Refine.Stage);
        Assert.Equal("plain", s.Refine.PolishCleanup);
        Assert.Equal("openai", s.Stt.ProviderId);
    }

    [Fact]
    public void NeueresSchemaWirdAbgelehnt()
    {
        Assert.Throws<NotSupportedException>(() => SettingsJson.Deserialize("""{ "schemaVersion": 2 }"""));
    }

    [Fact]
    public void NullAbschnittIstUngueltig()
    {
        Assert.Throws<JsonException>(() => SettingsJson.Deserialize("""{ "stt": null }"""));
        Assert.Throws<JsonException>(() => SettingsJson.Deserialize("null"));
    }

    [Fact]
    public void KeinKeyFeldImSchema()
    {
        var json = SettingsJson.Serialize(new WhisperLoomSettings()).ToLowerInvariant();

        Assert.DoesNotContain("key\"", json);
        Assert.DoesNotContain("token", json);
        Assert.DoesNotContain("secret", json);
    }
}
