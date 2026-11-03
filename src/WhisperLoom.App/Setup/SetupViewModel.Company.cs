using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Setup;

/// <summary>Firmen-Pfad des Assistenten (W2-ENR): Auswahl, Schrittfolge, Einladungslink, Weiterlaufen nach der Freigabe.</summary>
public sealed partial class SetupViewModel
{
    /// <summary>Firmen-Schritt; null ohne Firmen-Anbindung.</summary>
    public CompanyJoinViewModel? Join { get; }

    /// <summary>Auswahl im ersten Schritt: „Mit meiner Firma“ statt „Für mich“.</summary>
    public bool UseCompany
    {
        get => _useCompany;
        set
        {
            if (SetProperty(ref _useCompany, value))
            {
                OnPropertyChanged(nameof(UsePersonal));
                Steps = BuildSteps();
            }
        }
    }

    private bool _useCompany;

    public bool UsePersonal
    {
        get => !UseCompany;
        set => UseCompany = !value;
    }

    /// <summary>Schritte samt Firmen-Pfad: „Wie nutzt du …“ und ggf. der Firmen-Schritt vor den übrigen.</summary>
    private List<SetupStep> BuildSteps()
    {
        var steps = new List<SetupStep>();
        if (Join is not null && _companyAvailable)
        {
            steps.Add(SetupStep.Usage);
            if (UseCompany || Join.Phase != JoinPhase.Input)
            {
                steps.Add(SetupStep.Company);
            }
        }
        steps.AddRange(VisibleSteps(Settings.Editable, Settings.Local.Engine.Value));
        return steps;
    }

    /// <summary>Im Firmen-Schritt geht es erst nach der Freigabe weiter (oder per Überspringen).</summary>
    private bool CanNext() => Current != SetupStep.Company || Join is { IsApproved: true };

    /// <summary>Einladungslink geklickt: Firmen-Pfad wählen, Felder vorbelegen, zum Firmen-Schritt springen.</summary>
    public void OpenJoinLink(Management.JoinLink link)
    {
        if (Join is null || !Steps.Contains(SetupStep.Usage))
        {
            return; // schon verbunden bzw. von der IT eingerichtet
        }
        UseCompany = true;
        Join.Prefill(link);
        Go(Steps.ToList().IndexOf(SetupStep.Company));
    }

    /// <summary>
    /// Freigabe da und Konfiguration angewandt: neue Arbeitskopie (Fest der Firma ausgeblendet), Schritte neu, weiter zum
    /// ersten Schritt nach dem Firmen-Schritt.
    /// </summary>
    private void OnApproved()
    {
        Detach(Settings);
        var fresh = Settings.Reopen();
        Settings = fresh;
        Managed = ManagedFor(fresh);
        Hotkey = new HotkeyCheck(fresh.Hotkeys, _recordHotkey);
        Attach(fresh);
        _ = fresh.LoadKeysAsync();
        Steps = BuildSteps();
        var company = Steps.ToList().IndexOf(SetupStep.Company);
        Current = Steps[Math.Min(company + 1, Steps.Count - 1)];
        OnPropertyChanged(nameof(DoneHint));
        OnPropertyChanged(nameof(DoneKeys));
        OnPropertyChanged(nameof(SkippedStepsText));
        OnPropertyChanged(nameof(ShowOnline));
        OnPropertyChanged(nameof(ShowOffline));
    }

    private void Attach(SettingsViewModel settings)
    {
        settings.Local.PropertyChanged += OnLocalChanged;
        settings.PropertyChanged += OnSettingsChanged;
    }

    private void Detach(SettingsViewModel settings)
    {
        settings.Local.PropertyChanged -= OnLocalChanged;
        settings.PropertyChanged -= OnSettingsChanged;
    }
}
