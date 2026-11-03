using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Setup;

/// <summary>Zustand eines Eintrags der Schrittliste.</summary>
public enum StepState
{
    Done,
    Current,
    Upcoming,
}

/// <summary>
/// Eintrag der Schrittliste links (UX-3, einrichtung.html Befund 1): erledigt (Häkchen, anklickbar zurück), aktuell
/// (markiert), kommend (nur Nummer und Name).
/// </summary>
public sealed class SetupStepItem(SetupStep step, int number, StepState state, IRelayCommand go)
{
    public SetupStep Step { get; } = step;

    public int Number { get; } = number;

    public string NumberText => Number.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public string Label { get; } = Loc.Instance["Setup_Short_" + step];

    public StepState State { get; } = state;

    public bool IsDone => State == StepState.Done;

    public bool IsCurrent => State == StepState.Current;

    public bool IsUpcoming => State == StepState.Upcoming;

    /// <summary>Name des Knopfs eines erledigten Schritts: „Zurück zu Schritt 2: Sprache“.</summary>
    public string GoName => Loc.Instance.Format("Setup_GoTo", Number, Label);

    /// <summary>Name für Screenreader bei aktuellem und kommendem Schritt.</summary>
    public string Name => IsCurrent ? Loc.Instance.Format("Setup_CurrentStep", Number, Label) : Loc.Instance.Format("Setup_UpcomingStep", Number, Label);

    public IRelayCommand GoCommand { get; } = go;
}

/// <summary>Schrittliste, Nutzungswahl, Pegel-Knopf, „Später fortsetzen“ und Abmelden beim Schließen (UX-3).</summary>
public sealed partial class SetupViewModel : IDisposable
{
    /// <summary>Schritte, die nur mit offener Richtlinie erscheinen; fehlen sie, nennt der Hinweis sie.</summary>
    private static readonly SetupStep[] Optional = [SetupStep.Language, SetupStep.Microphone, SetupStep.Recognition, SetupStep.Stage];

    private bool _disposed;

    /// <summary>Alle Schritte dieses Pfads mit Zustand; neu bei jedem Schritt- oder Sprachwechsel.</summary>
    public IReadOnlyList<SetupStepItem> StepItems =>
        [.. Steps.Select((step, i) => new SetupStepItem(step, i + 1,
            i < Index ? StepState.Done : i == Index ? StepState.Current : StepState.Upcoming, GoToCommand))];

    /// <summary>Über der Liste: „Für mich · 7 Schritte“, mit Firma „Mit Firma · 9 Schritte“, ohne Firmen-Pfad nur die Zahl.</summary>
    public string StepsCaption
    {
        get
        {
            var count = Loc.Instance.Format("Setup_StepCount", Steps.Count);
            return Steps.Contains(SetupStep.Usage)
                ? Loc.Instance[UseCompany ? "Setup_PathCompany" : "Setup_PathPersonal"] + " · " + count
                : count;
        }
    }

    /// <summary>Name des Hilfe-Knopfs neben dem Titel: „Hilfe zu „Sprache“ (F1)“.</summary>
    public string HelpName => Loc.Instance.Format("Setup_HelpName", Title);

    /// <summary>Einleitung des Schritts unter dem Titel; leer bei „Fertig“.</summary>
    public string Intro => Current == SetupStep.Hotkey && !Windows ? Loc.Instance["Setup_LinuxIntroShort"] : Loc.Instance["Setup_Intro_" + Current];

    public bool HasIntro => Intro.Length > 0;

    /// <summary>
    /// Hinweis, welche Schritte die IT festgelegt hat und die darum entfallen („…; sie entfallen: Erkennung, Stufe.“);
    /// leer, wenn keiner entfällt (B36: ehrlicher Fortschritt statt springender Zahl).
    /// </summary>
    public string SkippedStepsText
    {
        get
        {
            if (!Settings.HasPolicy)
            {
                return "";
            }
            var skipped = Optional.Where(s => !Steps.Contains(s)).Select(s => Loc.Instance["Setup_Short_" + s]).ToList();
            return skipped.Count == 0 ? "" : Loc.Instance.Format("Setup_PolicyHint", string.Join(", ", skipped));
        }
    }

    /// <summary>Ein Knopf für den Pegel-Test: „Pegel testen“ bzw. während des Tests „Test beenden“ (Befund 9).</summary>
    public string MicButtonText => Loc.Instance[Mic is { Running: true } ? "Setup_MicStop" : "Setup_MicStart"];

    public bool MicRunning => Mic is { Running: true };

