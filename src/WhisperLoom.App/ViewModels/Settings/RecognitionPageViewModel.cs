using System.ComponentModel;
using System.Globalization;
using WhisperLoom.App.Design;
using WhisperLoom.Core.Api;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „Erkennung“: online und lokal auf einer Seite (Art, Sprache, Online-Anbieter, lokales Modell), Kontext-Wörter,
/// Ersatzanbieter. Modelle als Auswahl aus providers.json mit „Eigenes Modell …“.
/// </summary>
public sealed class RecognitionPageViewModel : SettingsPageViewModel
{
    public const int MinTimeout = 30;
    public const int MaxTimeout = 1800;

    public RecognitionPageViewModel(SettingsViewModel session) : base("recognition", "Recognition", "Red", session)
    {
        SttModel = Picker(SttOptions(session.SttProvider.Value), () => session.SttModel, v => session.SttModel = v);
        SttFallbackModel = Picker(SttOptions(session.SttFallbackProvider.Value), () => session.SttFallbackModel, v => session.SttFallbackModel = v);
        AdvancedOpen = NeedsUrl(session.SttProvider.Value);
        FallbackAdvancedOpen = NeedsUrl(session.SttFallbackProvider.Value);
        session.PropertyChanged += OnSessionChanged;
    }

    public LocalViewModel Local => Session.Local;

    public CustomChoiceViewModel SttModel { get; }

    public CustomChoiceViewModel SttFallbackModel { get; }

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
            SttModel.SetOptions(SttOptions(Session.SttProvider.Value));
        }
        else if (e.PropertyName == nameof(SettingsViewModel.SttFallbackProvider))
        {
            SttFallbackModel.SetOptions(SttOptions(Session.SttFallbackProvider.Value));
        }
    }

    private static IEnumerable<Choice> SttOptions(string providerId)
    {
        var models = ProviderCatalog.ById(providerId).SttModels ?? [];
        return ModelChoices(models, Recommended(models));
    }
}
