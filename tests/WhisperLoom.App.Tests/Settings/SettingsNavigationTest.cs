using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>Abnahme W2-UI1 Nr. 2: Tastatur (Tab, Pfeile, Esc, Strg+F), Suche, Accessible Names, Hell/Dunkel nach System.</summary>
public sealed class SettingsNavigationTest
{
    private static SettingsWindow Open(ControllerFixture f)
    {
        SettingsShellViewModel.LastPage = "dictation";
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IInputElement? Focused(Window w) => w.FocusManager?.GetFocusedElement();

    [AvaloniaFact]
    public void Arrow_keys_in_the_sidebar_switch_pages()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        var list = window.FindControl<ListBox>("PageList")!;
        (list.ContainerFromIndex(0) as InputElement)!.Focus(NavigationMethod.Tab);

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Equal("recognition", window.Shell!.SelectedPage!.Id);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        Assert.Equal("recognition", window.Shell.SelectedPage!.Id);
        Dispatcher.UIThread.RunJobs(); // PERF-2: Seite folgt einen Durchlauf nach der Markierung
        Assert.IsType<RecognitionPageViewModel>(window.FindControl<ContentControl>("PageHost")!.Content);
        window.Close();
    }

    [AvaloniaFact]
    public void Reopening_shows_the_last_viewed_page()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        Assert.Equal("dictation", window.Shell!.SelectedPage!.Id);
        SettingsUi.Show(window, "privacy");
        window.Close();

        var again = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        Assert.Equal("privacy", again.Shell!.SelectedPage!.Id);
        SettingsShellViewModel.LastPage = "dictation";
    }

    [AvaloniaFact]
    public void Tab_moves_from_search_to_sidebar_to_the_page()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
        Assert.Same(window.FindControl<TextBox>("SearchBox"), Focused(window));

        // TS-WKLEIN: „Hilfe“ ist der erste Eintrag der Seitenleiste und damit der nächste Tab-Stopp.
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.Same(window.FindControl<Button>("HelpNavButton"), Focused(window));

        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        var sidebar = window.FindControl<ListBox>("PageList")!;
        Assert.True(Focused(window) is Visual v && (v == sidebar || v.FindAncestorOfType<ListBox>() == sidebar), $"Fokus: {Focused(window)}");

        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        var host = window.FindControl<ContentControl>("PageHost")!;
        Assert.True(Focused(window) is Visual p && p.FindAncestorOfType<ContentControl>(true) is not null && host.IsVisualAncestorOf(p), $"Fokus: {Focused(window)}");
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_clears_the_search_first_and_then_closes()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Shell!.SearchText = "proxy";

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal("", window.Shell.SearchText);
        Assert.False(closed);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Search_finds_settings_on_every_page_and_jumps_to_the_row()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = Open(f);
        var shell = window.Shell!;

        shell.SearchText = "Proxy-Adresse";
        var hit = Assert.Single(shell.Results);
        Assert.Equal(("desktop.proxyUrl", "Verbindungen › Proxy"), (hit.Entry.Path, hit.Location));

        // Treffer hinter „Erweitert“: Seite wechselt, „Erweitert“ klappt auf, das Feld bekommt den Fokus.
        shell.SearchText = "Zeitlimit";
        var timeout = Assert.Single(shell.Results);
        shell.OpenCommand.Execute(timeout);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("recognition", shell.SelectedPage!.Id);
        var row = SettingsUi.Row(window, "stt.readTimeoutSec");
        Assert.True(Avalonia.LogicalTree.LogicalExtensions.FindLogicalAncestorOfType<Expander>(row)!.IsExpanded);
        Assert.True(Focused(window) is Visual focused && row.IsVisualAncestorOf(focused));

        // Mehrere Wörter, andere Sprache.
        Loc.Instance.SetLanguage("en");
        shell.SearchText = "base url";
        Assert.Equal(4, shell.Results.Count);
        shell.SearchText = "gibtesnicht";
        Assert.Empty(shell.Results);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<TextBlock>("NoResults")!.IsEffectivelyVisible);
        Loc.Instance.SetLanguage("de");
        window.Close();
    }

    [AvaloniaFact]
    public void Search_hides_fixed_settings()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        f.Controller.Policy = new PolicySettings { Locked = ["desktop.proxyUrl"] };
        var window = Open(f);
        window.Shell!.SearchText = "Proxy";
        Assert.DoesNotContain(window.Shell.Results, r => r.Entry.Path == "desktop.proxyUrl");
        Assert.Contains(window.Shell.Results, r => r.Entry.Path == "desktop.proxyMode");
        window.Close();
    }

    [AvaloniaFact]
    public void Every_visible_control_has_an_accessible_name()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = Open(f);
        var missing = new List<string>();
        SettingsUi.VisitAll(window, page =>
        {
            foreach (var c in window.GetVisualDescendants().OfType<TemplatedControl>()
                         .Where(c => c is TextBox or ComboBox or ToggleSwitch or CheckBox or Button or ListBox && c.IsEffectivelyVisible && c.Focusable)
                         .Where(c => c.FindAncestorOfType<ComboBox>() is null && c.TemplatedParent is null))
            {
                var name = AutomationProperties.GetName(c);
                var content = (c as ContentControl)?.Content as string;
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(content))
                {
                    missing.Add($"{page}: {c.GetType().Name} {c.Name}");
                }
            }
        });
        Assert.Empty(missing);
        window.Close();
    }

    [AvaloniaFact]
    public void Theme_follows_the_system_and_changes_live()
    {
        using var f = new ControllerFixture();
        var platform = new FakeTheme(PlatformThemeVariant.Light);
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        SystemTheme.Follow(window, platform);
        window.Show();
        Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
        Assert.True(window.TryFindResource("Loom.Surface", window.ActualThemeVariant, out var light));

        platform.Switch(PlatformThemeVariant.Dark);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        Assert.True(window.TryFindResource("Loom.Surface", window.ActualThemeVariant, out var dark));
        Assert.NotEqual(((Avalonia.Media.ISolidColorBrush)light!).Color, ((Avalonia.Media.ISolidColorBrush)dark!).Color);
        window.Close();
    }

    /// <summary>System-Hell/Dunkel zum Umschalten.</summary>
    private sealed class FakeTheme(PlatformThemeVariant variant) : IThemeSource
    {
        public event Action? Changed;

        public PlatformThemeVariant Current { get; private set; } = variant;

        public void Switch(PlatformThemeVariant v)
        {
            Current = v;
            Changed?.Invoke();
        }
    }
}
