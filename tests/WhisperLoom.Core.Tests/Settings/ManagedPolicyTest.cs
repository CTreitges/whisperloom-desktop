using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>Drei Stufen (Fest / Vorgabe / Frei): lockSettings → Fest, levels, User-Override, Zurücksetzen, policy.json.</summary>
public class ManagedPolicyTest
{
    private static ProvisioningPlan Plan(string json, WhisperLoomSettings? current = null) =>
        Provisioning.Plan(current ?? new WhisperLoomSettings(), SyncDocument.Parse(json));

    [Fact]
    public void Lock_settings_list_and_true_map_to_fixed()
    {
        var list = Plan("""{"stt":{"providerId":"groq"},"refine":{"stage":"polish"},"policy":{"lockSettings":["stt.providerId"]}}""");
        var all = Plan("""{"stt":{"providerId":"groq"},"refine":{"stage":"polish"},"policy":{"lockSettings":true}}""");

        Assert.True(list.CanApply, string.Join("; ", list.Errors));
        Assert.Equal(ManagedState.Fixed, list.Policy.StateOf("stt.providerId"));
        Assert.Equal(ManagedState.Free, list.Policy.StateOf("refine.stage"));
        Assert.Equal(ManagedState.Fixed, all.Policy.StateOf("refine.stage"));
        Assert.Equal(ManagedState.Free, all.Policy.StateOf("stt.model")); // nicht in der Datei
        Assert.Null(all.Policy.Defaults);
    }

    [Fact]
    public void Levels_set_all_three_states_and_win_over_lock_settings()
    {
        var plan = Plan("""
            {"stt":{"providerId":"groq","model":"m1"},"refine":{"stage":"polish"},"polish":{"removeFillers":false},
             "policy":{"lockSettings":true,"levels":{"refine.stage":"default","polish":"free","llm":"fixed"}}}
            """);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Equal(ManagedState.Fixed, plan.Policy.StateOf("stt.model"));
        Assert.Equal(ManagedState.Default, plan.Policy.StateOf("refine.stage"));
        Assert.Equal(ManagedState.Default, plan.Policy.StateOf("refine")); // Abschnitt mit Vorgabe darunter
        Assert.Equal(ManagedState.Free, plan.Policy.StateOf("polish.removeFillers"));
        Assert.Equal(ManagedState.Fixed, plan.Policy.StateOf("llm.model")); // fest ohne Wert in der Datei: aktueller Wert bleibt
        Assert.False(plan.Settings!.Polish.RemoveFillers); // frei = einmalig gesetzt
        Assert.Contains("default refine.stage", plan.Summary());
    }

    [Theory]
    [InlineData("""{"policy":{"levels":{"refine.stage":"sometimes"}}}""")]
    [InlineData("""{"policy":{"levels":["refine.stage"]}}""")]
    [InlineData("""{"policy":{"levels":{"refine.stage":"default"}}}""")] // Vorgabe ohne Wert
    [InlineData("""{"refine":{"stage":"polish"},"policy":{"levels":{"refine.nope":"fixed"}}}""")]
    public void Bad_levels_are_errors(string json)
    {
        Assert.False(Plan(json).CanApply);
    }

    [Fact]
    public void Default_override_survives_a_new_default_and_reset_restores_it()
    {
        var first = Plan("""{"refine":{"stage":"polish"},"history":{"size":25},"policy":{"levels":{"refine.stage":"default","history.size":"default"}}}""");
        Assert.True(first.CanApply, string.Join("; ", first.Errors));
        Assert.Equal("polish", first.Settings!.Refine.Stage);
        Assert.Empty(first.Settings.Management.Overrides);

        // Nutzer ändert die Vorgabe im Einstellungsfenster.
        var edited = SettingsJson.Deserialize(SettingsJson.Serialize(first.Settings));
        edited.Refine.Stage = "beautify";
        var own = SyncImport.ApplyPolicy(first.Settings, edited, first.Policy);
        Assert.True(own.CanApply, string.Join("; ", own.Errors));
        Assert.Equal(["refine.stage"], own.Settings!.Management.Overrides);

        // Neue Firmenvorgabe: fährt nicht über die Änderung, die andere Vorgabe folgt.
        var second = Plan("""{"refine":{"stage":"summarize"},"history":{"size":100},"policy":{"levels":{"refine.stage":"default","history.size":"default"}}}""", own.Settings);
        Assert.True(second.CanApply, string.Join("; ", second.Errors));
        Assert.Equal("beautify", second.Settings!.Refine.Stage);
        Assert.Equal(100, second.Settings.History.Size);
        Assert.Equal(["refine.stage"], second.Settings.Management.Overrides);

        // „Auf Firmenvorgabe zurücksetzen“.
        var reset = ManagedDefaults.Reset(second.Settings, second.Policy, "refine.stage");
        Assert.Equal("summarize", reset.Refine.Stage);
        Assert.Empty(reset.Management.Overrides);
        Assert.Equal("beautify", second.Settings.Refine.Stage); // Original unverändert
    }

    [Fact]
    public void User_import_of_a_default_path_counts_as_override_and_same_value_does_not()
    {
        var plan = Plan("""{"refine":{"stage":"polish"},"policy":{"levels":{"refine.stage":"default"}}}""");

        var changed = SyncFixtures.Import("""{"refine":{"stage":"beautify"}}""", plan.Settings, plan.Policy);
        var same = SyncFixtures.Import("""{"refine":{"stage":"polish"}}""", plan.Settings, plan.Policy);

        Assert.Equal(["refine.stage"], changed.Settings!.Management.Overrides);
        Assert.Empty(same.Settings!.Management.Overrides);
    }

    [Fact]
    public void Reset_without_path_restores_all_defaults()
    {
        var plan = Plan("""{"refine":{"stage":"polish"},"output":{"alwaysCopy":true},"policy":{"levels":{"refine":"default","output":"default"}}}""");
        var s = plan.Settings!;
        s.Refine.Stage = "off";
        s.Output.AlwaysCopy = false;
        ManagedDefaults.TrackOverrides(s, plan.Policy);
        Assert.Equal(["output.alwaysCopy", "refine.stage"], s.Management.Overrides);

        var reset = ManagedDefaults.Reset(s, plan.Policy);

        Assert.Equal("polish", reset.Refine.Stage);
        Assert.True(reset.Output.AlwaysCopy);
        Assert.Empty(reset.Management.Overrides);
    }

    [Fact]
    public void Policy_json_roundtrip_keeps_defaults()
    {
        var plan = Plan("""{"refine":{"stage":"polish"},"polish":{"customFillers":["halt"]},"policy":{"levels":{"refine.stage":"default","polish.customFillers":"default"}}}""");

        var back = PolicyJson.Deserialize(PolicyJson.Serialize(plan.Policy));

        Assert.Equal(ManagedState.Default, back.StateOf("refine.stage"));
        Assert.Equal("\"polish\"", back.Defaults!["refine.stage"].GetRawText());
        Assert.Equal("[\"halt\"]", JsonNode.Parse(back.Defaults["polish.customFillers"].GetRawText())!.ToJsonString());
        Assert.True(back.IsActive);
    }

    [Fact]
    public void Old_policy_json_without_defaults_still_reads()
    {
        var back = PolicyJson.Deserialize("""{"locked":["stt"],"historyEnabled":false}""");

        Assert.Null(back.Defaults);
        Assert.Equal(ManagedState.Fixed, back.StateOf("stt.model"));
        Assert.Equal(ManagedState.Free, back.StateOf("refine.stage"));
    }
}
