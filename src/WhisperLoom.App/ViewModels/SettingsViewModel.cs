using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
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
/// Einstellungen: Erkennung, Text, Kontext-Wörter, Netzwerk/Proxy, Datenschutz, Ausgabe, Oberfläche.
/// Arbeitet auf einer Kopie; erst <see cref="Save"/> übernimmt sie in die App und settings.json.
/// Keys laufen nur über die <see cref="KeyFieldViewModel"/> in den <see cref="ISecretStore"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> RecognitionLanguages = ["auto", "de", "en", "fr", "es", "it", "nl", "pl", "pt", "tr"];

    private readonly DictationController _controller;
    private readonly WhisperLoomSettings _s;
    private readonly DesktopSettings _desktop;

    /// <param name="local">Lokale Teile der App (Modelle, Begleitprozess, Ollama); null = Tab „Lokal“ ohne Downloads.</param>
    /// <param name="recordHotkey">Rekorder des globalen Hooks (Windows, Phase 3); null = Tastenkürzel nur anzeigen.</param>
    public SettingsViewModel(DictationController controller, ISecretStore secrets, IAccessChecker checker, IReadOnlyList<string>? microphones = null,
        Platform.Sidecar.LocalParts? local = null, Func<Task<string?>>? recordHotkey = null)
    {
        _controller = controller;
        _s = SettingsJson.Deserialize(SettingsJson.Serialize(controller.Settings));
        _desktop = SettingsStore.Desktop(_s);
        _policy = controller.Policy;
        Editable = new LockMap(_policy);
        Sync = new SyncViewModel(controller);
        SecretStoreKind = secrets.Kind;
        var loc = Loc.Instance;

        // Von IT nicht freigegebene Anbieter erscheinen nicht; der aktuelle bleibt sichtbar, sonst wechselte er still.
        var used = new[] { _s.Stt.ProviderId, _s.SttFallback.ProviderId, _s.Llm.ProviderId, _s.LlmFallback.ProviderId };
        var offered = ProviderChoice.All.Where(p => _policy.IsProviderAllowed(p.Id) || used.Contains(p.Id)).ToList();
        SttProviders = [.. offered.Where(p => p.Stt).Select(p => new Choice(p.Id, p.Name))];
        LlmProviders = [new Choice(LlmSettings.Same, loc["Settings_LlmSame"]), .. offered.Where(p => p.Llm).Select(p => new Choice(p.Id, p.Name))];
        LlmFallbackProviders = [.. offered.Where(p => p.Llm).Select(p => new Choice(p.Id, p.Name))];
        Languages = [.. RecognitionLanguages.Select(l => new Choice(l, l == "auto" ? loc["Language_auto"] : l.ToUpperInvariant()))];
        Stages = [.. RefineModes.SettingsFor(true).Select(m => new Choice(m.Key(), loc["Stage_" + m.Key()]))];
        ProxyModes = [.. new[] { "system", "off", "manual" }.Select(m => new Choice(m, loc["Proxy_" + m]))];
        HistorySizes = [.. HistorySettings.Sizes];
        Outputs = [new Choice("clipboard", loc["Output_clipboard"])];
        UiLanguages = [new Choice("", loc["Ui_System"]), new Choice("de", "Deutsch"), new Choice("en", "English")];
        Microphones = [new Choice("", loc["Mic_Default"]), .. (microphones ?? []).Select(m => new Choice(m, m))];
        InsertModes = [.. new[] { "paste", "type", "clipboard" }.Select(m => new Choice(m, loc["Insert_" + m]))];
        Hotkeys = new HotkeysViewModel(_desktop, recordHotkey);

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

        SttKey = new KeyFieldViewModel(SecretNames.Stt, secrets, checker, () => (SttProvider.Value, SttBaseUrl)) { Editable = KeyEditable(SecretNames.Stt) };
        SttFallbackKey = new KeyFieldViewModel(SecretNames.SttFallback, secrets, checker, () => (SttFallbackProvider.Value, SttFallbackBaseUrl)) { Editable = KeyEditable(SecretNames.SttFallback) };
        LlmKey = new KeyFieldViewModel(SecretNames.Llm, secrets, checker, () => (LlmProvider.Value == LlmSettings.Same ? SttProvider.Value : LlmProvider.Value, LlmBaseUrl)) { Editable = KeyEditable(SecretNames.Llm) };
        LlmFallbackKey = new KeyFieldViewModel(SecretNames.LlmFallback, secrets, checker, () => (LlmFallbackProvider.Value, LlmFallbackBaseUrl)) { Editable = KeyEditable(SecretNames.LlmFallback) };
        Local = new LocalViewModel(_s, local);
    }

    private readonly PolicySettings _policy;

    /// <summary>Welche Felder änderbar sind; gesperrte (von IT festgelegte) sind ausgegraut.</summary>
    public LockMap Editable { get; }

    /// <summary>Hinweis „von IT festgelegt“ zeigen.</summary>
    public bool HasPolicy => _policy.IsActive;

    public SyncViewModel Sync { get; }

    private bool KeyEditable(string name) => !_policy.IsLocked("secrets." + name);

    /// <summary>Tab „Lokal“ (WLD-P4b).</summary>
    public LocalViewModel Local { get; }

    public IReadOnlyList<Choice> SttProviders { get; }
    public IReadOnlyList<Choice> LlmProviders { get; }
    public IReadOnlyList<Choice> LlmFallbackProviders { get; }
    public IReadOnlyList<Choice> Languages { get; }
    public IReadOnlyList<Choice> Stages { get; }
    public IReadOnlyList<Choice> ProxyModes { get; }
    public IReadOnlyList<int> HistorySizes { get; }
    public IReadOnlyList<Choice> Outputs { get; }
    public IReadOnlyList<Choice> UiLanguages { get; }
    public IReadOnlyList<Choice> Microphones { get; }
    public IReadOnlyList<Choice> InsertModes { get; }

    /// <summary>Windows: Tastenkürzel-Seite (Phase 3).</summary>
    public HotkeysViewModel Hotkeys { get; }

    public KeyFieldViewModel SttKey { get; }
    public KeyFieldViewModel SttFallbackKey { get; }
    public KeyFieldViewModel LlmKey { get; }
    public KeyFieldViewModel LlmFallbackKey { get; }

    public string SecretStoreKind { get; }

    /// <summary>Hinweis, wenn Keys nur in einer Datei (600) liegen statt im Schlüsselbund.</summary>
    public bool SecretStoreIsFile => SecretStoreKind == "Datei";

    // Erkennung
    [ObservableProperty] private Choice _sttProvider;
    public string SttBaseUrl { get => _s.Stt.BaseUrl; set => _s.Stt.BaseUrl = value.Trim(); }
    public string SttModel { get => _s.Stt.Model; set => _s.Stt.Model = value.Trim(); }
    [ObservableProperty] private Choice _language;
    public bool SttFallbackEnabled { get => _s.SttFallback.Enabled; set => _s.SttFallback.Enabled = value; }
    [ObservableProperty] private Choice _sttFallbackProvider;
    public string SttFallbackBaseUrl { get => _s.SttFallback.BaseUrl; set => _s.SttFallback.BaseUrl = value.Trim(); }
    public string SttFallbackModel { get => _s.SttFallback.Model; set => _s.SttFallback.Model = value.Trim(); }

    // Text
    [ObservableProperty] private Choice _stage;
    public bool PromptEnabled { get => _s.Refine.PromptEnabled; set => _s.Refine.PromptEnabled = value; }
    [ObservableProperty] private Choice _llmProvider;
    public string LlmBaseUrl { get => _s.Llm.BaseUrl; set => _s.Llm.BaseUrl = value.Trim(); }
    public string LlmModel { get => _s.Llm.Model; set => _s.Llm.Model = value.Trim(); }
    public string ModelPolish { get => StageModel("polish"); set => SetStageModel("polish", value); }
    public string ModelBeautify { get => StageModel("beautify"); set => SetStageModel("beautify", value); }
    public string ModelSummarize { get => StageModel("summarize"); set => SetStageModel("summarize", value); }
    public string ModelPrompt { get => StageModel("prompt"); set => SetStageModel("prompt", value); }
    public bool LlmFallbackEnabled { get => _s.LlmFallback.Enabled; set => _s.LlmFallback.Enabled = value; }
    [ObservableProperty] private Choice _llmFallbackProvider;
    public string LlmFallbackBaseUrl { get => _s.LlmFallback.BaseUrl; set => _s.LlmFallback.BaseUrl = value.Trim(); }
    public string LlmFallbackModel { get => _s.LlmFallback.Model; set => _s.LlmFallback.Model = value.Trim(); }

    // Kontext-Wörter
    public string Vocabulary { get => _s.Stt.Vocabulary; set => _s.Stt.Vocabulary = value; }
    public string VocabularyFile { get => _s.Stt.VocabularyFile; set => _s.Stt.VocabularyFile = value.Trim(); }

    // Netzwerk
    [ObservableProperty] private Choice _proxyMode;
    public string ProxyUrl { get => _desktop.ProxyUrl; set => _desktop.ProxyUrl = value.Trim(); }

    // Datenschutz
    public bool HistoryEnabled { get => _s.History.Enabled; set => _s.History.Enabled = value; }
    [ObservableProperty] private int _historySize;

    // Ausgabe und Oberfläche
    [ObservableProperty] private Choice _output;
    [ObservableProperty] private Choice _microphone;
    [ObservableProperty] private Choice _uiLanguage;
    [ObservableProperty] private Choice _insertMode;

    [ObservableProperty] private string _saveStatus = "";

    public Task LoadKeysAsync() =>
        Task.WhenAll(SttKey.LoadAsync(), SttFallbackKey.LoadAsync(), LlmKey.LoadAsync(), LlmFallbackKey.LoadAsync());

    /// <summary>Übernimmt die Kopie: App, settings.json und Oberflächensprache. „Verlauf aus“ leert den Verlauf.</summary>
    [RelayCommand]
    public async Task Save()
    {
        _s.Stt.ProviderId = SttProvider.Value;
        _s.SttFallback.ProviderId = SttFallbackProvider.Value;
        _s.Llm.ProviderId = LlmProvider.Value;
        _s.LlmFallback.ProviderId = LlmFallbackProvider.Value;
        _s.Language = Language.Value;
        _s.Refine.Stage = Stage.Value;
        _s.History.Size = HistorySize;
        _desktop.ProxyMode = ProxyMode.Value;
        _desktop.Output = Output.Value;
        _desktop.Microphone = Microphone.Value;
        _desktop.UiLanguage = UiLanguage.Value;
        _desktop.InsertMode = InsertMode.Value;
        Hotkeys.Apply();
        SettingsStore.SetDesktop(_s, _desktop);
        Local.Apply();
        var result = SettingsJson.Deserialize(SettingsJson.Serialize(_s));
        if (_policy.IsActive)
        {
            // Wie ein Import: gesperrte Felder bleiben, fester Verlauf gilt, nicht freigegebene Anbieter werden abgelehnt.
            var preview = SyncImport.Preview(_controller.Settings, SyncDocument.Parse(SettingsJson.Serialize(result)), _policy);
            if (!preview.CanApply)
            {
                SaveStatus = string.Join(Environment.NewLine, preview.Errors);
                return;
            }
            result = preview.Settings!;
        }
        Loc.Instance.SetLanguage(UiLanguage.Value);
        try
        {
            await _controller.ApplySettingsAsync(result);
            SaveStatus = Loc.Instance["Settings_Saved"];
        }
        catch (IOException)
        {
            SaveStatus = Loc.Instance["Settings_HistoryClearError"];
        }
    }

    private string StageModel(string stage) => _s.Llm.StageModels.GetValueOrDefault(stage, "");

    private void SetStageModel(string stage, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _s.Llm.StageModels.Remove(stage);
        }
        else
        {
            _s.Llm.StageModels[stage] = value.Trim();
        }
    }

    private static Choice Pick(IReadOnlyList<Choice> choices, string value) =>
        choices.FirstOrDefault(c => c.Value == value) ?? choices[0];
}
