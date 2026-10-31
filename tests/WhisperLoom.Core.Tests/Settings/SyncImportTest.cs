using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>Import mit Vorschau (Diff-Liste), Teil-Dateien, Validierung und Migration.</summary>
public class SyncImportTest
{
    [Fact]
    public void Partial_file_changes_only_its_fields_and_lists_them_in_the_preview()
    {
        var current = new WhisperLoomSettings { Language = "en" };
        current.Polish.CustomFillers.Add("halt");

        var preview = SyncFixtures.Import("""{"schemaVersion":1,"refine":{"stage":"beautify"},"llm":{"stageModels":{"polish":"m1"}}}""", current);

        Assert.True(preview.CanApply);
        Assert.Equal("beautify", preview.Settings!.Refine.Stage);
        Assert.Equal("en", preview.Settings.Language);
        Assert.Equal(["halt"], preview.Settings.Polish.CustomFillers);
        Assert.Equal(
            [new SettingsChange("llm.stageModels.polish", null, "\"m1\""), new SettingsChange("refine.stage", "\"off\"", "\"beautify\"")],
            preview.Changes);
    }

    [Fact]
    public void Same_values_give_an_empty_preview()
    {
        var json = SyncFixtures.Read("settings-v1.json");
        var current = SyncFixtures.Import(json).Settings!;

        Assert.Empty(SyncFixtures.Import(json, current).Changes);
    }

    [Theory]
    [InlineData("""{"stt":{"providerId":"kein-anbieter"}}""", "stt.providerId")]
    [InlineData("""{"llm":{"providerId":"elevenlabs"}}""", "llm.providerId")]
    [InlineData("""{"history":{"size":7}}""", "history.size")]
    [InlineData("""{"refine":{"stage":"laut"}}""", "refine.stage")]
    [InlineData("""{"stt":{"baseUrl":"ftp://server"}}""", "stt.baseUrl")]
    [InlineData("""{"language":"Deutsch"}""", "language")]
    [InlineData("""{"llm":{"stageModels":{"readable":"m"}}}""", "llm.stageModels.readable")]
    [InlineData("""{"history":{"size":"viele"}}""", "history.size")]
    public void Invalid_values_block_the_import_and_name_the_path(string json, string path)
    {
        var preview = SyncFixtures.Import(json);

        Assert.False(preview.CanApply);
        Assert.Null(preview.Settings);
        Assert.Contains(preview.Errors, e => e.Contains(path, StringComparison.Ordinal));
    }

    [Fact]
    public void Plain_import_ignores_secrets_and_policy_with_a_warning()
    {
        var preview = SyncFixtures.Import(SyncFixtures.Read("settings-v1-provisioning.json"));

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Single(preview.Warnings);
        Assert.True(preview.Settings!.History.Enabled); // policy.historyEnabled=false gilt hier nicht
        Assert.DoesNotContain(preview.Changes, c => c.Path.StartsWith("secrets", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, c => (c.New ?? "").Contains("test-key-not-real", StringComparison.Ordinal));
    }

    [Fact]
    public void Migration_lifts_an_unversioned_file_to_v1()
    {
        var root = SyncFixtures.Tree("""{"refine":{"stage":"paragraphs"},"share":{"stage":"paragraphs"},"polish":{"customFillers":["Halt"," halt","Eben"]}}""");

        var from = SyncMigration.Migrate(root);

        Assert.Equal(0, from);
        Assert.Equal(1, root["schemaVersion"]!.GetValue<int>());
        Assert.Equal("polish", root["refine"]!["stage"]!.GetValue<string>());
        Assert.Equal("polish", root["share"]!["stage"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(new JsonArray("halt", "eben"), root["polish"]!["customFillers"]));
    }

    [Fact]
    public void Migration_leaves_v1_unchanged()
    {
        var root = SyncFixtures.Tree(SyncFixtures.Read("settings-v1.json"));
        var copy = root.DeepClone();

        Assert.Equal(1, SyncMigration.Migrate(root));
        Assert.True(JsonNode.DeepEquals(copy, root));
    }

    [Fact]
    public void Migration_rejects_newer_and_broken_versions()
    {
        Assert.Throws<NotSupportedException>(() => SyncDocument.Parse("""{"schemaVersion":2}"""));
        Assert.Throws<SyncFormatException>(() => SyncDocument.Parse("""{"schemaVersion":"eins"}"""));
        Assert.Throws<SyncFormatException>(() => SyncDocument.Parse("""{"schemaVersion":-1}"""));
    }
}
