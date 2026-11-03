using System.ComponentModel;
using System.Globalization;
using WhisperLoom.App.Design;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „Erkennung“ (UX-2): Erkennungsweg (Online-/Lokale Erkennung) und Sprache, darunter nur der gewählte Weg – online
/// Anbieter, API-Key und Modellwahl (Katalog, Serverliste, „Eigenes Modell …“), lokal die Modell-Liste mit Herunterladen,
/// Löschen und eigenen Whisper-Dateien (B1–B3) –, Kontext-Wörter und der Ersatzanbieter (Zeilen erst nach dem Schalter).
/// </summary>
public sealed class RecognitionPageViewModel : SettingsPageViewModel
{
    public const int MinTimeout = 30;
    public const int MaxTimeout = 1800;

    public RecognitionPageViewModel(SettingsViewModel session) : base("recognition", "Recognition", session)
    {
        SttModelPicker = new ModelPickerViewModel(() => SttOptions("stt", session.SttProvider.Value), () => session.SttModel, v => session.SttModel = v,
            ct => session.LoadServerModelsAsync("stt", ModelKind.Stt, ct));
        SttFallbackModelPicker = new ModelPickerViewModel(() => SttOptions("sttFallback", session.SttFallbackProvider.Value), () => session.SttFallbackModel,
            v => session.SttFallbackModel = v, ct => session.LoadServerModelsAsync("sttFallback", ModelKind.Stt, ct));
        AdvancedOpen = NeedsUrl(session.SttProvider.Value);
        FallbackAdvancedOpen = NeedsUrl(session.SttFallbackProvider.Value);
        session.PropertyChanged += OnSessionChanged;
        Local.PropertyChanged += OnLocalChanged;
    }

    /// <summary>
    /// Hinweis unter „Sprache“ bei „Deutsch + Englisch“ (CTR-273): online, wer beide Sprachen bekommt; lokal, dass das
    /// Modell nur eine Sprache je Diktat erkennt. null = kein Hinweis.
    /// </summary>
    public string? LanguageHint => Session.Language.Value != WhisperLoomSettings.LanguageDeEn ? null
        : Local.Engine.Value == "offline" ? "Language_deEnOffline" : "Language_deEnHint";

    public LocalViewModel Local => Session.Local;

    /// <summary>Gewählter Erkennungsweg: online (sonst steht nur die Gruppe „Lokale Erkennung“ da).</summary>
    public bool IsOnline => Local.Engine.Value != "offline";

    public bool IsLocal => !IsOnline;

    /// <summary>Datei aus der Dateiauswahl; Fehler steht danach an der Zeile (<see cref="LocalViewModel.CustomModelError"/>).</summary>
    public string? AddCustomModel(string path) => Local.AddCustomModel(path);

    public ModelPickerViewModel SttModelPicker { get; }

    public ModelPickerViewModel SttFallbackModelPicker { get; }

    public CustomChoiceViewModel SttModel => SttModelPicker.Choice;

    public CustomChoiceViewModel SttFallbackModel => SttFallbackModelPicker.Choice;

    /// <summary>„Erweitert“ offen, wenn der Anbieter eine eigene Adresse braucht (Eigener Server, Ollama).</summary>
    public bool AdvancedOpen { get; set; }

    public bool FallbackAdvancedOpen { get; set; }

    /// <summary>Lese-Zeitlimit; 0 = Standard des Anbieters, sonst 30–1800 Sekunden.</summary>
    public string ReadTimeout
    {
        get => Session.Working.Stt.ReadTimeoutSec.ToString(CultureInfo.InvariantCulture);
        set => SetNumber("stt.readTimeoutSec", value, MinTimeout, MaxTimeout, zeroAllowed: true, v => Session.Working.Stt.ReadTimeoutSec = v);
    }

    /// <summary>Begleitprozess beenden nach 1–240 Minuten ohne Erkennung.</summary>
    public string IdleMinutes
    {
        get => Local.IdleMinutes.ToString(CultureInfo.InvariantCulture);
        set => SetNumber("local.idleMinutes", value, 1, 240, zeroAllowed: false, v => Local.IdleMinutes = v);
    }

    public override void Dispose()
    {
        Session.PropertyChanged -= OnSessionChanged;
        Local.PropertyChanged -= OnLocalChanged;
        base.Dispose();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Language))
        {
            OnPropertyChanged(nameof(LanguageHint));
        }
        else if (e.PropertyName == nameof(SettingsViewModel.SttProvider))
        {
            SttModelPicker.Refresh();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.SttFallbackProvider))
        {
            SttFallbackModelPicker.Refresh();
        }
    }

    private void OnLocalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalViewModel.Engine))
        {
            OnPropertyChanged(nameof(LanguageHint));
            OnPropertyChanged(nameof(IsOnline));
            OnPropertyChanged(nameof(IsLocal));
        }
    }

    private IEnumerable<Choice> SttOptions(string slot, string providerId)
    {
        var models = Session.Providers.ById(providerId).SttModels ?? [];
        return ModelPickerViewModel.Combine(Recommended(models), models, Session.CachedServerModels(slot, ModelKind.Stt));
    }
}
