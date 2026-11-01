using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>Abnahme W2-UI1 Nr. 3: Fest = Zeile weg (Gruppe weg, wenn nur feste Zeilen), Vorgabe = sichtbar + Zurücksetzen, Frei = normal.</summary>
public sealed class ManagedLevelsTest
{
    /// <summary>Firmenquelle zum Testen (später ManagedState aus W2-C0).</summary>
    private sealed class FakeSource(Dictionary<string, ManagedLevel> levels, Dictionary<string, JsonNode?> defaults) : IManagedSource
    {
        public string? ManagerName => "Beispiel GmbH";

        public ManagedLevel LevelOf(string path) => levels.GetValueOrDefault(path, ManagedLevel.Free);

        public JsonNode? DefaultOf(string path) => defaults.GetValueOrDefault(path);
    }

    private static SettingsWindow Open(ControllerFixture f, IManagedSource source)
    {
        var shell = new SettingsShellViewModel(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()), _ => source);
        var window = new SettingsWindow(shell);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Fixed_rows_disappear_a_fully_fixed_group_disappears_and_the_page_names_the_company()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = Open(f, new FakeSource(new()
        {
            ["engine"] = ManagedLevel.Fixed,
            ["language"] = ManagedLevel.Fixed,
            ["stt.providerId"] = ManagedLevel.Fixed,
        }, []));

        var page = SettingsUi.Show(window, "recognition");
        var groups = window.GetVisualDescendants().OfType<SettingsGroup>().ToList();
        Assert.False(groups.Single(g => g.TitleKey == "G_Engine").IsShown); // nur feste Zeilen
        Assert.True(groups.Single(g => g.TitleKey == "G_Online").IsShown);
        Assert.False(SettingsUi.Row(window, "stt.providerId").IsShown);
        Assert.True(SettingsUi.Row(window, "stt.model").IsShown);
        Assert.True(page.HasManagedRows);
        Assert.Equal("Einige Einstellungen werden von Beispiel GmbH verwaltet.", page.ManagedText);

        Assert.False(SettingsUi.Show(window, "general").HasManagedRows);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Default_rows_stay_visible_marked_and_can_be_reset_to_the_company_value()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Settings.Stt.Model = "eigene-wahl";
        var window = Open(f, new FakeSource(new() { ["stt.model"] = ManagedLevel.Default }, new() { ["stt.model"] = JsonValue.Create("whisper-1") }));

        var page = SettingsUi.Show(window, "recognition");
        var row = SettingsUi.Row(window, "stt.model");
        Assert.True(row.IsShown);
        Assert.Equal(ManagedLevel.Default, row.Level);
        Assert.Contains(":default", row.Classes);
        Assert.Equal("Firmenvorgabe", row.BadgeText);
        Assert.False(page.HasManagedRows); // Vorgabe ist nicht „verwaltet“ im Sinne von ausgeblendet

        await window.Shell!.ResetToDefaultAsync("stt.model");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("whisper-1", f.Controller.Settings.Stt.Model);
        Assert.Equal("whisper-1", window.Settings!.SttModel);
        Assert.Equal("recognition", window.Shell.SelectedPage!.Id);
        window.Close();
    }

    [AvaloniaFact]
    public void Free_rows_have_no_mark_and_hotkeys_are_never_fixed()
    {
        using var f = new ControllerFixture();
        var source = new PolicyManagedSource(new PolicySettings { Locked = ["desktop"] });
        Assert.Equal(ManagedLevel.Fixed, source.LevelOf("desktop.proxyUrl"));
        Assert.Equal(ManagedLevel.Free, source.LevelOf("desktop.hotkeyHold"));
        Assert.Equal(ManagedLevel.Free, source.LevelOf("desktop.hotkeysEnabled"));
        Assert.Null(new PolicyManagedSource(PolicySettings.None).ManagerName);

        var window = Open(f, new PolicyManagedSource(PolicySettings.None));
        SettingsUi.Show(window, "network");
        Assert.All(SettingsUi.Rows(window), r => Assert.Equal(ManagedLevel.Free, r.Level));
        Assert.All(SettingsUi.Rows(window), r => Assert.DoesNotContain(":default", r.Classes));
        window.Close();
    }
}
