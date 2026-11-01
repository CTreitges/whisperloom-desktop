using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.Core.Tests.Settings;

/// <summary>Tastenkürzel sind nie Fest; management.* ist nie Fest (außer allowLeave), nie exportiert, nie importiert.</summary>
public class HotkeyAndManagementPolicyTest
{
    private static ProvisioningPlan Plan(string json, WhisperLoomSettings? current = null) =>
        Provisioning.Plan(current ?? new WhisperLoomSettings(), SyncDocument.Parse(json));

    [Theory]
    [InlineData("desktop")]
    [InlineData("desktop.hotkeyHold")]
    [InlineData("selection")]
    [InlineData("selection.hotkeyCombo")]
    public void Hotkeys_are_never_locked(string lockedPath)
    {
        var p = new PolicySettings { Locked = [lockedPath] };

        Assert.False(p.IsLocked("desktop.hotkeyHold"));
        Assert.False(p.IsLocked("Desktop.HotkeyToggle"));
        Assert.False(p.IsLocked("desktop.hotkeysEnabled"));
        Assert.False(p.IsLocked("selection.hotkeyCombo"));
        Assert.NotEqual(ManagedState.Fixed, p.StateOf("desktop.hotkeyRewrite"));
    }

    [Fact]
    public void Locked_section_still_locks_its_other_fields()
    {
        var p = new PolicySettings { Locked = ["desktop", "selection"] };

        Assert.True(p.IsLocked("desktop.proxyMode"));
        Assert.True(p.IsLocked("selection.floatingButton"));
    }

    [Fact]
    public void Fixed_hotkey_in_a_provisioning_file_becomes_default_with_warning()
    {
        var plan = Plan("""
            {"desktop":{"hotkeyHold":"Ctrl+Alt+H","proxyMode":"off"},"selection":{"hotkeyCombo":"Ctrl+Alt+M","ctrlRightClick":true},
             "policy":{"levels":{"desktop.hotkeyHold":"fixed","selection":"fixed","desktop.proxyMode":"fixed"}}}
            """);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.Contains(plan.Warnings, w => w.Contains("desktop.hotkeyHold", StringComparison.Ordinal) && w.Contains("nie Fest", StringComparison.Ordinal));
        Assert.Equal(ManagedState.Default, plan.Policy.StateOf("desktop.hotkeyHold"));
        Assert.Equal(ManagedState.Default, plan.Policy.StateOf("selection.hotkeyCombo")); // unter festem Abschnitt
        Assert.Equal(ManagedState.Fixed, plan.Policy.StateOf("selection.ctrlRightClick"));
        Assert.Equal(ManagedState.Fixed, plan.Policy.StateOf("desktop.proxyMode"));
        Assert.Equal("Ctrl+Alt+H", SyncFixtures.Tree(SyncExport.ToJson(plan.Settings!))["desktop"]!["hotkeyHold"]!.GetValue<string>());
    }

    [Fact]
    public void Lock_settings_true_does_not_lock_hotkeys_and_the_user_can_change_them()
    {
        var plan = Plan("""{"desktop":{"hotkeyHold":"Ctrl+Alt+H","proxyMode":"off"},"policy":{"lockSettings":true}}""");
        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        Assert.DoesNotContain("desktop.hotkeyHold", plan.Policy.Locked);

        var import = SyncFixtures.Import("""{"desktop":{"hotkeyHold":"Ctrl+Shift+H","proxyMode":"manual"}}""", plan.Settings, plan.Policy);

        Assert.True(import.CanApply, string.Join("; ", import.Errors));
        var desktop = SyncFixtures.Tree(SyncExport.ToJson(import.Settings!))["desktop"]!;
        Assert.Equal("Ctrl+Shift+H", desktop["hotkeyHold"]!.GetValue<string>());
        Assert.Equal("off", desktop["proxyMode"]!.GetValue<string>());
        Assert.Equal(["desktop.hotkeyHold"], import.Settings!.Management.Overrides);
    }

    [Fact]
    public void Management_is_never_fixed_except_allow_leave()
    {
        var p = new PolicySettings { Locked = ["management"] };

        Assert.True(p.IsLocked("management.allowLeave"));
        Assert.False(p.IsLocked("management.serverUrl"));
        Assert.False(p.IsLocked("management.overrides"));
        Assert.False(new PolicySettings { Locked = ["management.state"] }.IsLocked("management.state"));
    }

    [Fact]
    public void Provisioning_sets_only_server_url_code_and_allow_leave()
    {
        var plan = Plan("""
            {"management":{"serverUrl":"https://wl.example.com","code":"ABCD-EFGH-JKMN-PQRS","allowLeave":false,
                           "deviceId":"d_x","state":"approved","overrides":["refine.stage"]},
             "policy":{"levels":{"management.allowLeave":"fixed","management.serverUrl":"fixed"}}}
            """);

        Assert.True(plan.CanApply, string.Join("; ", plan.Errors));
        var m = plan.Settings!.Management;
        Assert.Equal(("https://wl.example.com", "ABCD-EFGH-JKMN-PQRS", false), (m.ServerUrl, m.Code, m.AllowLeave));
        Assert.Equal(("", "none"), (m.DeviceId, m.State));
        Assert.Empty(m.Overrides);
        Assert.Contains(plan.Warnings, w => w.StartsWith("management.deviceId", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, w => w.Contains("management.serverUrl: nie Fest", StringComparison.Ordinal));
        Assert.True(plan.Policy.IsLocked("management.allowLeave"));
        Assert.False(plan.Policy.IsLocked("management.serverUrl"));
    }

    [Fact]
    public void Management_is_not_exported()
    {
        var s = new WhisperLoomSettings();
        s.Management.ServerUrl = "https://wl.example.com";
        s.Management.DeviceId = "d_7k3m9q2x4w8z";
        s.Management.Overrides = ["refine.stage"];

        var tree = SyncFixtures.Tree(SyncExport.ToJson(s));

        Assert.Null(tree["management"]);
        Assert.DoesNotContain("wl.example.com", tree.ToJsonString());
    }

    [Theory]
    [InlineData("""{"management":{"serverUrl":"https://evil.example.com","state":"approved","allowLeave":false}}""")]
    [InlineData("""{"Management":{"ServerUrl":"https://evil.example.com"}}""")]
    public void Management_is_not_importable(string json)
    {
        var current = new WhisperLoomSettings();
        current.Management.ServerUrl = "https://wl.example.com";
        current.Management.State = "approved";

        var preview = SyncFixtures.Import(json, current);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors));
        Assert.Equal(("https://wl.example.com", "approved", true),
            (preview.Settings!.Management.ServerUrl, preview.Settings.Management.State, preview.Settings.Management.AllowLeave));
        Assert.Contains(preview.Warnings, w => w.StartsWith("management", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, c => c.Path.StartsWith("management", StringComparison.OrdinalIgnoreCase));
        Assert.Null(SyncFixtures.Tree(SyncExport.ToJson(preview.Settings))["Management"]);
    }

    [Fact]
    public void Own_edits_keep_management_state()
    {
        var current = new WhisperLoomSettings();
        current.Management.State = "approved";
        var edited = SettingsJson.Deserialize(SettingsJson.Serialize(current));
        edited.Refine.Stage = "polish";

        var preview = SyncImport.ApplyPolicy(current, edited, PolicySettings.None);

        Assert.Equal("approved", preview.Settings!.Management.State);
        Assert.IsType<JsonObject>(SyncFixtures.Tree(SettingsJson.Serialize(preview.Settings))["management"]);
    }
}
