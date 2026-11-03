using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Views;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>Helfer für Headless-Tests des Einstellungsfensters (Seitenleiste statt Tabs, W2-UI1).</summary>
internal static class SettingsUi
{
    public static readonly IReadOnlyList<string> PageIds = ViewModels.Settings.SettingsCatalog.Pages;

    /// <summary>Wählt eine Seite und lässt das Layout laufen; Rückgabe = die gezeigte Seiten-View.</summary>
    public static SettingsPage Show(SettingsWindow window, string pageId)
    {
        window.Shell!.SelectedPage = window.Shell.Page(pageId);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window.FindControl<ContentControl>("PageHost")!.GetVisualDescendants().OfType<SettingsPage>().First();
    }

    /// <summary>Besucht alle Seiten nacheinander.</summary>
    public static void VisitAll(SettingsWindow window, Action<string> atPage)
    {
        foreach (var id in PageIds)
        {
            Show(window, id);
            atPage(id);
        }
    }

    /// <summary>Zeilen der gezeigten Seite (sichtbarer Baum, auch aus Templates).</summary>
    public static List<SettingRow> Rows(SettingsWindow window) =>
        [.. window.FindControl<ContentControl>("PageHost")!.GetVisualDescendants().OfType<SettingRow>()
            .Concat(Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window.FindControl<ContentControl>("PageHost")!).OfType<SettingRow>())
            .Distinct()];

    public static SettingRow Row(SettingsWindow window, string path) => Rows(window).Single(r => r.Path == path);

    /// <summary>Steuerelement nach x:Name; auch in ausgeblendeten Zeilen (UX-2: nur zeigen, was gilt), die keinen sichtbaren Baum haben.</summary>
    public static T Named<T>(SettingsWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window).OfType<T>().First(c => c.Name == name);
}
