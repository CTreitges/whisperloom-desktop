using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tray;

/// <summary>
/// Tray-Menü: Start/Stopp, Stufe (Untermenü), Bühnenansicht, Verlauf, Einstellungen, Beenden. Texte folgen
/// Zustand, Stufe und Oberflächensprache. Unter Linux exportiert Avalonia es als StatusNotifierItem; zeigt der
/// Desktop kein Tray, reichen Bühnenansicht und CLI.
/// </summary>
// Doku: docs.avaloniaui.net/docs/reference/controls/tray-icon (abgerufen 2026-10-30).
public sealed class TrayMenu
{
    private readonly DictationController _controller;
    private readonly NativeMenuItem _toggle;
    private readonly NativeMenuItem _stage;
    private readonly NativeMenuItem _show;
    private readonly NativeMenuItem _history;
    private readonly NativeMenuItem _settings;
    private readonly NativeMenuItem _quit;

    /// <param name="open">Fenster-Aktion: "stage", "history", "settings", "quit".</param>
    public TrayMenu(DictationController controller, Action<string> open)
    {
        _controller = controller;
        _toggle = new NativeMenuItem { Command = new AsyncRelayCommand(() => controller.ToggleAsync()) };
        _stage = new NativeMenuItem { Menu = new NativeMenu() };
        _show = new NativeMenuItem { Command = new RelayCommand(() => open("stage")) };
        _history = new NativeMenuItem { Command = new RelayCommand(() => open("history")) };
        _settings = new NativeMenuItem { Command = new RelayCommand(() => open("settings")) };
        _quit = new NativeMenuItem { Command = new RelayCommand(() => open("quit")) };
        Menu = new NativeMenu();
        foreach (var item in new NativeMenuItemBase[] { _toggle, _stage, _show, _history, _settings, new NativeMenuItemSeparator(), _quit })
        {
            Menu.Items.Add(item);
        }
        controller.PropertyChanged += (_, _) => Refresh();
        Loc.Instance.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public NativeMenu Menu { get; }

    /// <summary>Kurztext für den Tooltip des Tray-Symbols.</summary>
    public string ToolTip => $"WhisperLoom · {Loc.Instance["Stage_" + _controller.Stage.Key()]}";

    public void Refresh()
    {
        var loc = Loc.Instance;
        _toggle.Header = loc[_controller.IsRecording ? "Action_Stop" : "Action_Start"];
        _stage.Header = $"{loc["Stage_Label"]}: {loc["Stage_" + _controller.Stage.Key()]}";
        _show.Header = loc["Tray_Stage"];
        _history.Header = loc["Tray_History"];
        _settings.Header = loc["Tray_Settings"];
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
    }
}
