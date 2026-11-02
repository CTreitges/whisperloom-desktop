using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.ViewModels.Settings;

namespace WhisperLoom.App.Views.Settings;

/// <summary>
/// Vorlage des Seiten-Hosts im Einstellungsfenster (PERF-2): baut je Seiten-ViewModel die View einmal und gibt bei jedem
/// Wechsel dieselbe Instanz zurück. Vorher entstand die Seite bei jedem Klick in der Seitenleiste neu (Messung: „Erkennung“
/// 118 ms bis Ruhe je Wechsel, erstes Öffnen 317 ms mit 158 ms UI-Blockade). Die eigentlichen Vorlagen stehen weiter in
/// <c>Window.DataTemplates</c> von <see cref="SettingsWindow"/>.
/// </summary>
// Doku: Context7 /avaloniaui/avalonia-docs, docs/data-templates/creating-data-templates-in-code.md (abgerufen 2026-11-02).
public sealed class PageViewCache(Control owner, Panel staging) : IDataTemplate
{
    private readonly Dictionary<SettingsPageViewModel, Control> _views = [];

    /// <summary>Anzahl gebauter Seiten (Tests).</summary>
    public int Count => _views.Count;

    public bool Match(object? data) => data is SettingsPageViewModel;

    public Control? Build(object? param)
    {
        if (param is not SettingsPageViewModel page)
        {
            return null;
        }
        if (!_views.TryGetValue(page, out var view))
        {
            view = owner.DataTemplates.First(t => t.Match(page)).Build(page) ?? new TextBlock { Text = page.Id };
            // Nur die gezeigte Seite ist gebunden: Eine abgehängte Seite mit aktiven Two-Way-Bindings schriebe sonst null
            // zurück, sobald eine Auswahl-Liste ausgetauscht wird (z. B. ComboBox Anbieter/Modell nach „Anbieter“-Änderung).
            view.AttachedToLogicalTree += (_, _) => view.DataContext = page;
            view.DetachedFromLogicalTree += (_, _) => view.DataContext = null;
            _views[page] = view;
        }
        else if (view.Parent == staging)
        {
            // Klick auf eine Seite, deren Vorbau gerade läuft: aus dem Staging lösen, der Host hängt sie an.
            staging.Children.Remove(view);
        }
        return view;
    }

    /// <summary>
    /// Vorbau einer Seite in kleinen Schritten (je Schritt ein Dispatcher-Durchlauf, damit keiner den UI-Thread lange hält):
    /// bauen → in <see cref="Panel"/> <c>staging</c> anhängen (Styles, Bindings) → je <see cref="SettingsGroup"/> messen
    /// (Templates) → ganz messen, anordnen und wieder abhängen. Danach kostet das erste Zeigen nur noch das Anhängen.
    /// Bereits gebaute Seiten (z. B. schon angeklickt) überspringt er.
    /// </summary>
    public IEnumerable<Action> PrepareSteps(SettingsPageViewModel page, Func<Size> size)
    {
        if (IsBuilt(page))
        {
            yield break;
        }
        Control? view = null;
        yield return () => view = Build(page);
        if (view is not { Parent: null })
        {
            yield break;
        }
        yield return () =>
        {
            // Vor dem Anhängen setzen: sonst erbt die View kurz den DataContext des Fensters (Hülle) aus dem Staging-Panel.
            view.DataContext = page;
            staging.Children.Add(view);
        };
        foreach (var group in view.GetLogicalDescendants().OfType<SettingsGroup>().Cast<Layoutable>().ToList())
        {
            yield return () =>
            {
                if (view.Parent == staging)
                {
                    group.Measure(size());
                }
            };
        }
        yield return () =>
        {
            if (view.Parent == staging)
            {
                staging.Measure(size());
                staging.Arrange(new Rect(size()));
                staging.Children.Remove(view);
            }
        };
    }

    public bool IsBuilt(SettingsPageViewModel page) => _views.ContainsKey(page);

    /// <summary>Neue Seiten-ViewModels (Zurücksetzen, Import, Firmenwechsel): alte Views verwerfen, Staging leeren.</summary>
    public void Clear()
    {
        _views.Clear();
        staging.Children.Clear();
    }
}
