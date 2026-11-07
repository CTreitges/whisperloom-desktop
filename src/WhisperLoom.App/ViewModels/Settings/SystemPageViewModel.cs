using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Updates;
using Channels = WhisperLoom.App.Updates.UpdateChannels;
using ChannelKind = WhisperLoom.App.Updates.UpdateChannel;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „System“ (UX-2): selten gebraucht – Sprache der Oberfläche, Updates (Kanal als Optionsfelder, Vorabversionen nur
/// bei Beta/Test, Versionsliste; W3-CHAN), Einstellungen übertragen und die Einrichtung (aus dem Tray hierher, B43).
/// </summary>
public sealed partial class SystemPageViewModel(SettingsViewModel session) : SettingsPageViewModel("system", "System", session)
{
    public SyncViewModel Sync => Session.Sync;

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
            RequestUpdateCheck();
        }
    }

    /// <summary>Der Vorab-Schalter wirkt nur in Beta/Test und steht nur dort (nur zeigen, was gilt).</summary>
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
            RequestUpdateCheck();
        }
    }

    /// <summary>
    /// TP-WUPDATE: geänderter Kanal oder Schalter wirkt sofort, nicht erst nach Neustart oder 24 h. Erst nach dem Speichern
    /// melden, damit die Prüfung den neuen Stand liest; der Dienst entprellt und prüft nie parallel. Im Assistenten (ohne
    /// Sofort-Speichern) nicht.
    /// </summary>
    private void RequestUpdateCheck()
    {
        if (Session.AutoSave)
        {
            _ = Session.Pending.ContinueWith(_ => UpdateCheckRequests.Request(), TaskScheduler.Default);
        }
    }

    [RelayCommand(CanExecute = nameof(UpdatesAllowed))]
    private void OpenVersions() => Session.OpenVersions?.Invoke();

    /// <summary>Startet den Einrichtungs-Assistenten (früher im Tray, B43); bisherige Einstellungen bleiben.</summary>
    [RelayCommand]
    private void OpenSetup() => Session.OpenSetup?.Invoke();

    protected override void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnLanguageChanged(sender, e);
        // Beschriftungen neu; die Auswahl bleibt (Getter sucht über den Wert).
        _channels = BuildChannels();
        OnPropertyChanged(nameof(UpdateChannels));
        OnPropertyChanged(nameof(UpdateChannel));
    }

    private static IReadOnlyList<Choice> BuildChannels() =>
        [.. Channels.All.Select(c => new Choice(c.Key(), Loc.Instance["Channel_" + c.Key()], Loc.Instance[DescriptionKey(c)]))];

    /// <summary>Test schließt Betas ein (TP-WUPDATE); der Text sagt das.</summary>
    private static string DescriptionKey(ChannelKind channel) =>
        channel == ChannelKind.Test ? "Updates.Channel_test_Desc" : "Channel_" + channel.Key() + "_Desc";
}
