using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhisperLoom.App.Setup;
using WhisperLoom.App.ViewModels.Settings;

namespace WhisperLoom.App.Views.Help;

/// <summary>
/// Einstieg in die Hilfe von überall: „?“ der Einstellungsseiten, Assistent, Tray und F1. Die App setzt <see cref="Handler"/>
/// (öffnet bzw. holt das Hilfe-Fenster nach vorn); <see cref="Install"/> hängt F1 an jedes Fenster und wählt den
/// Abschnitt nach dem Kontext des Fensters.
/// </summary>
public static class HelpLauncher
{
    private static bool _installed;

    /// <summary>Öffnet die Hilfe beim Abschnitt (Id aus <see cref="HelpCatalog"/>); null = Hilfe nicht verfügbar.</summary>
    public static Action<string>? Handler { get; set; }

    public static void Open(string topic) => Handler?.Invoke(topic);

    /// <summary>F1 in jedem Fenster der App öffnet die Hilfe zum Kontext (einmalig; weitere Aufrufe tun nichts).</summary>
    // Doku: docs.avaloniaui.net/docs/concepts/input/routed-events (Klassen-Handler, abgerufen 2026-11-01).
    public static void Install()
    {
        if (_installed)
        {
            return;
        }
        _installed = true;
        InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (e.Key != Key.F1 || e.KeyModifiers != KeyModifiers.None || window is HelpWindow || e.Handled)
        {
            return;
        }
        e.Handled = true;
        Open(TopicFor(window));
    }

    /// <summary>Abschnitt zum Fenster: Einstellungen → Seite, Assistent → Schritt, Verlauf, Bühne; sonst Übersicht.</summary>
    public static string TopicFor(Window window) => window switch
    {
        SettingsWindow { Shell.SelectedPage: { } page } => HelpCatalog.TopicForPage(page.Id),
        SetupWindow { DataContext: SetupViewModel setup } => HelpCatalog.TopicForSetup(setup.Current),
        HistoryWindow => "history",
        StageWindow => "dictation",
        _ => HelpCatalog.Overview,
    };

    /// <summary>Für den „?“-Knopf einer Einstellungsseite.</summary>
    public static void OpenForPage(SettingsPageViewModel? page) => Open(HelpCatalog.TopicForPage(page?.Id));
}
