using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.App.Views.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>PERF-2: Seiten-Views werden einmal gebaut, beim Wechsel wiederverwendet und nach dem Öffnen vorgebaut.</summary>
public sealed class PageViewCacheTest
{
    private static SettingsWindow Open(ControllerFixture f)
    {
        SettingsShellViewModel.LastPage = "general";
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Wechsel_zurueck_zeigt_dieselbe_Seiten_View()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        var recognition = SettingsUi.Show(window, "recognition");
        SettingsUi.Show(window, "general");
        var again = SettingsUi.Show(window, "recognition");
        Assert.Same(recognition, again);
        Assert.Same(window.Shell!.Page("recognition"), again.DataContext);
        window.Close();
    }

    [AvaloniaFact]
    public void Nach_dem_Oeffnen_sind_alle_Seiten_vorgebaut()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        Assert.Equal(window.Shell!.Pages.Count, window.Pages.Count);
        Assert.All(window.Shell.Pages, p => Assert.True(window.Pages.IsBuilt(p)));
        // Nur die gezeigte Seite ist gebunden; vorgebaute bzw. verlassene Seiten schreiben nichts in die Arbeitskopie zurück.
        Assert.All(window.Shell.Pages.Where(p => p != window.Shell.SelectedPage), p => Assert.Null(window.Pages.Build(p)!.DataContext));
        // Vorbau inkl. Templates (erstes Anhängen und Messen), danach wieder abgehängt: nichts bleibt im Staging-Panel.
        var engine = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window.Pages.Build(window.Shell.Page("recognition"))!)
            .OfType<ComboBox>().Single(c => c.Name == "EngineBox");
        Assert.NotNull(engine.Template);
        Assert.NotEmpty(Avalonia.VisualTree.VisualExtensions.GetVisualChildren(engine));
        Assert.Empty(window.FindControl<Panel>("Staging")!.Children);
        SettingsUi.Show(window, "text");
        var general = window.Pages.Build(window.Shell.Page("general"))!;
        Assert.Null(general.DataContext);
        Assert.Same(window.Shell.Page("text"), window.Pages.Build(window.Shell.Page("text"))!.DataContext);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Neue_Seiten_ViewModels_bekommen_neue_Views()
    {
        // Zurücksetzen auf die Firmenvorgabe baut die Hülle neu (neue Seiten-ViewModels): keine alte View weiterzeigen.
        Localization.Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Policy = new PolicySettings
        {
            Defaults = new Dictionary<string, System.Text.Json.JsonElement> { ["stt.model"] = System.Text.Json.JsonSerializer.SerializeToElement("gpt-transcribe") },
        };
        var window = Open(f);
        var before = SettingsUi.Show(window, "recognition");
        var oldPage = window.Shell!.Page("recognition");
        window.Settings!.SttModel = "eigene-wahl";
        await window.Settings.Pending;

        await window.Shell.ResetToDefaultAsync("stt.model");
        Dispatcher.UIThread.RunJobs();

        var after = SettingsUi.Show(window, "recognition");
        Assert.NotSame(oldPage, window.Shell.Page("recognition"));
        Assert.NotSame(before, after);
        Assert.Same(window.Shell.Page("recognition"), after.DataContext);
        Assert.False(window.Pages.IsBuilt(oldPage));
        window.Close();
    }

    [AvaloniaFact]
    public void Seite_folgt_einen_Durchlauf_nach_der_Markierung()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        var host = window.FindControl<ContentControl>("PageHost")!;
        window.Shell!.SelectedPage = window.Shell.Page("privacy");
        Assert.Same(window.Shell.Page("general"), host.Content); // erst rendert die Markierung …
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window.Shell.Page("privacy"), host.Content); // … dann hängt die Seite
        window.Close();
    }

    [AvaloniaFact]
    public void Vorlage_passt_nur_auf_Seiten()
    {
        var cache = new PageViewCache(new Window(), new Panel());
        Assert.False(cache.Match("text"));
        Assert.Null(cache.Build("text"));
    }

    [AvaloniaFact]
    public void Klick_waehrend_des_Vorbaus_holt_die_Seite_aus_dem_Staging()
    {
        using var f = new ControllerFixture();
        SettingsShellViewModel.LastPage = "general";
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show(); // Vorbau eingeplant, aber noch kein Schritt gelaufen
        var staging = window.FindControl<Panel>("Staging")!;
        var page = window.Shell!.Page("recognition");
        using (var steps = window.Pages.PrepareSteps(page, () => new Avalonia.Size(600, 600)).GetEnumerator())
        {
            Assert.True(steps.MoveNext());
            steps.Current(); // bauen
            Assert.True(steps.MoveNext());
            steps.Current(); // im Staging angehängt
        }
        Assert.Single(staging.Children);

        var shown = SettingsUi.Show(window, "recognition");
        Assert.Empty(staging.Children);
        Assert.Same(page, shown.DataContext);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Seiten_wechseln_aendert_keine_Einstellung()
    {
        // Abgehängte Seiten verlieren den DataContext: Auswahl-Steuerelemente dürfen dabei nichts in die Arbeitskopie schreiben.
        Localization.Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var before = System.Text.Json.JsonSerializer.Serialize(f.Controller.Settings);
        var window = Open(f);
        foreach (var round in Enumerable.Range(0, 2))
        {
            SettingsUi.VisitAll(window, _ => { });
        }
        await window.Settings!.Pending;
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(f.Controller.Settings));
        window.Close();
    }
}
