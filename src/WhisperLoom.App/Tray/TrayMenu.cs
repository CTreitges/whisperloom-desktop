using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Features.Reinsert;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tray;

/// <summary>
/// Tray-Menü nach HIG (W2-UI1): Hauptaktion Start/Stopp oben, dann Gruppen durch Trenner – Stufe und letzte Diktate
/// (Untermenüs), Fenster (Bühne, Verlauf), Einstellungen/Einrichtung (mit „…“, öffnen ein Fenster), zuletzt Beenden. Texte folgen Zustand, Stufe und Oberflächensprache. Unter Linux exportiert Avalonia es als
/// StatusNotifierItem; zeigt der Desktop kein Tray, reichen Bühnenansicht und CLI.
/// </summary>
// Doku: docs.avaloniaui.net/docs/reference/controls/tray-icon (abgerufen 2026-10-30).
public sealed class TrayMenu
{
    private readonly DictationController _controller;
    private readonly Func<HistoryEntry, Task>? _onRecent;
    private readonly NativeMenuItem _toggle;
    private readonly NativeMenuItem _retry;
    private readonly NativeMenuItem _selection;
    private readonly NativeMenuItem _stage;
    private readonly NativeMenuItem _recent;
    private readonly NativeMenuItem _show;
    private readonly NativeMenuItem _history;
    private readonly NativeMenuItem _settings;
    private readonly NativeMenuItem _setup;
    private readonly NativeMenuItem _help;
    private readonly NativeMenuItem _quit;
    private IReadOnlyList<HistoryEntry> _entries = [];

    /// <param name="open">Fenster-Aktion: "stage", "history", "settings", "setup", "help", "quit".</param>
    /// <param name="onRecent">Klick auf einen der letzten Einträge: einfügen bzw. kopieren.</param>
    /// <param name="onSelection">„Markierung verarbeiten …“ (W2-SEL); null = Eintrag fehlt.</param>
    public TrayMenu(DictationController controller, Action<string> open, Func<HistoryEntry, Task>? onRecent = null, Func<Task>? onSelection = null)
    {
        _controller = controller;
        _onRecent = onRecent;
        _toggle = new NativeMenuItem { Command = new AsyncRelayCommand(() => controller.ToggleAsync()) };
        // „Erneut senden“ nach einem wiederholbaren Fehler (W2-OUT): nur sichtbar, solange Audio im Speicher liegt.
        _retry = new NativeMenuItem { Command = new AsyncRelayCommand(() => controller.RetryAsync()) };
        _selection = new NativeMenuItem { Command = new AsyncRelayCommand(() => onSelection?.Invoke() ?? Task.CompletedTask) };
        _stage = new NativeMenuItem { Menu = new NativeMenu() };
        _recent = new NativeMenuItem { Menu = new NativeMenu() };
        _show = new NativeMenuItem { Command = new RelayCommand(() => open("stage")) };
        _history = new NativeMenuItem { Command = new RelayCommand(() => open("history")) };
        _settings = new NativeMenuItem { Command = new RelayCommand(() => open("settings")) };
        _setup = new NativeMenuItem { Command = new RelayCommand(() => open("setup")) };
        _help = new NativeMenuItem { Command = new RelayCommand(() => open("help")) };
        _quit = new NativeMenuItem { Command = new RelayCommand(() => open("quit")) };
        Menu = new NativeMenu();
        foreach (var item in new NativeMenuItemBase[]
        {
            _toggle, new NativeMenuItemSeparator(),
            _stage, _recent, _selection, new NativeMenuItemSeparator(),
            _show, _history, new NativeMenuItemSeparator(),
            _settings, _setup, _help, new NativeMenuItemSeparator(),
            _quit,
        })
        {
            Menu.Items.Add(item);
        }
        if (onSelection is null)
        {
            Menu.Items.Remove(_selection);
        }
        controller.PropertyChanged += (_, _) => Refresh();
        controller.HistoryChanged += () => _ = ReloadRecentAsync();
        Loc.Instance.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public NativeMenu Menu { get; }

    /// <summary>Kurztext für den Tooltip des Tray-Symbols.</summary>
    public string ToolTip => $"WhisperLoom · {Loc.Instance["Stage_" + _controller.Stage.Key()]}";

    /// <summary>Liest die letzten Einträge neu (App-Start, nach jedem Diktat). Ein unlesbarer Verlauf lässt die alte Liste stehen.</summary>
    public async Task ReloadRecentAsync()
    {
        try
        {
            SetRecent(await _controller.RecentAsync());
        }
        catch (IOException)
        {
            // Verlauf nicht lesbar: alte Liste bleibt.
        }
    }

    /// <param name="entries">Neueste zuerst, höchstens <see cref="RecentHistory.Count"/>.</param>
    public void SetRecent(IReadOnlyList<HistoryEntry> entries)
    {
        _entries = entries;
        RefreshRecent();
    }

    public void Refresh()
    {
        var loc = Loc.Instance;
        _toggle.Header = loc[_controller.IsRecording ? "Action_Stop" : "Action_Start"];
        _retry.Header = loc["Tray_Retry"];
        // Direkt unter der Hauptaktion, nur solange Audio für „Erneut senden“ im Speicher liegt.
        var shown = Menu.Items.Contains(_retry);
        if (_controller.CanRetry && !shown)
        {
            Menu.Items.Insert(1, _retry);
        }
        else if (!_controller.CanRetry && shown)
        {
            Menu.Items.Remove(_retry);
        }
        _stage.Header = $"{loc["Stage_Label"]}: {loc["Stage_" + _controller.Stage.Key()]}";
        _show.Header = loc["Tray_Stage"];
        _history.Header = loc["Tray_History"];
        _selection.Header = loc["Tray_Selection"];
        _settings.Header = loc["Tray_Settings"];
        _setup.Header = loc["Tray_Setup"];
        _help.Header = loc["Tray_Help"];
        _quit.Header = loc["Tray_Quit"];

        _stage.Menu!.Items.Clear();
        foreach (var mode in RefineModes.SettingsFor(_controller.Settings.Refine.PromptEnabled))
        {
            var key = mode.Key();
            _stage.Menu.Items.Add(new NativeMenuItem
            {
                Header = loc["Stage_" + key],
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = mode == _controller.Stage,
                Command = new RelayCommand(() => _controller.SetStage(key)),
            });
        }
        RefreshRecent();
    }

    private void RefreshRecent()
    {
        var loc = Loc.Instance;
        _recent.Header = loc["Tray_Recent"];
        _recent.Menu!.Items.Clear();
        if (_entries.Count == 0)
        {
            _recent.Menu.Items.Add(new NativeMenuItem { Header = loc["Tray_RecentEmpty"], IsEnabled = false });
            return;
        }
        foreach (var entry in _entries)
        {
            _recent.Menu.Items.Add(new NativeMenuItem
            {
                Header = RecentHistory.Label(entry),
                Command = new AsyncRelayCommand(() => _onRecent?.Invoke(entry) ?? Task.CompletedTask),
            });
        }
    }
}
