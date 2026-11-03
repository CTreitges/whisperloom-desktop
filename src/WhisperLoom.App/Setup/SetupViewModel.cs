using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Setup;

/// <summary>Schritte des Einrichtungs-Assistenten in fester Reihenfolge.</summary>
public enum SetupStep
{
    /// <summary>„Wie nutzt du WhisperLoom?“ – für mich oder mit meiner Firma (W2-ENR).</summary>
    Usage,

    /// <summary>Firmen-Server und Firmencode, danach „Wartet auf Freigabe“ (W2-ENR).</summary>
    Company,

    Language,
    Microphone,
    Recognition,
    Stage,
    Hotkey,
    Autostart,
    Done,
}

/// <summary>
/// Einrichtungs-Assistent beim ersten Start: Sprache, Mikrofon mit Pegel-Test, Erkennungsweg (Cloud mit Key und
/// „Zugang prüfen“ oder Lokal mit Modell-Download), Stufe, Hotkey-Test (Windows) bzw. Anleitung fürs Desktop-Kürzel
/// (Linux), Autostart, Fertig. Arbeitet auf einem <see cref="SettingsViewModel"/> (dieselbe Kopie, Sperren und
/// dasselbe Speichern wie das Einstellungsfenster). Von der IT gesperrte Schritte (<see cref="SettingsViewModel.Editable"/>)
/// entfallen. Überspringen speichert nichts, markiert die Einrichtung aber als erledigt.
/// W2-ENR: Mit Firmen-Anbindung beginnt der Assistent mit „Wie nutzt du WhisperLoom?“. Der Firmen-Pfad verbindet per
/// Firmencode, wartet auf die Freigabe und baut danach die Arbeitskopie und die Schritte neu auf: Was die Firma Fest
/// setzt, entfällt; Mikrofon, Hotkey-Test und Autostart bleiben.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly SetupState _state;
    private readonly IAutostart? _autostart;
    private readonly string _uiLanguageBefore;
    private readonly Func<Task<string?>>? _recordHotkey;
    private readonly bool _companyAvailable;

    /// <param name="settings">Kopie der Einstellungen samt Key-Feldern, lokalen Modellen und Hotkey-Rekorder.</param>
    /// <param name="autostart">null = OS ohne Autostart; der Schritt zeigt dann nur einen Hinweis.</param>
    /// <param name="microphone">Pegel-Test; null = kein Mikrofon nutzbar (Hinweis statt Test).</param>
    /// <param name="recordHotkey">Rekorder des globalen Hooks (Windows); null = Hotkey-Test nicht möglich.</param>
    /// <param name="windows">Windows-Variante (Hotkey-Test) statt Linux-Anleitung; Tests prüfen beide.</param>
    /// <param name="toggleCommand">Befehl fürs Desktop-Kürzel unter Linux, z. B. <c>"/opt/WhisperLoom" toggle</c>.</param>
    /// <param name="management">Firmen-Anbindung; null = ohne Firmen-Pfad (Schritt „Wie nutzt du WhisperLoom?“ entfällt).</param>
    /// <param name="pollDelay">Wartezeit zwischen zwei Abfragen beim Warten auf die Freigabe; Tests ersetzen sie.</param>
    public SetupViewModel(SettingsViewModel settings, SetupState state, IAutostart? autostart, MicLevelTest? microphone,
        Func<Task<string?>>? recordHotkey, bool windows, string toggleCommand, Management.ManagementService? management = null,
        Func<TimeSpan, CancellationToken, Task>? pollDelay = null)
    {
        _settings = settings;
        _state = state;
        _autostart = autostart;
        _recordHotkey = recordHotkey;
        Mic = microphone;
        _hotkey = new HotkeyCheck(settings.Hotkeys, recordHotkey);
        Windows = windows;
        ToggleCommand = toggleCommand;
        _uiLanguageBefore = settings.UiLanguage.Value;
        _autostartEnabled = autostart?.IsEnabled ?? false;
        if (management is not null)
        {
            Join = new CompanyJoinViewModel(management, pollDelay);
            Join.Approved += OnApproved;
            Join.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CompanyJoinViewModel.Phase))
                {
                    NextCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(ShowNext));
                }
            };
            var m = settings.Current.Management;
            // Usage nur für noch nicht verbundene Geräte ohne IT-Richtlinie; ein wartendes Gerät landet direkt beim Firmen-Schritt.
            _companyAvailable = m.State is "none" or "revoked" && !settings.HasPolicy || m.State == "pending";
            _useCompany = m.State == "pending";
        }
        _steps = BuildSteps();
        _current = _steps[0];
        Managed = ManagedFor(settings);
        Attach(settings);
        if (Mic is not null)
        {
            Mic.PropertyChanged += OnMicChanged;
        }
        // Abmelden in Dispose (Fenster zu), sonst hält Loc.Instance Assistent und Fenster fest.
        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>Stufen je Einstellung (Fest blendet Zeilen aus, Vorgabe zeigt das Etikett) für die Zeilen der Schritte.</summary>
    public Design.ManagedContext Managed
    {
        get => _managed;
        private set => SetProperty(ref _managed, value);
    }

    private Design.ManagedContext _managed = Design.ManagedContext.None;

    private static Design.ManagedContext ManagedFor(SettingsViewModel s)
    {
        var source = new Design.PolicyManagedSource(s.Policy, () => s.Current);
        return new Design.ManagedContext(source, path => s.ResetToDefaultAsync(path, source));
    }

    /// <summary>Arbeitskopie; nach der Freigabe durch die Firma neu aufgebaut (neue Richtlinie).</summary>
    public SettingsViewModel Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    private SettingsViewModel _settings;

    public MicLevelTest? Mic { get; }

    public HotkeyCheck Hotkey
    {
        get => _hotkey;
        private set => SetProperty(ref _hotkey, value);
    }

    private HotkeyCheck _hotkey;

    public bool Windows { get; }

    public string ToggleCommand { get; }

    /// <summary>Sichtbare Schritte nach der Richtlinie; „Fertig“ ist immer dabei und immer zuletzt.</summary>
    public IReadOnlyList<SetupStep> Steps
    {
        get => _steps;
        private set
        {
            if (SetProperty(ref _steps, value))
            {
                OnPropertyChanged(nameof(Index));
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(ProgressValue));
                OnPropertyChanged(nameof(CanBack));
                BackCommand.NotifyCanExecuteChanged();
                OnStepsChanged();
            }
        }
    }

    private IReadOnlyList<SetupStep> _steps;

    /// <summary>Fenster schließen (nach Fertig oder Überspringen).</summary>
    public event Action? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Index), nameof(Progress), nameof(ProgressValue), nameof(Title), nameof(CanBack), nameof(IsLast),
        nameof(ShowUsage), nameof(ShowCompany), nameof(ShowLanguage), nameof(ShowMicrophone), nameof(ShowRecognition), nameof(ShowStage), nameof(ShowHotkey),
        nameof(ShowAutostart), nameof(ShowDone))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand))]
    private SetupStep _current;

    [ObservableProperty]
    private bool _autostartEnabled;

    /// <summary>Fehler beim Abschließen (Speichern bzw. Autostart); leer = keiner.</summary>
    [ObservableProperty]
    private string _status = "";

    public int Index => Steps.ToList().IndexOf(Current);

    /// <summary>Fortschrittsbalken im Kopf: erledigte Schritte inklusive des aktuellen.</summary>
    public int ProgressValue => Index + 1;

    public string Progress => Loc.Instance.Format("Setup_Progress", Index + 1, Steps.Count);

    public string Title => Loc.Instance["Setup_Step_" + Current];

    public bool CanBack => Index > 0;

    public bool IsLast => Current == SetupStep.Done;

    public bool ShowUsage => Current == SetupStep.Usage;
    public bool ShowCompany => Current == SetupStep.Company;
    public bool ShowLanguage => Current == SetupStep.Language;
    public bool ShowMicrophone => Current == SetupStep.Microphone;
    public bool ShowRecognition => Current == SetupStep.Recognition;
    public bool ShowStage => Current == SetupStep.Stage;
    public bool ShowHotkey => Current == SetupStep.Hotkey;
    public bool ShowAutostart => Current == SetupStep.Autostart;
    public bool ShowDone => Current == SetupStep.Done;

    public bool AutostartAvailable => _autostart is not null;

    /// <summary>Erkennungsweg Cloud: Anbieter und Key mit „Zugang prüfen“.</summary>
    public bool ShowOnline => Settings.Local.Engine.Value == "online";

    /// <summary>Erkennungsweg Lokal: Modell wählen und laden.</summary>
    public bool ShowOffline => !ShowOnline;

    /// <summary>Das in der Auswahl gewählte lokale Modell samt Download (Schnittstelle aus WLD-P4b).</summary>
    public LocalModelViewModel SelectedModel =>
        Settings.Local.Models.First(m => m.Model.Id == Settings.Local.SelectedModel.Value);

    /// <summary>Zusammenfassung auf der Seite „Fertig“: wie man das erste Diktat startet.</summary>
    public string DoneHint => string.Format(System.Globalization.CultureInfo.CurrentCulture, DoneTemplate, [.. DoneKeys]);

    /// <summary>Vorlage des Satzes auf „Fertig“; {0}, {1} stehen für <see cref="DoneKeys"/> (als Tastenkappen gezeigt).</summary>
    public string DoneTemplate => Loc.Instance[Windows ? "Setup_DoneWindows" : "Setup_DoneLinux"];

    public IReadOnlyList<string> DoneKeys => Windows ? [Settings.Hotkeys.Hold.Value, Settings.Hotkeys.Toggle.Value] : [ToggleCommand];

    /// <summary>Schritte nach Richtlinie: ein Schritt entfällt, wenn alle seine Felder gesperrt sind.</summary>
    internal static IReadOnlyList<SetupStep> VisibleSteps(LockMap editable, string engine)
    {
        var steps = new List<SetupStep>();
        if (editable["language"] || editable["desktop.uiLanguage"])
        {
            steps.Add(SetupStep.Language);
        }
        if (editable["desktop.microphone"])
        {
            steps.Add(SetupStep.Microphone);
        }
        var wayOpen = engine == "offline"
            ? editable["offline.model"]
            : editable["stt.providerId"] || editable["secrets." + SecretNames.Stt];
        if (editable["engine"] || wayOpen)
        {
            steps.Add(SetupStep.Recognition);
        }
        if (editable["refine.stage"])
        {
            steps.Add(SetupStep.Stage);
        }
        // Hotkey-Test bzw. Desktop-Kürzel (Inhalt je OS) ändern keine Einstellung, der Autostart ist keine: beide bleiben.
        steps.Add(SetupStep.Hotkey);
        steps.Add(SetupStep.Autostart);
        steps.Add(SetupStep.Done);
        return steps;
    }

    [RelayCommand(CanExecute = nameof(CanNext))]
    public void Next() => Go(Index + 1);

    [RelayCommand(CanExecute = nameof(CanBack))]
    public void Back() => Go(Index - 1);

    private void Go(int index)
    {
        if (index < 0 || index >= Steps.Count)
        {
            return;
        }
        StopMic(); // Verlassen des Mikrofon-Schritts beendet den Pegel-Test
        Join?.Deactivate();
        Current = Steps[index];
        if (Current == SetupStep.Company)
        {
            Join?.Activate();
        }
    }

    /// <summary>Überspringen: nichts speichern, Oberflächensprache zurück, als erledigt markieren, schließen.</summary>
    [RelayCommand]
    public void Skip()
    {
        StopMic();
        Join?.Deactivate();
        Loc.Instance.SetLanguage(_uiLanguageBefore);
        MarkDoneAndClose();
    }

    /// <summary>Fertig: Einstellungen speichern, Autostart setzen, als erledigt markieren, schließen.</summary>
    [RelayCommand]
    public async Task Finish()
    {
        StopMic();
        Join?.Deactivate();
        Status = "";
        if (NeedsPromptEnabled(Settings))
        {
            Settings.PromptEnabled = true;
        }
        await Settings.Save();
        if (Settings.SaveStatus != Loc.Instance["Settings_Saved"])
        {
            Status = Settings.SaveStatus; // z. B. Anbieter von IT nicht freigegeben: Assistent bleibt offen
            return;
        }
        if (_autostart is not null && AutostartEnabled != _autostart.IsEnabled)
        {
            try
            {
                if (AutostartEnabled)
                {
                    _autostart.Enable();
                }
                else
                {
                    _autostart.Disable();
                }
            }
            catch (IOException)
            {
                Status = Loc.Instance["Setup_AutostartError"];
                return;
            }
        }
        MarkDoneAndClose();
    }

    private void MarkDoneAndClose()
    {
        try
        {
            _state.MarkDone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Dann erscheint der Assistent beim nächsten Start erneut; schließen trotzdem.
        }
        CloseRequested?.Invoke();
    }

    private void StopMic()
    {
        if (Mic is { Running: true } mic)
        {
            mic.Stop();
        }
    }

    private void OnLocalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalViewModel.Engine))
        {
            OnPropertyChanged(nameof(ShowOnline));
            OnPropertyChanged(nameof(ShowOffline));
        }
        else if (e.PropertyName == nameof(LocalViewModel.SelectedModel))
        {
            OnPropertyChanged(nameof(SelectedModel));
        }
        if (e.PropertyName == nameof(LocalViewModel.Engine))
        {
            OnPropertyChanged(nameof(RecognitionFooter));
        }
    }

    /// <summary>Die gewählte Oberflächensprache gilt sofort, damit der Rest des Assistenten schon darin erscheint.</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.UiLanguage))
        {
            Loc.Instance.SetLanguage(Settings.UiLanguage.Value);
            RaiseTexts();
        }
    }

    /// <summary>Stufe Prompt wirkt nur mit „Stufe Prompt anbieten“, sonst rechnet die App mit Verschönern.</summary>
    internal static bool NeedsPromptEnabled(SettingsViewModel s) =>
        s.Stage.Value == RefineMode.Prompt.Key() && !s.PromptEnabled && s.Editable["refine.promptEnabled"];
}
