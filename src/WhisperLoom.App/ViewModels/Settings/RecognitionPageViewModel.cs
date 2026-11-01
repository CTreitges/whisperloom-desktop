using System.ComponentModel;
using System.Globalization;
using WhisperLoom.App.Design;
using WhisperLoom.Core.Api;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „Erkennung“: online und lokal auf einer Seite (Art, Sprache, Anbieter inkl. eigener Anbieter und lokaler Server,
/// lokales Modell inkl. eigener Whisper-Dateien), Kontext-Wörter, Ersatzanbieter. Modelle als Auswahl aus Katalog,
/// Serverliste („Vom Server laden“) und „Eigenes Modell …“. Downloads stehen auf der Seite „Lokale Modelle“.
/// </summary>
public sealed class RecognitionPageViewModel : SettingsPageViewModel
{
    public const int MinTimeout = 30;
    public const int MaxTimeout = 1800;

    public RecognitionPageViewModel(SettingsViewModel session) : base("recognition", "Recognition", "Red", session)
    {
        SttModelPicker = new ModelPickerViewModel(() => SttOptions("stt", session.SttProvider.Value), () => session.SttModel, v => session.SttModel = v,
            ct => session.LoadServerModelsAsync("stt", ModelKind.Stt, ct));
        SttFallbackModelPicker = new ModelPickerViewModel(() => SttOptions("sttFallback", session.SttFallbackProvider.Value), () => session.SttFallbackModel,
            v => session.SttFallbackModel = v, ct => session.LoadServerModelsAsync("sttFallback", ModelKind.Stt, ct));
        AdvancedOpen = NeedsUrl(session.SttProvider.Value);
        FallbackAdvancedOpen = NeedsUrl(session.SttFallbackProvider.Value);
        session.PropertyChanged += OnSessionChanged;
    }

    public LocalViewModel Local => Session.Local;

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
        base.Dispose();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SttProvider))
        {
            SttModelPicker.Refresh();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.SttFallbackProvider))
        {
            SttFallbackModelPicker.Refresh();
        }
    }

    private IEnumerable<Choice> SttOptions(string slot, string providerId)
    {
        var models = Session.Providers.ById(providerId).SttModels ?? [];
        return ModelPickerViewModel.Combine(Recommended(models), models, Session.CachedServerModels(slot, ModelKind.Stt));
    }
}
