using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.ViewModels;

/// <summary>Ein Eintrag einer Auswahlliste: gespeicherter Wert plus Anzeige.</summary>
public sealed record Choice(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Arbeitskopie der Einstellungen für Einstellungsfenster und Einrichtungs-Assistent. Keys laufen nur über die
/// <see cref="KeyFieldViewModel"/> in den <see cref="ISecretStore"/>. Mit <see cref="AutoSave"/> (Einstellungsfenster)
/// gilt jede gültige Änderung sofort (SettingsViewModel.Commit.cs); ohne (Assistent) übernimmt erst <see cref="Save"/>.
/// Ungültige Werte (Adressen, Zahlen) landen nur in <see cref="Errors"/>, nicht in der Kopie.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> RecognitionLanguages = ["auto", "de", "en", "fr", "es", "it", "nl", "pl", "pt", "tr"];

    private readonly DictationController _controller;
    private readonly WhisperLoomSettings _s;
    private readonly DesktopSettings _desktop;
    private readonly PolicySettings _policy;
    private readonly ISecretStore _secrets;
    private readonly bool _ready;

    /// <param name="local">Lokale Teile der App (Modelle, Begleitprozess, Ollama); null = Seite „Erkennung“ ohne Downloads.</param>
    /// <param name="recordHotkey">Rekorder des globalen Hooks (Windows, Phase 3); null = Tastenkürzel nur anzeigen.</param>
    public SettingsViewModel(DictationController controller, ISecretStore secrets, IAccessChecker checker, IReadOnlyList<string>? microphones = null,
        Platform.Sidecar.LocalParts? local = null, Func<Task<string?>>? recordHotkey = null)
    {
        _controller = controller;
        _secrets = secrets;
        Checker = checker;
        _reopen = () => new SettingsViewModel(controller, secrets, checker, microphones, local, recordHotkey);
        _s = SettingsJson.Deserialize(SettingsJson.Serialize(controller.Settings));
        _desktop = SettingsStore.Desktop(_s);
        _policy = controller.Policy;
        Editable = new LockMap(_policy);
        Sync = new SyncViewModel(controller, secrets);
        SecretStoreKind = secrets.Kind;
        LocalParts = local;
        var loc = Loc.Instance;

        // Katalog, eigene Anbieter und lokale Server (SettingsViewModel.Providers.cs).
        FillProviderChoices();
        EnsureChoice(SttProviders, _s.Stt.ProviderId);
        EnsureChoice(SttProviders, _s.SttFallback.ProviderId);
        EnsureChoice(LlmProviders, _s.Llm.ProviderId);
        EnsureChoice(LlmFallbackProviders, _s.LlmFallback.ProviderId);
        Languages = [.. RecognitionLanguages.Select(l => new Choice(l, l == "auto" ? loc["Language_auto"] : l.ToUpperInvariant()))];
        Stages = [.. RefineModes.SettingsFor(true).Select(m => new Choice(m.Key(), loc["Stage_" + m.Key()]))];
        ProxyModes = [.. new[] { "system", "off", "manual" }.Select(m => new Choice(m, loc["Proxy_" + m]))];
        HistorySizes = [.. HistorySettings.Sizes];
        Outputs = [new Choice("clipboard", loc["Output_clipboard"])];
        UiLanguages = [new Choice("", loc["Ui_System"]), new Choice("de", "Deutsch"), new Choice("en", "English")];
        Microphones = [new Choice("", loc["Mic_Default"]), .. (microphones ?? []).Select(m => new Choice(m, m))];
        InsertModes = [.. new[] { "paste", "type", "clipboard" }.Select(m => new Choice(m, loc["Insert_" + m]))];
        Hotkeys = new HotkeysViewModel(_desktop, recordHotkey, _s.Selection) { Changed = Touch };
        Profiles = new Features.Profiles.ProfilesViewModel(_desktop) { Changed = Touch };

        _sttProvider = Pick(SttProviders, _s.Stt.ProviderId);
        _sttFallbackProvider = Pick(SttProviders, _s.SttFallback.ProviderId);
        _llmProvider = Pick(LlmProviders, _s.Llm.ProviderId);
        _llmFallbackProvider = Pick(LlmFallbackProviders, _s.LlmFallback.ProviderId);
        _language = Pick(Languages, _s.Language);
        _stage = Pick(Stages, DictationController.StageOf(_s.Refine).Key());
        _proxyMode = Pick(ProxyModes, _desktop.ProxyMode);
        _output = Pick(Outputs, _desktop.Output);
        _uiLanguage = Pick(UiLanguages, _desktop.UiLanguage);
        _microphone = Pick(Microphones, _desktop.Microphone);
        _insertMode = Pick(InsertModes, _desktop.InsertMode);
        _historySize = HistorySizes.Contains(_s.History.Size) ? _s.History.Size : HistorySettings.DefaultSize;

        SttKey = new KeyFieldViewModel(SecretNames.Stt, secrets, checker, () => (SttProvider.Value, SttBaseUrl)) { Editable = KeyEditable(SecretNames.Stt), Policy = _policy };
        SttFallbackKey = new KeyFieldViewModel(SecretNames.SttFallback, secrets, checker, () => (SttFallbackProvider.Value, SttFallbackBaseUrl)) { Editable = KeyEditable(SecretNames.SttFallback), Policy = _policy };
        // „wie Erkennung“: Adresse der Erkennung wie in der Auflösung (llm.baseUrl gilt dann nicht).
        LlmKey = new KeyFieldViewModel(SecretNames.Llm, secrets, checker,
            () => LlmProvider.Value == LlmSettings.Same ? (SttProvider.Value, SttBaseUrl) : (LlmProvider.Value, LlmBaseUrl)) { Editable = KeyEditable(SecretNames.Llm), Policy = _policy };
        LlmFallbackKey = new KeyFieldViewModel(SecretNames.LlmFallback, secrets, checker, () => (LlmFallbackProvider.Value, LlmFallbackBaseUrl)) { Editable = KeyEditable(SecretNames.LlmFallback), Policy = _policy };
        Local = new LocalViewModel(_s, local);
        Local.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LocalViewModel.Engine) or nameof(LocalViewModel.Backend) or nameof(LocalViewModel.Rule)
                or nameof(LocalViewModel.SelectedModel) or nameof(LocalViewModel.OllamaModel) or nameof(LocalViewModel.Accurate)
                or nameof(LocalViewModel.IdleMinutes) or nameof(LocalViewModel.OllamaUrl) or nameof(LocalViewModel.TextEngine)
                or nameof(LocalViewModel.BundledModel) or nameof(LocalViewModel.TextServer))
            {
                Touch();
            }
        };
        // Eigene Whisper-Dateien und lokale Server (W2-LOCAL): Anbieterlisten neu, dann speichern.
        Local.EntriesChanged += () =>
        {
            RefreshProviders();
            Touch();
        };
        _baseline = BuildResult(); // normalisiert (Abschnitte desktop/local vollständig), damit der erste Commit nur echte Änderungen überträgt
        _ready = true;
    }

    /// <summary>Welche Felder änderbar sind (Einrichtungs-Assistent); das Einstellungsfenster blendet Festes aus.</summary>
    public LockMap Editable { get; }

    /// <summary>Hinweis „von IT festgelegt“ zeigen.</summary>
    public bool HasPolicy => _policy.IsActive;

    public PolicySettings Policy => _policy;

    public SyncViewModel Sync { get; }

    /// <summary>Inline-Fehler je Pfad (ungültig, nicht gespeichert).</summary>
    public ErrorMap Errors { get; } = new();

    private bool KeyEditable(string name) => !_policy.IsLocked("secrets." + name);

    /// <summary>Lokale Erkennung (WLD-P4b): Modelle, Rechenweg, Ollama.</summary>
    public LocalViewModel Local { get; }

    internal Platform.Sidecar.LocalParts? LocalParts { get; }

    /// <summary>Arbeitskopie für Seiten-ViewModels, die Felder ohne eigene Eigenschaft hier binden (Bereinigung, Stufen-Optionen).</summary>
    internal WhisperLoomSettings Working => _s;

    internal DesktopSettings Desktop => _desktop;

    public ObservableCollection<Choice> SttProviders { get; } = [];
    public ObservableCollection<Choice> LlmProviders { get; } = [];
    public ObservableCollection<Choice> LlmFallbackProviders { get; } = [];
    public IReadOnlyList<Choice> Languages { get; }
    public IReadOnlyList<Choice> Stages { get; }
    public IReadOnlyList<Choice> ProxyModes { get; }
    public IReadOnlyList<int> HistorySizes { get; }
    public IReadOnlyList<Choice> Outputs { get; }
    public IReadOnlyList<Choice> UiLanguages { get; }
    public IReadOnlyList<Choice> Microphones { get; }
    public IReadOnlyList<Choice> InsertModes { get; }

    /// <summary>Windows: Tastenkürzel (Phase 3).</summary>
    public HotkeysViewModel Hotkeys { get; }

    /// <summary>App-Profile (nur Windows, WLD-P5b).</summary>
    public Features.Profiles.ProfilesViewModel Profiles { get; }

    public KeyFieldViewModel SttKey { get; }
    public KeyFieldViewModel SttFallbackKey { get; }
    public KeyFieldViewModel LlmKey { get; }
    public KeyFieldViewModel LlmFallbackKey { get; }

    public string SecretStoreKind { get; }

    /// <summary>Hinweis, wenn Keys nur in einer Datei (600) liegen statt im Schlüsselbund.</summary>
    public bool SecretStoreIsFile => SecretStoreKind == "Datei";

    // Erkennung
    [ObservableProperty] private Choice _sttProvider;
    public string SttBaseUrl { get => _s.Stt.BaseUrl; set => SetChecked("stt.baseUrl", value.Trim(), v => SettingsValidation.BaseUrl(v, SttProvider.Value), v => _s.Stt.BaseUrl = v); }
    public string SttModel { get => _s.Stt.Model; set => SetPlain(() => _s.Stt.Model = value.Trim()); }
    [ObservableProperty] private Choice _language;
    public bool SttFallbackEnabled { get => _s.SttFallback.Enabled; set => SetPlain(() => _s.SttFallback.Enabled = value); }
    [ObservableProperty] private Choice _sttFallbackProvider;
    public string SttFallbackBaseUrl { get => _s.SttFallback.BaseUrl; set => SetChecked("sttFallback.baseUrl", value.Trim(), v => SettingsValidation.BaseUrl(v, SttFallbackProvider.Value), v => _s.SttFallback.BaseUrl = v); }
    public string SttFallbackModel { get => _s.SttFallback.Model; set => SetPlain(() => _s.SttFallback.Model = value.Trim()); }

    // Text
    [ObservableProperty] private Choice _stage;
    public bool PromptEnabled { get => _s.Refine.PromptEnabled; set => SetPlain(() => _s.Refine.PromptEnabled = value); }
    [ObservableProperty] private Choice _llmProvider;
    public string LlmBaseUrl { get => _s.Llm.BaseUrl; set => SetChecked("llm.baseUrl", value.Trim(), v => SettingsValidation.BaseUrl(v, LlmUrlProvider), v => _s.Llm.BaseUrl = v); }
    public string LlmModel { get => _s.Llm.Model; set => SetPlain(() => _s.Llm.Model = value.Trim()); }
    public string ModelPolish { get => StageModel("polish"); set => SetStageModel("polish", value); }
    public string ModelBeautify { get => StageModel("beautify"); set => SetStageModel("beautify", value); }
    public string ModelSummarize { get => StageModel("summarize"); set => SetStageModel("summarize", value); }
    public string ModelPrompt { get => StageModel("prompt"); set => SetStageModel("prompt", value); }
    public bool LlmFallbackEnabled { get => _s.LlmFallback.Enabled; set => SetPlain(() => _s.LlmFallback.Enabled = value); }
    [ObservableProperty] private Choice _llmFallbackProvider;
    public string LlmFallbackBaseUrl { get => _s.LlmFallback.BaseUrl; set => SetChecked("llmFallback.baseUrl", value.Trim(), v => SettingsValidation.BaseUrl(v, LlmFallbackProvider.Value), v => _s.LlmFallback.BaseUrl = v); }
    public string LlmFallbackModel { get => _s.LlmFallback.Model; set => SetPlain(() => _s.LlmFallback.Model = value.Trim()); }

    // Kontext-Wörter
    public string Vocabulary { get => _s.Stt.Vocabulary; set => SetPlain(() => _s.Stt.Vocabulary = value); }
    public string VocabularyFile { get => _s.Stt.VocabularyFile; set => SetPlain(() => _s.Stt.VocabularyFile = value.Trim()); }

    // Netzwerk
    [ObservableProperty] private Choice _proxyMode;
    public string ProxyUrl { get => _desktop.ProxyUrl; set => SetChecked("desktop.proxyUrl", value.Trim(), SettingsValidation.ProxyUrl, v => _desktop.ProxyUrl = v); }

    // Datenschutz (Ausschalten im Fenster erst nach Rückfrage, weil es den Verlauf löscht: SettingsViewModel.Commit.cs)
    [ObservableProperty] private int _historySize;

    // Ausgabe und Oberfläche
    [ObservableProperty] private Choice _output;
    [ObservableProperty] private Choice _microphone;
    [ObservableProperty] private Choice _uiLanguage;
    [ObservableProperty] private Choice _insertMode;

    [ObservableProperty] private string _saveStatus = "";

    public Task LoadKeysAsync() =>
        Task.WhenAll(SttKey.LoadAsync(), SttFallbackKey.LoadAsync(), LlmKey.LoadAsync(), LlmFallbackKey.LoadAsync());

    /// <summary>Anbieter, gegen den llm.baseUrl geprüft wird („wie Erkennung“ = Erkennungs-Anbieter).</summary>
    private string LlmUrlProvider => LlmProvider.Value == LlmSettings.Same ? SttProvider.Value : LlmProvider.Value;

    /// <summary>Übernimmt die ganze Kopie: App, settings.json und Oberflächensprache. „Verlauf aus“ leert den Verlauf.</summary>
    [RelayCommand]
    public async Task Save()
    {
        var result = BuildResult();
        if (await ApplyAsync(result))
        {
            _baseline = result;
        }
    }

    private string StageModel(string stage) => _s.Llm.StageModels.GetValueOrDefault(stage, "");

    private void SetStageModel(string stage, string value) => SetPlain(() =>
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _s.Llm.StageModels.Remove(stage);
        }
        else
        {
            _s.Llm.StageModels[stage] = value.Trim();
        }
    });

    // Auswahllisten: jede Änderung zählt sofort (AutoSave).
    // Beim Neuaufbau der Listen (RefreshProviders) setzt die ComboBox kurz null: nicht speichern.
    partial void OnSttProviderChanged(Choice value) => ProviderChanged(value);
    partial void OnSttFallbackProviderChanged(Choice value) => ProviderChanged(value);
    partial void OnLlmProviderChanged(Choice value) => ProviderChanged(value);
    partial void OnLlmFallbackProviderChanged(Choice value) => ProviderChanged(value);

    private void ProviderChanged(Choice? value)
    {
        if (_refreshingProviders || value is null)
        {
            return;
        }
        NotifyEntryUse();
        Touch();
    }
    partial void OnLanguageChanged(Choice value) => Touch();
    partial void OnStageChanged(Choice value) => Touch();
    partial void OnProxyModeChanged(Choice value) => Touch();
    partial void OnHistorySizeChanged(int value) => Touch();
    partial void OnOutputChanged(Choice value) => Touch();
    partial void OnMicrophoneChanged(Choice value) => Touch();
    partial void OnUiLanguageChanged(Choice value) => Touch();
    partial void OnInsertModeChanged(Choice value) => Touch();

    internal static Choice Pick(IReadOnlyList<Choice> choices, string value) =>
        choices.FirstOrDefault(c => c.Value == value) ?? choices[0];
}
