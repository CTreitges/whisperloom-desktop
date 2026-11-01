using System.Globalization;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>Seite „Allgemein“: Sprache der Oberfläche, Mikrofon, Schriftgröße der Bühne, Einstellungen übertragen.</summary>
public sealed class GeneralPageViewModel(SettingsViewModel session) : SettingsPageViewModel("general", "General", "Gray", session)
{
    public const int MinFontSize = 16;
    public const int MaxFontSize = 96;

    public SyncViewModel Sync => Session.Sync;

    /// <summary>Schriftgröße der Bühne als Text; gültig 16–96 (wie Strg+Plus/Minus in der Bühne).</summary>
    public string StageFontSize
    {
        get => Session.Desktop.StageFontSize.ToString(CultureInfo.InvariantCulture);
        set => SetNumber("desktop.stageFontSize", value, MinFontSize, MaxFontSize, zeroAllowed: false, v => Session.Desktop.StageFontSize = v);
    }
}
