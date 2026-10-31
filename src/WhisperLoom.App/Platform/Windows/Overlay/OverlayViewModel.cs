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
/// Overlay der Umschalt-Kombi: Zustand, Dauer, Pegel, Stufe und die Knöpfe Pause/Weiter, Verwerfen, Senden,
/// Senden + Enter. Die Knöpfe rufen nur die Rückrufe; den Ablauf steuert der <c>WindowsInputService</c>.
/// </summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly Action _pauseResume;
    private readonly Action _discard;
    private readonly Action<bool> _send;

    public OverlayViewModel(Action pauseResume, Action discard, Action<bool> send)
    {
        _pauseResume = pauseResume;
        _discard = discard;
        _send = send;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(IsRecording), nameof(IsPaused), nameof(CanAct), nameof(IsError), nameof(PauseText))]
    private OverlayState _state;

    [ObservableProperty]
    private TimeSpan _elapsed;

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    private string _stage = "";

    /// <summary>Lokalisierungs-Schlüssel des Fehlers (<c>Error_*</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private string? _errorKey;

    public bool IsRecording => State == OverlayState.Recording;

    public bool IsPaused => State == OverlayState.Paused;

    public bool IsError => State == OverlayState.Error;

    /// <summary>Knöpfe und Overlay-Tasten wirken nur in Aufnahme und Pause.</summary>
    public bool CanAct => State is OverlayState.Recording or OverlayState.Paused;

    public string StateText => State switch
    {
        OverlayState.Recording => Loc.Instance["Overlay_Recording"],
        OverlayState.Paused => Loc.Instance["Overlay_Paused"],
        OverlayState.Processing => Loc.Instance["Overlay_Processing"],
        _ => Loc.Instance[ErrorKey ?? "Overlay_Error"],
    };

    public string PauseText => Loc.Instance[IsPaused ? "Action_Resume" : "Action_Pause"];

    public string ElapsedText => $"{(int)Elapsed.TotalMinutes}:{Elapsed.Seconds:00}";

    partial void OnElapsedChanged(TimeSpan value) => OnPropertyChanged(nameof(ElapsedText));

    [RelayCommand]
    private void PauseResume() => _pauseResume();

    [RelayCommand]
    private void Discard() => _discard();

    [RelayCommand]
    private void Send() => _send(false);

    [RelayCommand]
    private void SendEnter() => _send(true);
}
