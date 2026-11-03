using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Updates;
using Channels = WhisperLoom.App.Updates.UpdateChannels;
using ChannelKind = WhisperLoom.App.Updates.UpdateChannel;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „Allgemein“: Sprache der Oberfläche, Mikrofon, Schriftgröße der Bühne, Updates (Kanal, Vorabversionen,
/// Versionsliste; W3-CHAN), Einstellungen übertragen.
/// </summary>
public sealed partial class GeneralPageViewModel(SettingsViewModel session) : SettingsPageViewModel("general", "System", "Gray", session)
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

    /// <summary>false bei <c>policy.updatesEnabled=false</c>: Kanal, Schalter und Versionsliste sind gesperrt.</summary>
    public bool UpdatesAllowed => Session.Policy.UpdatesEnabled != false;

    private IReadOnlyList<Choice> _channels = BuildChannels();

    public IReadOnlyList<Choice> UpdateChannels => _channels;

    public Choice UpdateChannel
    {
        get => UpdateChannels.First(c => c.Value == Channels.Parse(Session.Desktop.UpdateChannel).Key());
        set
        {
            if (value is null || !UpdatesAllowed || value.Value == Session.Desktop.UpdateChannel)
            {
                return;
            }
            Session.Desktop.UpdateChannel = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPrereleaseChannel));
            Session.Touch();
        }
    }

    /// <summary>Der Vorab-Schalter wirkt nur in Beta/Test.</summary>
    public bool IsPrereleaseChannel => UpdatesAllowed && Channels.Parse(Session.Desktop.UpdateChannel) != ChannelKind.Stable;

    public bool UpdatePrereleaseAuto
    {
        get => Session.Desktop.UpdatePrereleaseAuto;
        set
        {
            if (!UpdatesAllowed || value == Session.Desktop.UpdatePrereleaseAuto)
            {
                return;
            }
            Session.Desktop.UpdatePrereleaseAuto = value;
            OnPropertyChanged();
            Session.Touch();
        }
    }

    [RelayCommand(CanExecute = nameof(UpdatesAllowed))]
    private void OpenVersions() => Session.OpenVersions?.Invoke();

    protected override void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnLanguageChanged(sender, e);
        // Beschriftungen neu; die Auswahl bleibt (Getter sucht über den Wert).
        _channels = BuildChannels();
        OnPropertyChanged(nameof(UpdateChannels));
        OnPropertyChanged(nameof(UpdateChannel));
    }

    private static IReadOnlyList<Choice> BuildChannels() =>
        [.. Channels.All.Select(c => new Choice(c.Key(), Loc.Instance["Channel_" + c.Key()]))];
}