    /// <summary>Fußzeile unter der Karte im Schritt Erkennung, je nach Weg.</summary>
    public string RecognitionFooter => Loc.Instance[ShowOnline ? "Setup_RecognitionFootOnline" : "Setup_RecognitionFootLocal"];

    /// <summary>Wahl im ersten Schritt als Optionsfelder mit Erklärung.</summary>
    public IReadOnlyList<Choice> UsageChoices { get; } =
    [
        new("personal", Loc.Instance["Setup_UsagePersonal"], Loc.Instance["Setup_UsagePersonalDesc"]),
        new("company", Loc.Instance["Setup_UsageCompany"], Loc.Instance["Setup_UsageCompanyDesc"]),
    ];

    public Choice Usage
    {
        get => UsageChoices[UseCompany ? 1 : 0];
        set => UseCompany = value?.Value == "company";
    }

    /// <summary>Esc hat „Einrichtung später fortsetzen?“ gefragt; der Fuß zeigt die Rückfrage statt der Knöpfe.</summary>
    public bool LaterPending
    {
        get => _laterPending;
        private set
        {
            if (SetProperty(ref _laterPending, value))
            {
                OnPropertyChanged(nameof(ShowFoot));
            }
        }
    }

    private bool _laterPending;

    public bool ShowFoot => !LaterPending;

    /// <summary>„Weiter“ steht im Firmen-Schritt erst nach der Freigabe da (vorher ist „Mit Firma verbinden“ die Hauptaktion).</summary>
    public bool ShowNext => !IsLast && CanNext();

    /// <summary>Esc: Rückfrage „Später fortsetzen?“ statt sofort zu schließen (B29).</summary>
    [RelayCommand]
    public void AskLater() => LaterPending = true;

    [RelayCommand]
    public void KeepGoing() => LaterPending = false;

    /// <summary>
    /// „Später fortsetzen“: Angaben speichern, aber nicht als erledigt markieren – der Assistent erscheint beim nächsten Start
    /// wieder (bzw. unter Einstellungen › System). Autostart bleibt unverändert, bis „Fertig“.
    /// </summary>
    [RelayCommand]
    public async Task Later()
    {
        LaterPending = false;
        StopMic();
        Join?.Deactivate();
        Status = "";
        await Settings.Save();
        if (Settings.SaveStatus != Loc.Instance["Settings_Saved"])
        {
            Status = Settings.SaveStatus;
            return;
        }
        CloseRequested?.Invoke();
    }

    /// <summary>Zurück zu einem erledigten Schritt der Liste; kommende Schritte sind nicht anklickbar.</summary>
    [RelayCommand]
    private void GoTo(SetupStep step)
    {
        var index = Steps.ToList().IndexOf(step);
        if (index >= 0 && index < Index)
        {
            Go(index);
        }
    }

    [RelayCommand]
    public void ToggleMic()
    {
        if (Mic is null)
        {
            return;
        }
        if (Mic.Running)
        {
            Mic.Stop();
        }
        else
        {
            Mic.Start();
        }
    }

    /// <summary>Fenster zu: Abos auf Sprache, Arbeitskopie und Pegel-Test lösen (sonst hält <see cref="Loc.Instance"/> alles fest).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        Detach(Settings);
        if (Mic is not null)
        {
            Mic.PropertyChanged -= OnMicChanged;
        }
        Join?.Deactivate();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) =>
        RaiseTexts();

    private void RaiseTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(StepItems));
        OnPropertyChanged(nameof(StepsCaption));
        OnPropertyChanged(nameof(HelpName));
        OnPropertyChanged(nameof(Intro));
        OnPropertyChanged(nameof(SkippedStepsText));
        OnPropertyChanged(nameof(MicButtonText));
        OnPropertyChanged(nameof(RecognitionFooter));
        OnPropertyChanged(nameof(DoneHint));
    }

    private void OnMicChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MicLevelTest.Running))
        {
            OnPropertyChanged(nameof(MicButtonText));
            OnPropertyChanged(nameof(MicRunning));
        }
    }

    partial void OnCurrentChanged(SetupStep value)
    {
        LaterPending = false;
        OnPropertyChanged(nameof(StepItems));
        OnPropertyChanged(nameof(HelpName));
        OnPropertyChanged(nameof(Intro));
        OnPropertyChanged(nameof(HasIntro));
        OnPropertyChanged(nameof(ShowNext));
    }

    private void OnStepsChanged()
    {
        OnPropertyChanged(nameof(StepItems));
        OnPropertyChanged(nameof(StepsCaption));
        OnPropertyChanged(nameof(SkippedStepsText));
        OnPropertyChanged(nameof(Usage));
    }
}
