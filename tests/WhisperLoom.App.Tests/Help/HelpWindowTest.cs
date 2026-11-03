using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Views.Help;

namespace WhisperLoom.App.Tests.Help;

/// <summary>Hilfe-Fenster (W2-HELP Nr. 4): Seitenleiste, Suche, Tastatur, Sprache, hell/dunkel, Links.</summary>
public sealed class HelpWindowTest
{
    private static HelpWindow Open(string? topic = null)
    {
        Loc.Instance.SetLanguage("de");
        var window = new HelpWindow(new HelpViewModel());
        window.Show();
        if (topic is not null)
        {
            window.ShowTopic(topic);
        }
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IInputElement? Focused(Window w) => w.FocusManager?.GetFocusedElement();

    private static T Named<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public void Opens_on_the_overview_with_what_do_you_want_to_do()
    {
        var window = Open();
        Assert.True(window.Help!.SelectedSection!.IsOverview);
        // UX-3: sieben Aufgaben als ganze Sätze mit dem Abschnitt dahinter statt Kacheln aller Abschnitte.
        Assert.Equal(HelpCatalog.Tasks, window.Help.Overview.Tasks.Select(t => t.Section.Id));
        Assert.All(window.Help.Overview.Tasks, t => Assert.False(t.Text.StartsWith('!')));
        var tasks = Named<ItemsControl>(window, "TaskList").GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(HelpCatalog.Tasks.Count, tasks.Count);
        Assert.Equal(Loc.Instance["Help_Task_hotkeys"], AutomationProperties.GetName(tasks[1]));

        tasks.First(t => t.DataContext is HelpTaskView { Section.Id: "trouble" }).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("trouble", window.Help.SelectedSection!.Id);
        window.Close();
    }

    [AvaloniaFact]
    public void Section_shows_illustration_text_screenshots_and_legend()
    {
        var window = Open("recognition");
        Assert.Equal(Loc.Instance["Help_recognition_Title"], Named<TextBlock>(window, "SectionTitle").Text);
        var ill = Named<HelpIllustration>(window, "Illustration");
        Assert.Equal(HelpCatalog.Find("recognition")!.Illustration, ill.Source);
        Assert.Equal(Loc.Instance["HelpImg_recognition"], AutomationProperties.GetName(ill));
        var shot = window.GetVisualDescendants().OfType<HelpScreenshot>().Single();
        Assert.NotNull(shot.Source);
        Assert.Equal(Loc.Instance["HelpShot_recognition"], AutomationProperties.GetName(shot));
        var legend = window.Help!.SelectedSection!.Shots.Single().Legend;
        Assert.Equal([1, 2, 3, 4], legend.Select(l => l.Number));
        window.Close();
    }

    [AvaloniaFact]
    public void Search_finds_sections_and_keyboard_moves_into_results()
    {
        var window = Open();
        window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
        var search = Named<TextBox>(window, "SearchBox");
        Assert.Same(search, Focused(window));

        window.Help!.SearchText = "Proxy";
        Assert.Contains(window.Help.Results, r => r.Section.Id == "network");
        Assert.Contains(window.Help.Results, r => r.Section.Id == "it");
        Assert.All(window.Help.Results, r => Assert.False(string.IsNullOrWhiteSpace(r.Snippet)));

        Assert.StartsWith(window.Help.Results.Count + " Treffer", window.Help.SearchSummary, StringComparison.Ordinal);
        Dispatcher.UIThread.RunJobs();
        // UX-3: Treffer rechts als Knöpfe; ↓ springt in die Treffer, ↑ vom ersten zurück in die Suche, Enter/Klick öffnet.
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        var buttons = window.ResultButtons;
        Assert.Same(buttons[0], Focused(window));
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Same(buttons[1], Focused(window));
        window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        Assert.Same(search, Focused(window));

        // Esc leert erst die Suche, das nächste Esc schließt.
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal("", window.Help.SearchText);
        Assert.False(closed);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Search_needs_all_words_and_shows_nothing_without_match()
    {
        var window = Open();
        window.Help!.SearchText = "Firmencode Freigabe";
        Assert.Contains(window.Help.Results, r => r.Section.Id == "company");
        Assert.DoesNotContain(window.Help.Results, r => r.Section.Id == "stages");
        window.Help.SearchText = "xyzzy-gibt-es-nicht";
        Assert.Empty(window.Help.Results);
        Assert.True(Named<TextBlock>(window, "NoResults").IsEffectivelyVisible);
        Assert.StartsWith("Keine Treffer für „xyzzy-gibt-es-nicht“", window.Help.SearchSummary, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Arrow_keys_in_the_sidebar_switch_sections()
    {
        var window = Open();
        var list = Named<ListBox>(window, "SectionList");
        (list.ContainerFromIndex(0) as InputElement)!.Focus(NavigationMethod.Tab);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Equal(HelpCatalog.Topics[0].Id, window.Help!.SelectedSection!.Id);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Equal(HelpCatalog.Topics[1].Id, window.Help.SelectedSection!.Id);
        // Accessible Names der Seitenleiste = Titel.
        Assert.Equal(window.Help.SelectedSection.Title, AutomationProperties.GetName((Control)list.ContainerFromIndex(2)!));
        window.Close();
    }

    [AvaloniaFact]
    public void Unknown_topic_falls_back_to_the_overview_and_ends_search()
    {
        var window = Open("history");
        window.Help!.SearchText = "Verlauf";
        window.ShowTopic("gibt-es-nicht");
        Assert.True(window.Help.SelectedSection!.IsOverview);
        Assert.False(window.Help.IsSearching);
        window.Help.SelectedSection = null;
        Assert.True(window.Help.SelectedSection!.IsOverview);
        window.Close();
    }

    [AvaloniaFact]
    public void Language_switch_updates_texts_and_screenshot()
    {
        var window = Open("output");
        var shot = window.GetVisualDescendants().OfType<HelpScreenshot>().First();
        Assert.Contains("/de-", shot.CurrentPath);
        Loc.Instance.SetLanguage("en");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Output and clipboard", Named<TextBlock>(window, "SectionTitle").Text);
        // Die Liste der Screenshots wird mit der Sprache neu aufgebaut: das gezeigte Bild ist das englische.
        window.UpdateLayout();
        var shown = window.GetVisualDescendants().OfType<HelpScreenshot>().ToList();
        Assert.NotEmpty(shown);
        Assert.All(shown, s => Assert.Contains("/en-", s.CurrentPath));
        Assert.Equal("Features", window.Help!.Section("selection").GroupTitle);
        Loc.Instance.SetLanguage("de");
        window.Close();
    }

    [AvaloniaFact]
    public void Light_and_dark_use_their_own_colors_and_screenshots()
    {
        var window = Open("privacy");
        var ill = Named<HelpIllustration>(window, "Illustration");
        var shot = window.GetVisualDescendants().OfType<HelpScreenshot>().First();
        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var light = ((ISolidColorBrush)ill.Brush("Accent")!).Color;
        Assert.Contains("-light/", shot.CurrentPath);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(light, ((ISolidColorBrush)ill.Brush("Accent")!).Color);
        Assert.Contains("-dark/", shot.CurrentPath);
        Assert.Null(ill.Brush("GibtEsNicht"));
        // Hochkontrast erbt von Dunkel: dunkle Bilder, nicht helle auf schwarzer Oberfläche.
        window.RequestedThemeVariant = WhisperLoom.App.Design.SystemTheme.HighContrast;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("-dark/", shot.CurrentPath);
        window.Close();
    }

    [AvaloniaFact]
    public void Links_open_through_the_launcher()
    {
        var window = Open("it");
        var opened = new List<Uri>();
        window.OpenUri = uri =>
        {
            opened.Add(uri);
            return Task.CompletedTask;
        };
        var links = window.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is HelpLinkView).ToList();
        Assert.Equal(3, links.Count);
        foreach (var link in links)
        {
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Assert.Contains(opened, u => u.AbsolutePath.EndsWith("/server/README.md", StringComparison.Ordinal));
        Assert.Contains(opened, u => u.AbsolutePath.EndsWith("/docs/provisioning.md", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void Screenshot_releases_its_bitmap_when_removed()
    {
        // Review W2-HELP: alte Screenshots hielten ihre dekodierte PNG bis zum GC (Abschnittswechsel baut sie neu).
        var window = Open("output");
        var shot = window.GetVisualDescendants().OfType<HelpScreenshot>().First();
        Assert.NotNull(shot.Source);
        window.ShowTopic("privacy");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(shot.Source);
        Assert.Null(shot.CurrentPath);
        // Nicht im Baum: nichts laden.
        var loose = new HelpScreenshot { Shot = "output" };
        Assert.Null(loose.Source);
        window.Close();
    }

    [AvaloniaFact]
    public void Language_switch_refreshes_each_section_once()
    {
        // Review W2-HELP: Loc meldet drei Ereignisse je Wechsel; der Abschnitt baute sich dreimal neu.
        Loc.Instance.SetLanguage("de");
        using var help = new HelpViewModel();
        var refreshes = 0;
        help.Section("output").PropertyChanged += (_, e) => refreshes += e.PropertyName == "" ? 1 : 0;
        Loc.Instance.SetLanguage("en");
        Loc.Instance.SetLanguage("de");
        Assert.Equal(2, refreshes);
    }

    [AvaloniaFact]
    public void Illustration_resolves_brushes_and_pens_once_per_theme_and_source()
    {
        // PERF-4: Render zeichnet mit den einmal aufgelösten Pinseln/Stiften (vorher je Shape und Frame neu).
        var window = Open("privacy");
        var ill = Named<HelpIllustration>(window, "Illustration");
        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var light = ill.Paint();
        Assert.Same(light, ill.Paint());
        Assert.NotNull(window.CaptureRenderedFrame());
        Assert.Same(light, ill.Paint());
        Assert.Equal(ill.Shapes.Count, light.Length);
        Assert.Contains(light, p => p.Pen is not null);

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        var dark = ill.Paint();
        Assert.NotSame(light, dark);
        var i = Array.FindIndex(light, p => p.Fill is not null);
        Assert.NotEqual(((ISolidColorBrush)light[i].Fill!).Color, ((ISolidColorBrush)dark[i].Fill!).Color);

        // Geänderte Ressourcen (ohne Theme-Wechsel) lösen ebenfalls neu auf (Prüfer PR #33 N3).
        ill.Resources["Loom.Accent"] = Brushes.Red;
        var custom = ill.Paint();
        Assert.NotSame(dark, custom);
        Assert.Contains(custom, p => ReferenceEquals(p.Fill, Brushes.Red) || ReferenceEquals((p.Pen as Pen)?.Brush, Brushes.Red));

        ill.Source = HelpCatalog.Find("recognition")!.Illustration;
        Assert.NotSame(custom, ill.Paint());
        Assert.Equal(ill.Shapes.Count, ill.Paint().Length);
        window.Close();
    }

    [AvaloniaFact]
    public void Illustration_renders_without_errors_at_any_size()
    {
        var window = Open("start");
        var ill = Named<HelpIllustration>(window, "Illustration");
        Assert.True(ill.Bounds.Width > 0 && ill.Bounds.Height > 0);
        Assert.NotNull(window.CaptureRenderedFrame());
        ill.Source = null;
        Assert.Empty(ill.Shapes);
        window.Close();
    }
}
