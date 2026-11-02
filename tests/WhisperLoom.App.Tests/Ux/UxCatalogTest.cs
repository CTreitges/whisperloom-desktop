using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Die Liste der Ansichten ist vollständig: jedes Fenster und jede UserControl-Ansicht der App kommt in mindestens einer Szene
/// vor, jede Szene baut sich auf. Sonst würde eine neue Ansicht von keinem Ux-Test geprüft.
/// </summary>
public sealed class UxCatalogTest
{
    /// <summary>Fenster ohne eigene Szene, mit Grund.</summary>
    private static readonly Dictionary<string, string> NotInCatalog = new(StringComparer.Ordinal)
    {
    };

    [AvaloniaFact]
    public async Task Every_window_and_view_of_the_app_is_in_a_scene()
    {
        var before = Loc.Instance.Language;
        Loc.Instance.SetLanguage("de");
        var seen = new HashSet<Type>();
        try
        {
            foreach (var view in UxViews.All)
            {
                using var scene = await view.Build(ThemeVariant.Dark);
                UxStage.Settle(scene.Window);
                seen.Add(scene.Origin ?? scene.Window.GetType());
                foreach (var c in scene.Window.GetVisualDescendants())
                {
                    seen.Add(c.GetType());
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(before);
        }
        var app = typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (t.IsSubclassOf(typeof(Window)) || t.IsSubclassOf(typeof(UserControl))))
            .Where(t => !NotInCatalog.ContainsKey(t.Name))
            .ToList();
        // Fenster aus der Update-Nachfrage sind schlichte Window-Instanzen (Code), ihre Szene steht in der Liste.
        Assert.Empty(app.Where(t => !seen.Contains(t)).Select(t => t.FullName));
        Assert.Equal(UxViews.All.Count, UxViews.All.Select(v => v.Id).Distinct().Count());
    }
}
