using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>W3-PERF: Restbefunde zum Seiten-Cache (N6 Fokus, N7 Automation-Baum, N10 Keys einmal laden, Hinweis DataContext).</summary>
public sealed class SettingsPagesFollowUpTest
{
    private static SettingsWindow Open(ControllerFixture f, ISecretStore? secrets = null, bool runJobs = true)
    {
        SettingsShellViewModel.LastPage = "dictation";
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, secrets ?? new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        if (runJobs)
        {
            Dispatcher.UIThread.RunJobs();
        }
        return window;
    }

    [AvaloniaFact]
    public void N6_Pfeil_und_sofort_Tab_verliert_den_Fokus_nicht()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        var list = window.FindControl<ListBox>("PageList")!;
        var host = window.FindControl<ContentControl>("PageHost")!;
        (list.ContainerFromIndex(0) as InputElement)!.Focus(NavigationMethod.Tab);

        // ↓ wechselt die Auswahl, die Seite folgt erst einen Durchlauf später; Tab kommt vorher an (schnelle Tastenfolge).
        // Direkt geroutet statt window.KeyPress: Das arbeitet den Dispatcher nach jeder Taste ab und verdeckte den Fall.
        Press(window, Key.Down);
        Assert.IsType<DictationPageViewModel>(host.Content);
        Press(window, Key.Tab);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<RecognitionPageViewModel>(host.Content);
        var focused = window.FocusManager?.GetFocusedElement() as Visual;
        Assert.NotNull(focused);
        Assert.True(focused.IsAttachedToVisualTree(), $"Fokus hängt an keiner Seite: {focused}");
        Assert.True(list.IsVisualAncestorOf(focused) || focused == list || host.IsVisualAncestorOf(focused), $"Fokus: {focused}");
        window.Close();
    }

    private static void Press(Window window, Key key)
    {
        var target = window.FocusManager?.GetFocusedElement() as Interactive ?? window;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
    }

    [AvaloniaFact]
    public void N6_Klick_ohne_Fokus_in_der_alten_Seite_laesst_den_Fokus_stehen()
    {
        using var f = new ControllerFixture();
        var window = Open(f);
        var search = window.FindControl<TextBox>("SearchBox")!;
        search.Focus(NavigationMethod.Tab);
        SettingsUi.Show(window, "privacy");
        Assert.Same(search, window.FocusManager?.GetFocusedElement());
        window.Close();
    }

    [AvaloniaFact]
    public void N7_Vorgebaute_Seite_ist_nicht_im_Automation_Baum()
    {
        using var f = new ControllerFixture();
        var window = Open(f, runJobs: false); // Vorbau eingeplant, noch kein Schritt gelaufen
        var staging = window.FindControl<Panel>("Staging")!;
        var page = window.Shell!.Page("recognition");
        using (var steps = window.Pages.PrepareSteps(page, () => new Size(600, 600)).GetEnumerator())
        {
            Assert.True(steps.MoveNext());
            steps.Current(); // bauen
            Assert.True(steps.MoveNext());
            steps.Current(); // im Staging angehängt
        }
        var staged = Assert.Single(staging.Children);

        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(staging));
        var peers = Peers(ControlAutomationPeer.CreatePeerForElement(window)).OfType<ControlAutomationPeer>().ToList();
        var owners = peers.Select(p => p.Owner).ToList();
        Assert.Contains(window.FindControl<ListBox>("PageList")!, owners); // Gegenprobe: der Baum wird überhaupt gelesen
        Assert.DoesNotContain(owners, o => o == staged || staged.IsVisualAncestorOf(o));
        // Das Panel selbst bleibt nur im Raw-Baum: weder Control- noch Content-Element, ohne Kinder.
        var panel = ControlAutomationPeer.CreatePeerForElement(staging);
        Assert.False(panel.IsControlElement());
        Assert.False(panel.IsContentElement());
        Assert.Empty(panel.GetChildren());
        window.Close();
    }

    private static IEnumerable<AutomationPeer> Peers(AutomationPeer root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var deeper in Peers(child))
            {
                yield return deeper;
            }
        }
    }

    [AvaloniaFact]
    public async Task N10_Anbieter_Keys_werden_einmal_gelesen()
    {
        var s = new WhisperLoomSettings();
        s.Providers.Custom.Add(new CustomProvider { Id = "lab", Name = "Labor", BaseUrl = "https://lab.example/v1" });
        using var f = new ControllerFixture(s);
        var secrets = new CountingSecretStore();
        var window = Open(f, secrets);
        var name = SecretNames.ForProvider(ProvidersSettings.CustomPrefix + "lab");
        foreach (var round in Enumerable.Range(0, 3))
        {
            SettingsUi.Show(window, "connections");
            SettingsUi.Show(window, "dictation");
        }
        await window.Settings!.Pending;
        Assert.Equal(1, secrets.Reads.Count(r => r == name));
        window.Close();
    }

    [AvaloniaFact]
    public void Hinweis_Vorbau_erbt_nie_den_DataContext_des_Fensters()
    {
        using var f = new ControllerFixture();
        var window = Open(f, runJobs: false);
        var page = window.Shell!.Page("text");
        var seen = new List<object?>();
        using (var steps = window.Pages.PrepareSteps(page, () => new Size(600, 600)).GetEnumerator())
        {
            Assert.True(steps.MoveNext());
            steps.Current(); // bauen
            var view = window.Pages.Build(page)!;
            view.DataContextChanged += (_, _) => seen.Add(view.DataContext);
            while (steps.MoveNext())
            {
                steps.Current();
            }
        }
        Assert.NotEmpty(seen);
        Assert.All(seen, dc => Assert.True(dc is null || ReferenceEquals(dc, page), $"fremder DataContext: {dc}"));
        window.Close();
    }

    /// <summary>Zählt Lesezugriffe (N10).</summary>
    private sealed class CountingSecretStore : ISecretStore
    {
        private readonly MemorySecretStore _inner = new();

        public List<string> Reads { get; } = [];

        public string Kind => _inner.Kind;

        public Task<string?> GetAsync(string name, CancellationToken ct)
        {
            Reads.Add(name);
            return _inner.GetAsync(name, ct);
        }

        public Task SetAsync(string name, string value, CancellationToken ct) => _inner.SetAsync(name, value, ct);

        public Task DeleteAsync(string name, CancellationToken ct) => _inner.DeleteAsync(name, ct);
    }
}
