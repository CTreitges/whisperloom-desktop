using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Setup;

/// <summary>
/// Block „So geht's weiter“ auf der Seite „Fertig“ (<see cref="DoneGuideView"/>): wo WhisperLoom jetzt läuft (Tray, unter Windows
/// mit Taskleisten-Skizze), was Links- und Rechtsklick tun und ob es von selbst startet – nach dem Autostart-Schalter, wie er beim
/// Erreichen der Seite steht (umgesetzt wird er erst mit „Fertig“), bzw. ohne Autostart auf diesem System.
/// </summary>
public sealed partial class SetupViewModel
{
    private string Os => Windows ? "Windows" : "Linux";

    /// <summary>Skizze der Taskleiste nur unter Windows; unter Linux sehen Tray und Statusleiste je Desktop anders aus.</summary>
    public bool ShowTaskbarSketch => Windows;

    public string DoneGuideTray => Loc.Instance["Setup_DoneGuideTray" + Os];

    /// <summary>Windows: Linksklick Bühne, Rechtsklick Menü. Linux: je Desktop öffnet der Linksklick die Bühne oder schon das Menü.</summary>
    public string DoneGuideClicks => Loc.Instance["Setup_DoneGuideClicks" + Os];

    public string DoneGuideAutostart => Loc.Instance[
        "Setup_DoneGuideAutostart" + (!AutostartAvailable ? "Unavailable" : AutostartEnabled ? "On" : "Off") + Os];

    partial void OnAutostartEnabledChanged(bool value) => OnPropertyChanged(nameof(DoneGuideAutostart));

    private void RaiseDoneGuideTexts()
    {
        OnPropertyChanged(nameof(DoneGuideTray));
        OnPropertyChanged(nameof(DoneGuideClicks));
        OnPropertyChanged(nameof(DoneGuideAutostart));
    }
}
