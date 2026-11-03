using System.Globalization;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „Diktat“ (UX-2): alles, was ein Diktat auslöst und begleitet – Tastenkürzel (an/aus und je Aktion eine Zeile),
/// Mikrofon, Markierten Text verarbeiten (Entscheidung 8: bleibt hier) und die Schriftgröße der Bühne. Linux: Befehle für
/// ein Kürzel der Desktop-Umgebung statt globaler Tastenkürzel.
/// </summary>
public sealed class DictationPageViewModel(SettingsViewModel session, string toggleCommand) : SettingsPageViewModel("dictation", "Dictation", session)
{
    public const int MinFontSize = 16;
    public const int MaxFontSize = 96;

    public HotkeysViewModel Hotkeys => Session.Hotkeys;

    public bool IsLinux => !Hotkeys.Available;

    /// <summary>Befehl für ein Kürzel der Desktop-Umgebung (Linux).</summary>
    public string ToggleCommand { get; } = toggleCommand;

    /// <summary>Befehl „Markierung verarbeiten“ für ein Kürzel der Desktop-Umgebung (Linux, W2-SEL).</summary>
    public string SelectionCommand => ToggleCommand.EndsWith(" toggle", StringComparison.Ordinal)
        ? ToggleCommand[..^" toggle".Length] + " process-selection"
        : "WhisperLoom process-selection";

    /// <summary>Schriftgröße der Bühne als Text; gültig 16–96 (wie Strg+Plus/Minus in der Bühne).</summary>
    public string StageFontSize
    {
        get => Session.Desktop.StageFontSize.ToString(CultureInfo.InvariantCulture);
        set => SetNumber("desktop.stageFontSize", value, MinFontSize, MaxFontSize, zeroAllowed: false, v => Session.Desktop.StageFontSize = v);
    }
}
