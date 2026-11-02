using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.Updates;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.ViewModels.Updates;

/// <summary>Eine Zeile der Versionsliste.</summary>
public sealed class VersionRow(UpdateChannel channel, UpdateRelease release, bool installed, bool downgrade)
{
    public UpdateChannel Channel { get; } = channel;

    public UpdateRelease Release { get; } = release;

    public string Version => Release.Version;

    public string ChannelLabel => Loc.Instance["Channel_" + Channel.Key()];

    /// <summary>ISO-Datum (in beiden Sprachen eindeutig); leer bei Ordner-Feeds.</summary>
    public string Date => Release.Published?.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public bool IsInstalled { get; } = installed;

    /// <summary>Niedriger als die installierte Version: Installieren nur nach Rückfrage.</summary>
    public bool IsDowngrade { get; } = downgrade;

    public bool CanInstall => !IsInstalled;
}

/// <summary>
/// Liste aller Versionen der GitHub-Releases (W3-CHAN) über alle Kanäle dieses OS, neueste zuerst. Installieren fragt
/// immer nach; eine niedrigere Version (auch „Zurück zu Stabil“) ist ein Downgrade mit eigenem Hinweis. Die Richtlinie
/// <c>updatesEnabled=false</c> sperrt Laden und Installieren.
/// </summary>
/// <param name="confirm">Rückfrage: (Version, Downgrade) → true = installieren und neu starten.</param>
public sealed partial class VersionListViewModel(
    IUpdateClient client,
    AppLog log,
    Func<PolicySettings> policy,
    Func<string, bool, Task<bool>> confirm,
    string? os = null) : ObservableObject
{
    private readonly string _os = os ?? UpdateChannels.CurrentOs;

    public ObservableCollection<VersionRow> Rows { get; } = [];

    [ObservableProperty] private string _status = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(BackToStableCommand))] private bool _isBusy;

    /// <summary>false bei <c>policy.updatesEnabled=false</c>.</summary>
    public bool Allowed => policy().UpdatesEnabled != false;

    public string? CurrentVersion => client.CurrentVersion;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Rows.Clear();
        if (!Allowed)
        {
            Status = Loc.Instance["Channel_Disabled"];
            return;
        }
        IsBusy = true;
        Status = Loc.Instance["Channel_Loading"];
        try
        {
            var current = client.CurrentVersion;
            var all = new List<VersionRow>();
            foreach (var channel in UpdateChannels.All)
            {
                foreach (var release in await client.ListAsync(channel.FeedName(_os), ct))
                {
                    var cmp = current is null ? 1 : AppVersion.Compare(release.Version, current);
                    all.Add(new VersionRow(channel, release, installed: cmp == 0, downgrade: cmp < 0));
                }
            }
            foreach (var row in all.OrderByDescending(r => r.Version, Comparer<string>.Create(AppVersion.Compare)))
            {
                Rows.Add(row);
            }
            Status = Rows.Count == 0 ? Loc.Instance["Channel_Empty"] : client.IsInstalled ? "" : Loc.Instance["Channel_NotInstalled"];
            log.Event("update.list", ("status", "ok"), ("count", Rows.Count));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Nur die Fehlerklasse loggen, die Meldung kann URLs oder Pfade enthalten.
            log.Event("update.list", ("status", "failed"), ("errorClass", e.GetType().Name));
            Status = Loc.Instance["Channel_LoadFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Installiert die Zeile nach Rückfrage und startet neu.</summary>
    public async Task<UpdateOutcome> InstallAsync(VersionRow row)
    {
        if (!Allowed)
        {
            Status = Loc.Instance["Channel_Disabled"];
            return UpdateOutcome.DisabledByPolicy;
        }
        if (!client.IsInstalled)
        {
            Status = Loc.Instance["Channel_NotInstalled"];
            return UpdateOutcome.NotInstalled;
        }
        if (row.IsInstalled)
        {
            return UpdateOutcome.Current;
        }
        if (!await confirm(row.Version, row.IsDowngrade))
        {
            log.Event("update.manual", ("status", "declined"), ("version", row.Version), ("channel", row.Release.Channel));
            return UpdateOutcome.Declined;
        }
        IsBusy = true;
        Status = Loc.Instance.Format("Channel_Downloading", row.Version);
        try
        {
            await client.DownloadAsync(row.Release, row.IsDowngrade, CancellationToken.None);
            log.Event("update.apply", ("version", row.Version), ("channel", row.Release.Channel), ("downgrade", row.IsDowngrade), ("source", "list"));
            client.ApplyAndRestart(row.Release);
            return UpdateOutcome.Applied;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.Event("update.manual", ("status", "failed"), ("errorClass", e.GetType().Name));
            Status = Loc.Instance["Channel_InstallFailed"];
            return UpdateOutcome.Failed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Rückweg: neueste stabile Version installieren (meist ein Downgrade von einer Vorabversion).</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public Task<UpdateOutcome> BackToStableAsync() =>
        Rows.FirstOrDefault(r => r.Channel == UpdateChannel.Stable) is { } stable
            ? InstallAsync(stable)
            : Task.FromResult(UpdateOutcome.Current);

    private bool CanGoBack() => !IsBusy;

}
