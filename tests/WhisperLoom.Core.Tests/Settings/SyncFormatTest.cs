using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

internal static class SyncFixtures
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    public static JsonObject Tree(string json) => JsonNode.Parse(json)!.AsObject();

    public static ImportPreview Import(string json, WhisperLoomSettings? current = null, PolicySettings? policy = null) =>
        SyncImport.Preview(current ?? new WhisperLoomSettings(), SyncDocument.Parse(json), policy ?? PolicySettings.None);
}

/// <summary>Format v1: Roundtrip der Fixture, Erhalt unbekannter Felder, fremder Abschnitt, Export ohne Keys.</summary>
public class SyncFormatTest
{
    [Fact]
    public void Fixture_roundtrip_import_then_export_gives_the_same_json()
    {
        var json = SyncFixtures.Read("settings-v1.json");

        var preview = SyncFixtures.Import(json);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Empty(preview.Warnings);
        var exported = SyncExport.ToJson(preview.Settings!);
        Assert.True(JsonNode.DeepEquals(SyncFixtures.Tree(json), SyncFixtures.Tree(exported)), exported);
    }

    [Fact]
    public void Fixture_fields_land_in_the_model()
    {
        var s = SyncFixtures.Import(SyncFixtures.Read("settings-v1.json")).Settings!;

        Assert.Equal("groq", s.Stt.ProviderId);
        Assert.Equal("whisper-large-v3-turbo", s.Stt.Model);
        Assert.Equal("WhisperLoom\nKubernetes\nTreitges", s.Stt.Vocabulary);
        Assert.Equal("polish", s.Refine.Stage);
        Assert.Equal("beautify", s.Share.Stage);
        Assert.Equal("llama-3.3-70b-versatile", s.Llm.StageModels["summarize"]);
        Assert.Equal(["sozusagen", "quasi"], s.Polish.CustomFillers);
        Assert.Equal(["also"], s.Polish.DisabledFillers);
        Assert.Equal(100, s.History.Size);
    }

    [Fact]
    public void Unknown_top_level_fields_and_unknown_desktop_fields_survive_import_and_export()
    {
        var tree = SyncFixtures.Tree(SyncFixtures.Read("settings-v1.json"));
        tree["futureFeature"] = new JsonObject { ["level"] = 3 };
        tree["desktop"]!["hotkeys"] = new JsonObject { ["toggle"] = "Ctrl+Alt+Space" };

        var s = SyncFixtures.Import(tree.ToJsonString()).Settings!;
        var exported = SyncFixtures.Tree(SyncExport.ToJson(s));

        Assert.Equal(3, exported["futureFeature"]!["level"]!.GetValue<int>());
        Assert.Equal("Ctrl+Alt+Space", exported["desktop"]!["hotkeys"]!["toggle"]!.GetValue<string>());
    }

    [Fact]
    public void Android_section_is_ignored_but_passed_through()
    {
        var tree = SyncFixtures.Tree(SyncFixtures.Read("settings-v1.json"));
        // Felder, die wie Desktop-Felder heißen, dürfen nichts bewirken; ungültige Werte keinen Fehler.
        tree["android"] = new JsonObject { ["language"] = "fr", ["stt"] = new JsonObject { ["providerId"] = "kein-anbieter" }, ["floatX"] = 0.8 };

        var preview = SyncFixtures.Import(tree.ToJsonString());

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Equal("de", preview.Settings!.Language);
        Assert.Equal("groq", preview.Settings.Stt.ProviderId);
        Assert.DoesNotContain(preview.Changes, c => c.Path.StartsWith("android", StringComparison.Ordinal));
        var exported = SyncFixtures.Tree(SyncExport.ToJson(preview.Settings));
        Assert.True(JsonNode.DeepEquals(tree["android"], exported["android"]));
    }

    [Fact]
    public void Export_never_contains_secrets_or_policy()
    {
        var file = SyncDocument.Parse(SyncFixtures.Read("settings-v1-provisioning.json"));
        var plan = Provisioning.Plan(new WhisperLoomSettings(), file);
        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        // Auch ein von Hand in settings.json eingetragener Abschnitt geht nicht hinaus.
        plan.Settings!.Extra ??= [];
        plan.Settings.Extra["secrets"] = System.Text.Json.JsonSerializer.SerializeToElement(new { stt = "test-key-not-real-9" });
        plan.Settings.Extra["policy"] = System.Text.Json.JsonSerializer.SerializeToElement(new { lockSettings = true });

        var exported = SyncExport.ToJson(plan.Settings);

        var tree = SyncFixtures.Tree(exported);
        Assert.Null(tree["secrets"]);
        Assert.Null(tree["policy"]);
        Assert.DoesNotContain("test-key-not-real", exported, StringComparison.Ordinal);
        Assert.Equal("groq", tree["stt"]!["providerId"]!.GetValue<string>());
    }

    [Fact]
    public void Invalid_json_is_rejected_without_echoing_the_content()
    {
        var e = Assert.Throws<SyncFormatException>(() => SyncDocument.Parse("{\"secrets\": {\"groq\": \"test-key-not-real-5\"}"));

        Assert.DoesNotContain("test-key-not-real", e.Message, StringComparison.Ordinal);
        Assert.Throws<SyncFormatException>(() => SyncDocument.Parse("[1, 2]"));
        Assert.Throws<SyncFormatException>(() => SyncDocument.Parse("{\"secrets\": [\"x\"]}"));
        Assert.Throws<SyncFormatException>(() => SyncDocument.Parse("{\"secrets\": {\"groq\": \"\"}}"));
    }
}
