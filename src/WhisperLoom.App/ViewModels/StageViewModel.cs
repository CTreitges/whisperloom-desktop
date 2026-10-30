using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// Bühnenansicht: Aufnahme-Indikator mit Timer und Pegel, links Rohtext, rechts Ergebnis in Großschrift,
/// Stufe und Sprache sichtbar. Zoom über <see cref="ZoomIn"/>/<see cref="ZoomOut"/> (Strg+Plus/Minus),
/// gespeichert in den Desktop-Einstellungen.
/// </summary>
public sealed partial class StageViewModel : ObservableObject
{
    public const double MinFontSize = 16;
    public const double MaxFontSize = 96;
    public const double ZoomStep = 4;

    public StageViewModel(DictationController controller)
    {
        Controller = controller;
        _fontSize = Math.Clamp(SettingsStore.Desktop(controller.Settings).StageFontSize, MinFontSize, MaxFontSize);
        controller.PropertyChanged += OnControllerChanged;
        Loc.Instance.PropertyChanged += (_, _) => RefreshTexts();
    }

    public DictationController Controller { get; }

    [ObservableProperty]
    private double _fontSize;

    [ObservableProperty]
    private string _elapsedText = "00:00";

    [ObservableProperty]
    private double _level;

    public bool IsRecording => Controller.IsRecording;

    public bool IsPaused => Controller.State == DictationState.Paused;

    public bool IsProcessing => Controller.IsBusy;

    public string RawText => Controller.LastRaw;

    public string ResultText => Controller.LastResult;

    public string StageText => Loc.Instance["Stage_" + Controller.Stage.Key()];

    public string LanguageText => Controller.Language == "auto" ? Loc.Instance["Language_auto"] : Controller.Language.ToUpperInvariant();

    public string StateText => Loc.Instance["State_" + Controller.State.ToString().ToLowerInvariant()];

    public string ToggleText => Loc.Instance[Controller.IsRecording ? "Action_Stop" : "Action_Start"];

    public string PauseText => Loc.Instance[IsPaused ? "Action_Resume" : "Action_Pause"];

    public string? ErrorText => Controller.LastErrorKey is { } key ? Loc.Instance[key] : null;

    /// <summary>Vom Fenster-Timer (100 ms) aufgerufen: Timer und Pegel nachziehen.</summary>
    public void Tick()
    {
        var elapsed = Controller.Elapsed;
        ElapsedText = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        Level = Controller.Level;
    }

    [RelayCommand]
    private Task Toggle() => Controller.ToggleAsync();

    [RelayCommand]
    private void PauseResume()
    {
        if (IsPaused)
        {
            Controller.Resume();
        }
        else
        {
            Controller.Pause();
        }
    }

    [RelayCommand]
    private void NextStage() => Controller.SetStage("next");

    [RelayCommand]
    public void ZoomIn() => SetFontSize(FontSize + ZoomStep);

    [RelayCommand]
    public void ZoomOut() => SetFontSize(FontSize - ZoomStep);

    public void ZoomReset() => SetFontSize(DesktopSettings.DefaultStageFontSize);

    private void SetFontSize(double size)
    {
        FontSize = Math.Clamp(size, MinFontSize, MaxFontSize);
        var settings = Controller.Settings;
        var desktop = SettingsStore.Desktop(settings);
        desktop.StageFontSize = FontSize;
        SettingsStore.SetDesktop(settings, desktop);
        _ = Controller.ApplySettingsAsync(settings);
    }

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e) => RefreshTexts();

    private void RefreshTexts()
    {
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(RawText));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(StageText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ToggleText));
        OnPropertyChanged(nameof(PauseText));
        OnPropertyChanged(nameof(ErrorText));
        Tick();
    }
}
