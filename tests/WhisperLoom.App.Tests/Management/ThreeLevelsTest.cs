using System.Text.Json.Nodes;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Design;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Management;

/// <summary>
/// Drei Stufen anwenden (Kriterium 4), je Quelle Firmen-Server und Provisioning-Datei: Fest erzwungen und ausgeblendet,
/// Vorgabe gesetzt bis zur eigenen Änderung, Frei lokal, Import und lokale Datei überschreiben Fest nicht, Tastenkürzel lokal.
/// </summary>
public sealed class ThreeLevelsTest
{
    public enum Source
    {
        Server,
        File,
    }

    /// <summary>Gleiche Richtlinie aus beiden Quellen: Stufe Fest, Vorgabe, Frei und ein Tastenkürzel, das Fest sein soll.</summary>
    private static async Task<(ManagementRig Rig, Func<Task> Reapply)> Apply(Source source)
    {
        var r = new ManagementRig();
        var settings = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["refine"] = new JsonObject { ["stage"] = "beautify" },
            ["output"] = new JsonObject { ["alwaysCopy"] = true },
            ["polish"] = new JsonObject { ["trailingSpace"] = false },
            ["desktop"] = new JsonObject { ["hotkeyHold"] = "Ctrl+Shift" },
        };
        var levels = new JsonObject
        {
            ["refine.stage"] = "fixed",
            ["output.alwaysCopy"] = "default",
            ["polish.trailingSpace"] = "free",
            ["desktop.hotkeyHold"] = "fixed",
        };
        if (source == Source.Server)
        {
            r.Server.AutoApprove = true;
            r.Server.Settings = settings;
            r.Server.Levels = levels;
            await r.JoinAsync();
            return (r, async () =>
            {
                r.Cache.Clear(); // ohne ETag: der Server schickt die Konfiguration erneut (200)
                await r.Service.SyncAsync(CancellationToken.None);
            });
        }
        var file = (JsonObject)settings.DeepClone();
        file["policy"] = new JsonObject { ["levels"] = levels.DeepClone() };
        var path = Path.Combine(r.F.DataDir, "provision.json");
        File.WriteAllText(path, file.ToJsonString());
        async Task Provision()
        {
            r.F.Store.Save(r.Settings);
            var code = await new SyncCommands(r.F.DataDir, r.User, TextWriter.Null, TextWriter.Null).RunAsync("provision", path, false, CancellationToken.None);
            Assert.Equal(0, code);
            var loaded = r.F.Store.Load();
            var policy = r.Policies.Load();
            r.F.Controller.Policy = policy;
            await r.F.Controller.ApplySettingsAsync(loaded);
        }
        await Provision();
        return (r, Provision);
    }

    [Theory]
    [InlineData(Source.Server)]
    [InlineData(Source.File)]
    public async Task Fest_ist_erzwungen_und_in_der_Oberflaeche_ausgeblendet(Source source)
    {
        var (r, _) = await Apply(source);
        using (r)
        {
            Assert.Equal("beautify", r.Settings.Refine.Stage);
            Assert.True(r.Policy.IsLocked("refine.stage"));
            Assert.True(r.F.Controller.StageLocked); // Hotkey, Tray, CLI ändern sie nicht
            var ui = new PolicyManagedSource(r.Policy, () => r.Settings);
            Assert.Equal(ManagedState.Fixed, ui.StateOf("refine.stage")); // SettingRow blendet Fest aus (W2-UI1)
            Assert.NotNull(ui.ManagerName);
        }
    }

    [Theory]
    [InlineData(Source.Server)]
    [InlineData(Source.File)]
    public async Task Vorgabe_gilt_bis_der_Nutzer_sie_aendert(Source source)
    {
        var (r, reapply) = await Apply(source);
        using (r)
        {
            Assert.True(r.Settings.Output.AlwaysCopy);
            Assert.Equal(ManagedState.Default, r.Policy.StateOf("output.alwaysCopy"));

            // Eigene Änderung wie im Einstellungsfenster → Override, die nächste Vorgabe fährt nicht darüber.
            var edited = SettingsJson.Deserialize(SettingsJson.Serialize(r.Settings));
            edited.Output.AlwaysCopy = false;
            var preview = SyncImport.ApplyPolicy(r.Settings, edited, r.Policy);
            await r.F.Controller.ApplySettingsAsync(preview.Settings!);
            Assert.Contains("output.alwaysCopy", r.Settings.Management.Overrides);
            await reapply();
            Assert.False(r.Settings.Output.AlwaysCopy);

            // „Auf Firmenvorgabe zurücksetzen“
            var reset = ManagedDefaults.Reset(r.Settings, r.Policy, "output.alwaysCopy");
            Assert.True(reset.Output.AlwaysCopy);
            Assert.DoesNotContain("output.alwaysCopy", reset.Management.Overrides);
        }
    }

    [Theory]
    [InlineData(Source.Server)]
    [InlineData(Source.File)]
    public async Task Frei_bleibt_lokal_und_Tastenkuerzel_sind_nie_Fest(Source source)
    {
        var (r, _) = await Apply(source);
        using (r)
        {
            Assert.Equal(ManagedState.Free, r.Policy.StateOf("polish.trailingSpace"));
            Assert.False(r.Policy.IsLocked("polish.trailingSpace"));
            Assert.False(r.Policy.IsLocked("desktop.hotkeyHold")); // gewünscht Fest → Vorgabe
            Assert.Equal(ManagedState.Default, r.Policy.StateOf("desktop.hotkeyHold"));
            Assert.Equal("Ctrl+Shift", SettingsStore.Desktop(r.Settings).HotkeyHold);
            Assert.False(r.Policy.IsLocked("desktop.hotkeyToggle"));
        }
    }

    [Theory]
    [InlineData(Source.Server)]
    [InlineData(Source.File)]
    public async Task Import_und_lokale_Datei_ueberschreiben_Fest_nicht(Source source)
    {
        var (r, _) = await Apply(source);
        using (r)
        {
            r.F.Store.Save(r.Settings);
            var import = Path.Combine(r.F.DataDir, "fremd.json");
            File.WriteAllText(import, """{"schemaVersion":1,"refine":{"stage":"off"},"polish":{"trailingSpace":true}}""");
            var code = await new SyncCommands(r.F.DataDir, r.User, TextWriter.Null, TextWriter.Null).RunAsync("import", import, false, CancellationToken.None);
            Assert.Equal(0, code);
            var afterImport = r.F.Store.Load();
            Assert.Equal("beautify", afterImport.Refine.Stage);
            Assert.True(afterImport.Polish.TrailingSpace); // Frei übernimmt der Import

            // settings.json von Hand geändert: Start bzw. Neuladen setzt den festen Wert wieder ein (policy-fixed.json).
            var raw = JsonNode.Parse(File.ReadAllText(r.F.Store.FilePath))!.AsObject();
            raw["refine"]!["stage"] = "off";
            File.WriteAllText(r.F.Store.FilePath, raw.ToJsonString());
            var (enforced, changed, _) = r.Policies.EnforceFixed(r.F.Store.Load(), r.Policies.Load());
            Assert.Equal(1, changed);
            Assert.Equal("beautify", enforced.Refine.Stage);
        }
    }

    [Fact]
    public async Task Lokale_Provisioning_Datei_ueberschreibt_Server_Fest_nicht()
    {
        var (r, _) = await Apply(Source.Server);
        using (r)
        {
            r.F.Store.Save(r.Settings);
            var path = Path.Combine(r.F.DataDir, "lokal.json");
            File.WriteAllText(path, """{"schemaVersion":1,"refine":{"stage":"off"},"polish":{"trailingSpace":true},"policy":{"lockSettings":true}}""");
            var errors = new StringWriter();
            var code = await new SyncCommands(r.F.DataDir, r.User, TextWriter.Null, errors).RunAsync("provision", path, false, CancellationToken.None);
            Assert.Equal(0, code);
            Assert.Contains("Firmen-Server verwaltet", errors.ToString());
            var after = r.F.Store.Load();
            Assert.Equal("beautify", after.Refine.Stage);
            Assert.True(after.Polish.TrailingSpace);
            Assert.Equal("approved", after.Management.State);
            Assert.True(r.Policies.Load().IsLocked("refine.stage"));
            Assert.False(r.Policies.Load().IsLocked("polish.trailingSpace")); // Richtlinie der Datei gilt nicht
        }
    }

    [Fact]
    public async Task Server_gibt_frueher_festen_Pfad_frei_der_Wert_bleibt()
    {
        var (r, _) = await Apply(Source.Server);
        using (r)
        {
            r.Server.Levels.Remove("refine.stage");
            r.Server.Settings.AsObject().Remove("refine");
            await r.Service.SyncAsync(CancellationToken.None);
            Assert.False(r.Policy.IsLocked("refine.stage"));
            Assert.Equal("beautify", r.Settings.Refine.Stage);
        }
    }
}
