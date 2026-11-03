using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Platform.Windows.Overlay;

public enum OverlayState
{
    Recording,
    Paused,
    Processing,
    Error,
}

/// <summary>
/// Overlay der Umschalt-Kombi (k3.html): Zustand mit Symbol und Wort, Dauer, Pegel, Stufe (Klick öffnet den Stufen-Wähler) und die
/// Knöpfe Pause/Weiter, Verwerfen, Senden; nach einem Fehler Verwerfen und Erneut senden, beim Verarbeiten „Ohne KI einfügen“.
/// „Senden + Enter“ gibt es nicht mehr (W2-OUT): Enter danach ist eine Einstellung. Die Knöpfe rufen nur die Rückrufe; den
/// Ablauf steuert <c>InputFlow</c>.
/// </summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly Action _pauseResume;
    private readonly Action _discard;
    private readonly Action _send;

    public OverlayViewModel(Action pauseResume, Action discard, Action send)
    {
        _pauseResume = pauseResume;
        _discard = discard;
        _send = send;
        Loc.Instance.PropertyChanged += (_, _) => RaiseTexts();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(IsRecording), nameof(IsPaused), nameof(CanAct), nameof(IsError), nameof(PauseText),
        nameof(IsProcessing), nameof(ShowLevel), nameof(Moving), nameof(Detail), nameof(KeysTemplate), nameof(Keys), nameof(AccessibleName),
        nameof(ShowDiscard), nameof(ShowRetry), nameof(ShowSkip))]
    private OverlayState _state;

    [ObservableProperty]
    private TimeSpan _elapsed;

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StageText))]
    private string _stage = "";

    /// <summary>Stufen-Wähler des laufenden Diktats (W2-OUT); null = keine Wahl möglich (z. B. Umschreiben, IT-Sperre).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPickStage), nameof(KeysTemplate))]
    private Design.StagePickerViewModel? _stagePicker;

    [ObservableProperty]
    private bool _isStagePickerOpen;

    public bool CanPickStage => StagePicker is not null;

    /// <summary>Wiederholbarer Fehler: Knopf „Erneut senden“ (Audio liegt nur im Speicher, W2-OUT).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(ShowRetry))]
    private bool _canRetry;

    /// <summary>Rückruf für „Erneut senden“; null = kein Knopf.</summary>
    public Action? Retry { get; set; }

    /// <summary>Rückruf für „Ohne KI einfügen“ beim Verarbeiten (wie Esc, W3-NEXT); null = kein Knopf.</summary>
    public Action? SkipRefine { get; set; }

    /// <summary>Rückruf für „Verwerfen“ nach einem Fehler: Overlay zu, gemerktes Audio weg; null = kein Knopf.</summary>
    public Action? DismissError { get; set; }

    /// <summary>Die Textverbesserung läuft (erst dann wirkt „Ohne KI einfügen“).</summary>
    [ObservableProperty]
    private bool _canSkip;

    /// <summary>Diktat einer Anweisung zum Umschreiben der Markierung (markierung.html Befund 7): eigene Zustandswörter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(AccessibleName), nameof(ShowSkip), nameof(KeysTemplate))]
    private bool _isRewrite;

    public bool IsProcessing => State == OverlayState.Processing;

    /// <summary>Ein voriges Diktat wird noch verarbeitet, während diese Aufnahme läuft (W3-NEXT): Hinweis mit Esc.</summary>
    [ObservableProperty]
    private bool _previousPending;

    /// <summary>Lokalisierungs-Schlüssel des Fehlers (<c>Error_*</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(AccessibleName))]
    private string? _errorKey;

    public bool IsRecording => State == OverlayState.Recording;

    public bool IsPaused => State == OverlayState.Paused;

    public bool IsError => State == OverlayState.Error;

    /// <summary>Knöpfe und Overlay-Tasten wirken nur in Aufnahme und Pause.</summary>
    public bool CanAct => State is OverlayState.Recording or OverlayState.Paused;

    /// <summary>
    /// Bewegung erlaubt (Systemeinstellung „Animationen anzeigen“, Richtlinie 02 §2.7); aus = beim Verarbeiten kein laufender
    /// Balken, nur das ruhende Symbol.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Moving), nameof(ShowLevel))]
    private bool _motion = true;

    /// <summary>Pegel bzw. Lauf-Balken; nach einem Fehler gibt es nichts zu messen, beim Verarbeiten ohne Bewegung auch nicht.</summary>
    public bool ShowLevel => State != OverlayState.Error && (Motion || State != OverlayState.Processing);

    /// <summary>Lauf-Balken (nur beim Verarbeiten und nur mit Bewegung).</summary>
    public bool Moving => Motion && State == OverlayState.Processing;

    public bool ShowDiscard => CanAct || (IsError && DismissError is not null);

    public bool ShowRetry => IsError && CanRetry && Retry is not null;

    /// <summary>„Ohne KI einfügen“ nur beim Diktat: Umschreiben läuft nicht über die überspringbare Verbesserung (Prüfer-Fund).</summary>
    public bool ShowSkip => IsProcessing && !IsRewrite && SkipRefine is not null;

    public string StateText => State switch
    {
        OverlayState.Recording => Loc.Instance[IsRewrite ? "Overlay_Instruction" : "Overlay_Recording"],
        OverlayState.Paused => Loc.Instance["Overlay_Paused"],
        OverlayState.Processing => Loc.Instance[IsRewrite ? "Overlay_Rewriting" : "Overlay_Processing"],
        _ => Loc.Instance[ErrorKey ?? "Overlay_Error"],
    };

    /// <summary>Name für Screenreader (overlay.html Befund 8): „WhisperLoom – Diktat: Aufnahme läuft“.</summary>
    public string AccessibleName => Loc.Instance.Format("Overlay_Name", StateText);

    /// <summary>Satz unter dem Zustand: nach einem wiederholbaren Fehler, was mit dem Diktat passiert (Befund 6); sonst leer.</summary>
    public string? Detail => State == OverlayState.Error && CanRetry ? Loc.Instance["Overlay_ErrorKept"] : null;

    public string StageText => Loc.Instance.Format("Overlay_StageButton", Stage);

    public string PauseText => Loc.Instance[IsPaused ? "Action_Resume" : "Action_Pause"];

    public string ElapsedText => $"{(int)Elapsed.TotalMinutes}:{Elapsed.Seconds:00}";

    /// <summary>
    /// Tastenwege des Zustands als Tastenkappen (Befund 2), nur die, die wirklich wirken: in Aufnahme und Pause Enter und
    /// Rücktaste (Overlay-Tasten des Hooks), beim Verarbeiten Esc. Nach einem Fehler sind die Overlay-Tasten frei, beim
    /// Umschreiben wirkt Esc nicht: keine Zeile.
    /// </summary>
    public string? KeysTemplate => State switch
    {
        OverlayState.Recording or OverlayState.Paused => Loc.Instance[CanPickStage ? "Overlay_Keys" : "Overlay_KeysNoStage"],
        OverlayState.Processing when !IsRewrite => Loc.Instance["Overlay_KeysProcessing"],
        _ => null,
    };

    public IReadOnlyList<string> Keys => State == OverlayState.Processing
        ? [Loc.Instance["Keycap_Esc"]]
        : [Loc.Instance["Keycap_Enter"], Loc.Instance["Keycap_Backspace"]];

    partial void OnElapsedChanged(TimeSpan value) => OnPropertyChanged(nameof(ElapsedText));

    private void RaiseTexts()
    {
        foreach (var name in new[] { nameof(StateText), nameof(AccessibleName), nameof(Detail), nameof(StageText), nameof(PauseText), nameof(KeysTemplate), nameof(Keys) })
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    private void PauseResume() => _pauseResume();

    /// <summary>Verwerfen: in Aufnahme und Pause das Diktat, nach einem Fehler das gemerkte Audio.</summary>
    [RelayCommand]
    private void Discard()
    {
        if (State == OverlayState.Error)
        {
            DismissError?.Invoke();
        }
        else
        {
            _discard();
        }
    }

    [RelayCommand]
    private void Send() => _send();

    [RelayCommand]
    private void RetrySend() => Retry?.Invoke();

    [RelayCommand]
    private void Skip() => SkipRefine?.Invoke();

    [RelayCommand]
    private void ToggleStages() => IsStagePickerOpen = StagePicker is not null && !IsStagePickerOpen;
}
