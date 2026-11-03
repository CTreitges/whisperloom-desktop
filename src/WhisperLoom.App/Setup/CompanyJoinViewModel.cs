using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Management;

namespace WhisperLoom.App.Setup;

/// <summary>Ablauf im Firmen-Schritt: Eingabe → verbinden → wartet auf Freigabe → freigegeben.</summary>
public enum JoinPhase
{
    Input,
    Joining,
    Pending,
    Approved,
}

/// <summary>
/// Firmen-Schritt des Assistenten (W2-ENR): Server-Adresse und Firmencode (oder ein eingefügter Einladungslink), dann
/// „Wartet auf Freigabe durch die IT“ mit Abfrage alle <c>pollAfterSec</c> Sekunden, solange der Schritt offen ist. Nach
/// der Freigabe gleicht <see cref="ManagementService"/> ab und <see cref="Approved"/> lässt den Assistenten weiterlaufen.
/// </summary>
public sealed partial class CompanyJoinViewModel : ObservableObject
{
    private readonly ManagementService _service;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _poll;

    /// <param name="delay">Wartezeit zwischen zwei Abfragen; Tests ersetzen sie.</param>
    public CompanyJoinViewModel(ManagementService service, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _service = service;
        _delay = delay ?? Task.Delay;
        var m = service.Settings.Management;
        _server = m.ServerUrl;
        if (m.State == "pending" && m.ServerUrl.Length > 0)
        {
            _phase = JoinPhase.Pending;
            _status = Loc.Instance["Company_Waiting"];
        }
    }

    /// <summary>Gerät ist freigegeben und die Firmen-Konfiguration angewandt.</summary>
    public event Action? Approved;

    [ObservableProperty] private string _server = "";

    [ObservableProperty] private string _code = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInput), nameof(IsWaiting), nameof(IsApproved), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    private JoinPhase _phase;

    /// <summary>Fehler bzw. Hinweis unter dem Formular; leer = keiner.</summary>
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private bool _statusIsError;

    public bool ShowInput => Phase is JoinPhase.Input or JoinPhase.Joining;

    public bool IsBusy => Phase == JoinPhase.Joining;

    public bool IsWaiting => Phase == JoinPhase.Pending;

    public bool IsApproved => Phase == JoinPhase.Approved;

    public string TenantName => ManagementTexts.Tenant(_service.Settings.Management);

    public string ApprovedText => Loc.Instance.Format("Company_ApprovedBy", TenantName);

    public string WaitingDetail => Loc.Instance.Format("Company_WaitingDetail", TenantName, DeviceId);

    /// <summary>Geräte-ID zum Kopieren für die IT (Portal-Freigabe).</summary>
    public string DeviceId => _service.Settings.Management.DeviceId;

    /// <summary>Server und Code aus einem Einladungslink (Klick auf <c>whisperloom://join…</c>).</summary>
    public void Prefill(JoinLink link)
    {
        Server = link.Server;
        Code = link.Code;
    }

    /// <summary>Ein eingefügter Einladungslink im Adressfeld füllt beide Felder.</summary>
    partial void OnServerChanged(string value)
    {
        if (JoinLink.TryParse(value) is { } link)
        {
            Prefill(link);
        }
    }

    private bool CanJoin() => Phase is JoinPhase.Input;

    [RelayCommand(CanExecute = nameof(CanJoin))]
    public async Task Join()
    {
        var (url, urlError) = EnrollInput.NormalizeServer(Server);
        var code = EnrollInput.NormalizeCode(Code);
        if (url is null || code is null)
        {
            ShowError(Loc.Instance[urlError ?? "Company_CodeInvalid"]);
            return;
        }
        Phase = JoinPhase.Joining;
        Status = "";
        try
        {
            Handle(await _service.JoinAsync(url, code, EnrollInput.DefaultDeviceName(), CancellationToken.None));
        }
        catch (ManagementException e)
        {
            Phase = JoinPhase.Input;
            ShowError(ManagementTexts.Error(e.Error));
        }
        catch (ManagementOfflineException)
        {
            Phase = JoinPhase.Input;
            ShowError(ManagementTexts.Error("offline"));
        }
        catch (ArgumentException e) when (e.Message == "Company_AlreadyJoined")
        {
            Phase = JoinPhase.Input;
            ShowError(Loc.Instance[e.Message]);
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            Phase = JoinPhase.Input;
            ShowError(ManagementTexts.Error(null));
        }
    }

    /// <summary>Schritt sichtbar: wartet das Gerät schon, gleich wieder abfragen.</summary>
    public void Activate()
    {
        if (Phase == JoinPhase.Pending)
        {
            StartPolling();
        }
    }

    /// <summary>Schritt verlassen bzw. Fenster zu: Abfragen beenden (danach gleicht die App im Intervall ab).</summary>
    public void Deactivate()
    {
        _poll?.Cancel();
        _poll = null;
    }

    /// <summary>Läuft gerade eine Abfrage-Schleife (Tests)?</summary>
    public Task Polling { get; private set; } = Task.CompletedTask;

    private void Handle(SyncOutcome outcome)
    {
        switch (outcome)
        {
            case SyncOutcome.Synced or SyncOutcome.NotModified:
                Deactivate();
                Phase = JoinPhase.Approved;
                StatusIsError = false;
                Status = "";
                OnPropertyChanged(nameof(ApprovedText));
                Approved?.Invoke();
                break;
            case SyncOutcome.Pending:
                Phase = JoinPhase.Pending;
                StatusIsError = false;
                Status = Loc.Instance["Company_Waiting"];
                OnPropertyChanged(nameof(WaitingDetail));
                StartPolling();
                break;
            default:
                Deactivate();
                Phase = JoinPhase.Input;
                ShowError(outcome == SyncOutcome.Blocked ? Loc.Instance["Company_Err_device_blocked"] : ManagementTexts.Error(_service.LastError));
                break;
        }
    }

    private void StartPolling()
    {
        if (_poll is not null)
        {
            return;
        }
        _poll = new CancellationTokenSource();
        Polling = PollAsync(_poll.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && Phase == JoinPhase.Pending)
            {
                await _delay(TimeSpan.FromSeconds(Math.Clamp(_service.PollAfterSec, 2, 60)), ct);
                var outcome = await _service.SyncAsync(ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                // Kurz offline: weiter warten, Hinweis zeigen; sonst ist das Warten vorbei.
                Status = Loc.Instance[outcome == SyncOutcome.Offline ? "Company_Err_offline" : "Company_Waiting"];
                if (outcome is not (SyncOutcome.Pending or SyncOutcome.Offline))
                {
                    _poll = null;
                    Handle(outcome);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Schritt verlassen
        }
    }

    private void ShowError(string text)
    {
        StatusIsError = true;
        Status = text;
    }
}
