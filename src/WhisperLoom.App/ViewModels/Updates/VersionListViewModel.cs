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

    /// <summary>Datum im Format der Sprache („2. Nov. 2026“ bzw. „2 Nov 2026“, UX-3 Befund 3); leer bei Ordner-Feeds.</summary>
    public string Date => Release.Published?.ToLocalTime().ToString(
        Loc.Instance.Language == "en" ? "d MMM yyyy" : "d. MMM yyyy",
        System.Globalization.CultureInfo.GetCultureInfo(Loc.Instance.Language == "en" ? "en-GB" : "de-DE")) ?? "";

    public bool IsStable => Channel == UpdateChannel.Stable;

    public bool IsInstalled { get; } = installed;

    /// <summary>Niedriger als die installierte Version: Installieren nur nach Rückfrage.</summary>
    public bool IsDowngrade { get; } = downgrade;

    public bool CanInstall => !IsInstalled;

    /// <summary>Name des Textknopfs: „Version 1.0.1 (Stabil) installieren“ statt fünfmal „Installieren“.</summary>
    public string InstallName => Loc.Instance.Format("Channel_InstallName", Version, ChannelLabel);
}

/// <summary>
/// Liste aller Versionen der GitHub-Releases (W3-CHAN) über alle Kanäle dieses OS, neueste zuerst. Installieren fragt
/// immer nach; eine niedrigere Version (auch „Zurück zu Stabil“) ist ein Downgrade mit eigenem Hinweis. Die Richtlinie
/// <c>updatesEnabled=false</c> sperrt Laden und Installieren.
/// </summary>
/// <param name="confirm">Rückfrage: (Version, Downgrade) → true = installieren und neu starten.</param>
/// <param name="channelChosen">
/// Nach erfolgreichem Laden, vor dem Neustart: Update-Kanal = Kanal der installierten Version. Sonst böte die
/// automatische Prüfung nach „Zurück zu Stabil“ mit Vorab-Schalter sofort wieder die Beta an.
/// </param>
public sealed partial class VersionListViewModel(
    IUpdateClient client,
    AppLog log,
    Func<PolicySettings> policy,
    Func<string, bool, Task<bool>> confirm,
    string? os = null,
    Func<UpdateChannel, Task>? channelChosen = null) : ObservableObject
{
    private readonly string _os = os ?? UpdateChannels.CurrentOs;

    public ObservableCollection<VersionRow> Rows { get; } = [];

    [ObservableProperty] private string _status = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(BackToStableCommand))] private bool _isBusy;

    /// <summary>Ein Download läuft: Balken mit Prozent und „Abbrechen“ (UX-6 N19, Richtlinie 03 §3.2: &gt; 10 s).</summary>
    [ObservableProperty] private bool _isDownloading;

    /// <summary>Fortschritt des Downloads in Prozent.</summary>
    [ObservableProperty] private int _progress;

    private CancellationTokenSource? _download;

    /// <summary>Bricht den laufenden Download ab; die installierte Version bleibt.</summary>
    [RelayCommand]
    private void CancelDownload() => _download?.Cancel();

    /// <summary>false bei <c>policy.updatesEnabled=false</c>.</summary>
    public bool Allowed => policy().UpdatesEnabled != false;

    public string? CurrentVersion => client.CurrentVersion;

    public bool HasStatus => Status.Length > 0;

    /// <summary>Neueste stabile Version der Liste; null ohne stabile Version.</summary>
    public VersionRow? NewestStable => Rows.FirstOrDefault(r => r.IsStable);

    /// <summary>
    /// „Zur neuesten stabilen Version (1.0.1)“ nur, wenn es eine gibt und die installierte Version nicht schon stabil ist
    /// (UX-3 Befund 4: Nebenaktion statt gleichrangigem Knopf).
    /// </summary>
    public bool ShowBackToStable => Allowed && NewestStable is { IsInstalled: false } && !Rows.Any(r => r.IsInstalled && r.IsStable);

    public string BackToStableText => Loc.Instance.Format("Channel_BackToStableVersion", NewestStable?.Version ?? "");

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

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
            OnPropertyChanged(nameof(ShowBackToStable));
            OnPropertyChanged(nameof(BackToStableText));
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
        if (row.IsInstalled || IsBusy)
        {
            // IsBusy: zweiter Klick während eines Downloads (Velopacks Update-Sperre ließe ihn scheitern).
            return UpdateOutcome.Current;
        }
        if (!await confirm(row.Version, row.IsDowngrade))
        {
            log.Event("update.manual", ("status", "declined"), ("version", row.Version), ("channel", row.Release.Channel));
            return UpdateOutcome.Declined;
        }
        IsBusy = true;
        IsDownloading = true;
        Progress = 0;
        Status = Loc.Instance.Format("Channel_Downloading", row.Version);
        using var download = _download = new CancellationTokenSource();
        var started = System.Diagnostics.Stopwatch.StartNew();
        void Show(int percent)
        {
            // Ein verspäteter Fortschritt (Post aus Velopacks Thread) überschreibt nie die Endmeldung.
            if (!ReferenceEquals(_download, download) || !IsDownloading)
            {
                return;
            }
            Progress = Math.Clamp(percent, 0, 100);
            Status = Loc.Instance.Format("Channel_DownloadingProgress", row.Version, DownloadText.OfPercent(percent, started.Elapsed));
        }
        try
        {
            await client.DownloadAsync(row.Release, row.IsDowngrade, download.Token, percent =>
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    Show(percent);
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => Show(percent));
                }
            });
            download.Token.ThrowIfCancellationRequested();
            IsDownloading = false; // ab hier nicht mehr abbrechbar: Balken und „Abbrechen“ weg
            log.Event("update.apply", ("version", row.Version), ("channel", row.Release.Channel), ("downgrade", row.IsDowngrade), ("source", "list"));
            if (channelChosen is not null)
            {
                await channelChosen(row.Channel);
            }
            client.ApplyAndRestart(row.Release);
            return UpdateOutcome.Applied;
        }
        catch (OperationCanceledException) when (download.IsCancellationRequested)
        {
            log.Event("update.manual", ("status", "canceled"), ("version", row.Version), ("channel", row.Release.Channel));
            Status = Loc.Instance["Channel_DownloadCanceled"];
            return UpdateOutcome.Declined;
        }
        catch (Exception e)
        {
            // Auch ein fremder Abbruch (z. B. Zeitüberschreitung im Download) ist ein Fehlschlag, kein Absturz in OnInstall.
            log.Event("update.manual", ("status", "failed"), ("errorClass", e.GetType().Name));
            Status = Loc.Instance["Channel_InstallFailed"];
            return UpdateOutcome.Failed;
        }
        finally
        {
            _download = null;
            IsDownloading = false;
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
