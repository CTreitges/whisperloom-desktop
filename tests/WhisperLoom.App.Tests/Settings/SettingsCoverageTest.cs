using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// Abnahme W2-UI1 Nr. 1: Jeder Settings-Pfad (außer <c>management.*</c>) ist auf genau einer Seite gebunden oder steht
/// mit Grund auf der Ausnahmeliste. „Gebunden“ = eine <see cref="Design.SettingRow"/> mit diesem Pfad in der Seiten-View.
/// </summary>
public sealed class SettingsCoverageTest
{
    /// <summary>Alle Blätter der Einstellungen inkl. Abschnitte <c>desktop</c> und <c>local</c>; Modell je Stufe einzeln.</summary>
    internal static List<string> AllPaths()
    {
        var s = new WhisperLoomSettings { Engine = "online" };
        SettingsStore.SetDesktop(s, new DesktopSettings());
        LocalSettings.Set(s, new LocalSettings());
        foreach (var stage in RefineModes.ModelStages)
        {
            s.Llm.StageModels[stage.Key()] = "x";
        }
        return [.. SettingsMerge.Leaves(SettingsMerge.ToJson(s)).Keys.Where(p => !p.StartsWith("management.", StringComparison.Ordinal))];
    }

    /// <summary>Pfad → Seiten, auf denen eine Zeile ihn bindet.</summary>
    private static Dictionary<string, List<string>> BoundPaths()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        var bound = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        SettingsUi.VisitAll(window, page =>
        {
            var paths = SettingsUi.Rows(window).Where(r => r.Path is not null).Select(r => r.Path!).ToList();
            if (page == "hotkeys" && !OperatingSystem.IsWindows())
            {
                // Unter Linux ist die Gruppe ausgeblendet und die Vorlage (ein Template je Aktion) wird nicht erzeugt.
                paths.AddRange(window.Settings!.Hotkeys.All.Select(r => r.Path));
            }
            foreach (var path in paths.Distinct())
            {
                (bound.TryGetValue(path, out var pages) ? pages : bound[path] = []).Add(page);
            }
        });
        window.Close();
        return bound;
    }

    [AvaloniaFact]
    public void Every_setting_is_bound_on_exactly_one_page_or_listed_as_exception()
    {
        var bound = BoundPaths();
        var problems = new List<string>();
        foreach (var path in AllPaths())
        {
            var excepted = SettingsCatalog.Exceptions.ContainsKey(path);
            var pages = bound.GetValueOrDefault(path) ?? [];
            if (excepted && pages.Count > 0)
            {
                problems.Add($"{path}: Ausnahme, aber gebunden auf {string.Join(", ", pages)}");
            }
            else if (!excepted && pages.Count != 1)
            {
                problems.Add($"{path}: auf {pages.Count} Seiten gebunden ({string.Join(", ", pages)})");
            }
        }
        Assert.Empty(problems);
    }

    [AvaloniaFact]
    public void Every_catalog_entry_is_bound_on_its_own_page_and_exceptions_have_a_reason()
    {
        var bound = BoundPaths();
        Assert.All(SettingsCatalog.Entries, e => Assert.Equal([e.Page], bound.GetValueOrDefault(e.Path) ?? []));
        Assert.All(SettingsCatalog.Exceptions, e => Assert.False(string.IsNullOrWhiteSpace(e.Value), e.Key));
        // Keine Leiche im Katalog: jeder Eintrag ist ein echter Pfad oder ein Key (secrets.*).
        var paths = AllPaths();
        Assert.All(SettingsCatalog.Entries, e => Assert.True(paths.Contains(e.Path) || e.Path.StartsWith("secrets.", StringComparison.Ordinal), e.Path));
    }

    [Fact]
    public void Sidebar_has_at_most_eleven_pages_in_sections()
    {
        // W2-LOCAL: „Anbieter“ und „Lokale Modelle“ im Abschnitt „Quellen“ (task.md verlangt eigene Seiten); Platz für „Firma“ (W2-ENR).
        Assert.InRange(SettingsCatalog.Pages.Count, 1, 11);
        Assert.Equal(["dictation", "sources", "usage", "system"], SettingsCatalog.Pages.Select(p => p.Section).Distinct());
        Assert.All(SettingsCatalog.Entries, e => Assert.Contains(SettingsCatalog.Pages, p => p.Id == e.Page));
    }

    [Fact]
    public void All_paths_include_the_former_file_only_settings()
    {
        // Vorher nur per Datei: polish.*, refine.polishCleanup/summarizeForm/paragraphs*, Bühnen-Schrift, Ollama-Adresse.
        string[] fileOnly = ["polish.removeFillers", "polish.autoCapitalize", "polish.trailingSpace", "polish.customFillers", "polish.disabledFillers",
            "refine.polishCleanup", "refine.summarizeForm", "refine.paragraphsPolish", "refine.paragraphsBeautify",
            "desktop.stageFontSize", "local.ollamaUrl", "local.idleMinutes", "stt.readTimeoutSec"];
        Assert.All(fileOnly, p => Assert.NotNull(SettingsCatalog.Find(p)));
    }
}
