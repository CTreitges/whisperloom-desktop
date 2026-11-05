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
public sealed partial class StageViewModel : ObservableObject, IDisposable
{
    public const double MinFontSize = 16;
    public const double MaxFontSize = 96;
    public const double ZoomStep = 4;

    /// <param name="openSettings">Öffnet die Einstellungen wie das Tray-Menü; null = Knopf „Einstellungen“ ausgeblendet.</param>
    public StageViewModel(DictationController controller, Action? openSettings = null)
    {
        Controller = controller;
        _openSettings = openSettings;
        var desktop = SettingsStore.Desktop(controller.Settings);
        _fontSize = Math.Clamp(desktop.StageFontSize, MinFontSize, MaxFontSize);
        _darkForProjector = desktop.StageDark;
        controller.PropertyChanged += OnControllerChanged;
        // Abmelden in Dispose (Fenster zu), sonst halten Loc.Instance und der Controller Bühne und Fenster fest.
        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>„Dunkel für Beamer“ (Strg+D): Bühne dunkel statt nach dem System; gespeichert wie die Schriftgröße.</summary>
    [ObservableProperty]
    private bool _darkForProjector;

    /// <summary>Textknopf im Kopf: „Stufe: Glätten“ (Klick = nächste Stufe).</summary>
    public string StageChipText => Loc.Instance.Format("Stage_Current", StageText);

    /// <summary>Etikett im Kopf: „Sprache: DE“.</summary>
    public string LanguageChipText => Loc.Instance.Format("Language_Current", LanguageText);

    /// <summary>Etikett am Ergebnis: „Glätten · KI“; leer bei Stufe Aus (dann ist das Ergebnis der bereinigte Rohtext).</summary>
    public string ResultBadge => Controller.Stage == RefineMode.Off ? "" : Loc.Instance.Format("Stage_ResultBadge", StageText);

    public bool HasResultBadge => ResultBadge.Length > 0;

    /// <summary>„Pause“ bzw. „Weiter“ nur, solange eine Aufnahme läuft oder pausiert (nichts Deaktiviertes ohne Grund).</summary>
    public bool ShowPause => IsRecording || IsPaused;

    public bool HasError => ErrorText is not null;

    public void Dispose()
    {
        Controller.PropertyChanged -= OnControllerChanged;
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
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

    public string LanguageText => SettingsViewModel.LanguageLabel(Controller.Language);

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

    private readonly Action? _openSettings;

    /// <summary>Knopf „Einstellungen“ im Kopf, nur mit Öffnen-Aufruf (in der App immer).</summary>
    public bool CanOpenSettings => _openSettings is not null;

    [RelayCommand]
    private void OpenSettings() => _openSettings?.Invoke();

    [RelayCommand]
    public void ToggleDark()
    {
        DarkForProjector = !DarkForProjector;
        var settings = Controller.Settings;
        var desktop = SettingsStore.Desktop(settings);
        desktop.StageDark = DarkForProjector;
        SettingsStore.SetDesktop(settings, desktop);
        _ = Controller.ApplySettingsAsync(settings);
    }

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

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RefreshTexts();

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
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(StageChipText));
        OnPropertyChanged(nameof(LanguageChipText));
        OnPropertyChanged(nameof(ResultBadge));
        OnPropertyChanged(nameof(HasResultBadge));
        OnPropertyChanged(nameof(ShowPause));
        Tick();
    }
}
