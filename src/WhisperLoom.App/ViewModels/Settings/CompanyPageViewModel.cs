using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Management;
using WhisperLoom.App.Setup;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „Firma“ (W2-ENR): verbunden → Name, Server, Status, letzter Abgleich, „Jetzt abgleichen“, „Firma verlassen“ (nur
/// wenn die Richtlinie es erlaubt, mit Rückfrage). Nicht verbunden bzw. widerrufen → „Mit Firma verbinden …“ öffnet das
/// Formular aus dem Assistenten (Server, Firmencode, Warten auf Freigabe).
/// </summary>
public sealed partial class CompanyPageViewModel : SettingsPageViewModel
{
    private readonly ManagementService? _service;
    private readonly Action _changed;

    public CompanyPageViewModel(SettingsViewModel session) : base("company", "Company", "Blue", session)
    {
        _service = session.Company;
        _changed = () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        if (_service is not null)
        {
            _service.Changed += _changed;
            Join = new CompanyJoinViewModel(_service);
            _showJoin = Join.Phase == JoinPhase.Pending;
        }
    }

    /// <summary>Formular zum Verbinden; null ohne Firmen-Anbindung (Tests ohne Server).</summary>
    public CompanyJoinViewModel? Join { get; }

    private Core.Contracts.Settings.ManagementSettings M => (_service?.Settings ?? Session.Current).Management;

    /// <summary>Mit einem Firmen-Server verbunden (auch wartend oder gesperrt), nicht widerrufen.</summary>
    public bool IsConnected => M.ServerUrl.Length > 0 && M.State is "pending" or "approved" or "blocked";

    public bool IsNotConnected => !IsConnected;

    /// <summary>„Mit Firma verbinden …“ zeigen: nicht verbunden und Formular zu (sonst stünde eine leere Gruppe darüber).</summary>
    public bool ShowConnect => IsNotConnected && !ShowJoin;

    public string TenantName => ManagementTexts.Tenant(M);

    public string ServerUrl => M.ServerUrl;

    public string DeviceId => M.DeviceId;

    public string StateText => ManagementTexts.State(M.State);

    public string LastSyncText => M.LastSync <= 0
        ? Loc.Instance["Company_LastSyncNever"]
        : DateTimeOffset.FromUnixTimeMilliseconds(M.LastSync).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Hinweis bei Sperre bzw. Widerruf; leer = keiner.</summary>
    public string Notice => M.State is "blocked" or "revoked" ? ManagementTexts.Notice(M) : "";

    public bool CanLeave => _service?.CanLeave ?? false;

    /// <summary>Ergebnis des letzten Abgleichs bzw. Verlassens.</summary>
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private bool _leavePending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnect))]
    private bool _showJoin;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand), nameof(LeaveCommand))]
    private bool _busy;

    [RelayCommand]
    private void OpenJoin() => ShowJoin = true;

    /// <summary>Formular schließen, solange noch nicht verbunden (danach wartet das Gerät; das Formular zeigt den Status).</summary>
    [RelayCommand]
    private void CloseJoin() => ShowJoin = false;

    private bool NotBusy() => !Busy && _service is not null;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task SyncNow()
    {
        Busy = true;
        try
        {
            var outcome = await _service!.SyncAsync(CancellationToken.None);
            Status = outcome switch
            {
                SyncOutcome.Synced or SyncOutcome.NotModified => Loc.Instance["Company_SyncOk"],
                SyncOutcome.Pending => Loc.Instance["Company_Waiting"],
                SyncOutcome.Blocked or SyncOutcome.Revoked => ManagementTexts.Notice(M),
                _ => ManagementTexts.Error(_service.LastError),
            };
        }
        finally
        {
            Busy = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void Leave() => LeavePending = true;

    [RelayCommand]
    private void CancelLeave() => LeavePending = false;

    /// <summary>Rückfrage bestätigt: abmelden; Firmen-Werte bleiben als eigene Einstellungen stehen.</summary>
    [RelayCommand]
    private async Task ConfirmLeave()
    {
        LeavePending = false;
        Busy = true;
        try
        {
            var outcome = await _service!.LeaveAsync(CancellationToken.None);
            Status = outcome switch
            {
                LeaveOutcome.Left => Loc.Instance["Company_LeftOk"],
                LeaveOutcome.NotAllowed => Loc.Instance["Company_Err_leave_not_allowed"],
                _ => ManagementTexts.Error(_service.LastError),
            };
        }
        finally
        {
            Busy = false;
            Refresh();
        }
    }

    public override void Dispose()
    {
        if (_service is not null)
        {
            _service.Changed -= _changed;
        }
        Join?.Deactivate();
        base.Dispose();
    }

    protected override void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnLanguageChanged(sender, e);
        Refresh();
    }

    private void Refresh()
    {
        foreach (var name in new[] { nameof(IsConnected), nameof(IsNotConnected), nameof(TenantName), nameof(ServerUrl), nameof(DeviceId),
                     nameof(StateText), nameof(LastSyncText), nameof(Notice), nameof(CanLeave), nameof(ShowConnect) })
        {
            OnPropertyChanged(name);
        }
        if (IsConnected && Join is { Phase: not JoinPhase.Pending })
        {
            ShowJoin = false;
        }
    }
}
