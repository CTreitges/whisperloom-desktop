using System.ComponentModel;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Seite „Text &amp; Stufen“: Stufe, Stufen-Optionen, KI-Zugang mit Modellwahl (Katalog, Serverliste, „Eigenes Modell …“)
/// und Modell je Stufe, lokale Verbesserung (Ollama, mitgeliefertes Modell oder lokaler Server) und Ersatzanbieter.
/// </summary>
public sealed class TextPageViewModel : SettingsPageViewModel
{
    private static readonly string[] Cleanups = ["plain", "clean", "readable"];
    private static readonly string[] Forms = ["auto", "prose"];

    public TextPageViewModel(SettingsViewModel session) : base("text", "TextStage", "Violet", session)
    {
        var loc = Loc.Instance;
        CleanupChoices = [.. Cleanups.Select(c => new Choice(c, loc["Cleanup_" + c]))];
        FormChoices = [.. Forms.Select(f => new Choice(f, loc["Form_" + f]))];
        LlmModelPicker = new ModelPickerViewModel(LlmOptions, () => session.LlmModel, v => session.LlmModel = v,
            ct => session.LoadServerModelsAsync("llm", ModelKind.Llm, ct), RefreshStagePickers);
        LlmFallbackModelPicker = new ModelPickerViewModel(FallbackOptions, () => session.LlmFallbackModel, v => session.LlmFallbackModel = v,
            ct => session.LoadServerModelsAsync("llmFallback", ModelKind.Llm, ct));
        StagePolish = Picker(StageOptions(), () => session.ModelPolish, v => session.ModelPolish = v);
        StageBeautify = Picker(StageOptions(), () => session.ModelBeautify, v => session.ModelBeautify = v);
        StageSummarize = Picker(StageOptions(), () => session.ModelSummarize, v => session.ModelSummarize = v);
        StagePrompt = Picker(StageOptions(), () => session.ModelPrompt, v => session.ModelPrompt = v);
        AdvancedOpen = session.LlmProvider.Value != LlmSettings.Same && NeedsUrl(session.LlmProvider.Value);
        FallbackAdvancedOpen = NeedsUrl(session.LlmFallbackProvider.Value);
        session.PropertyChanged += OnSessionChanged;
    }

    public LocalViewModel Local => Session.Local;

    public IReadOnlyList<Choice> CleanupChoices { get; }

    public IReadOnlyList<Choice> FormChoices { get; }

    public ModelPickerViewModel LlmModelPicker { get; }

    public ModelPickerViewModel LlmFallbackModelPicker { get; }

    public CustomChoiceViewModel LlmModel => LlmModelPicker.Choice;

    public CustomChoiceViewModel LlmFallbackModel => LlmFallbackModelPicker.Choice;

    public CustomChoiceViewModel StagePolish { get; }

    public CustomChoiceViewModel StageBeautify { get; }

    public CustomChoiceViewModel StageSummarize { get; }

    public CustomChoiceViewModel StagePrompt { get; }

    public bool AdvancedOpen { get; set; }

    public bool FallbackAdvancedOpen { get; set; }

    private RefineSettings Refine => Session.Working.Refine;

    public Choice PolishCleanup
    {
        get => SettingsViewModel.Pick(CleanupChoices, Refine.PolishCleanup);
        set => Write(() => Refine.PolishCleanup = value.Value);
    }

    public Choice SummarizeForm
    {
        get => SettingsViewModel.Pick(FormChoices, Refine.SummarizeForm);
        set => Write(() => Refine.SummarizeForm = value.Value);
    }

    public bool ParagraphsPolish { get => Refine.ParagraphsPolish; set => Write(() => Refine.ParagraphsPolish = value); }

    public bool ParagraphsBeautify { get => Refine.ParagraphsBeautify; set => Write(() => Refine.ParagraphsBeautify = value); }

    /// <summary>Ollama-Adresse: leer = Standard, sonst http(s)-Adresse.</summary>
    public string OllamaUrl
    {
        get => Local.OllamaUrl;
        set
        {
            var url = value.Trim();
            var error = SettingsValidation.ProxyUrl(url);
            Session.Errors.Set("local.ollamaUrl", error);
            if (error is null)
            {
                Local.OllamaUrl = url;
            }
        }
    }

    public override void Dispose()
    {
        Session.PropertyChanged -= OnSessionChanged;
        base.Dispose();
    }

    private void Write(Action write)
    {
        write();
        Session.Touch();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.LlmProvider) or nameof(SettingsViewModel.SttProvider))
        {
            LlmModelPicker.Refresh();
            RefreshStagePickers();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.LlmFallbackProvider))
        {
            LlmFallbackModelPicker.Refresh();
        }
    }

    private void RefreshStagePickers()
    {
        foreach (var picker in new[] { StagePolish, StageBeautify, StageSummarize, StagePrompt })
        {
            picker.SetOptions(StageOptions());
        }
    }

    /// <summary>„Wie Erkennung“ nutzt die Textmodelle des Erkennungs-Anbieters.</summary>
    private IReadOnlyList<ModelOption> LlmModels =>
        Session.Providers.ById(Session.LlmProvider.Value == LlmSettings.Same ? Session.SttProvider.Value : Session.LlmProvider.Value).LlmModels ?? [];

    private IReadOnlyList<RemoteModel> LlmServerModels => Session.CachedServerModels("llm", ModelKind.Llm);

    private IEnumerable<Choice> LlmOptions() => ModelPickerViewModel.Combine(Recommended(LlmModels), LlmModels, LlmServerModels);

    private IEnumerable<Choice> StageOptions() => ModelPickerViewModel.Combine(Loc.Instance["Model_SameAsAccess"], LlmModels, LlmServerModels);

    private IEnumerable<Choice> FallbackOptions()
    {
        var models = Session.Providers.ById(Session.LlmFallbackProvider.Value).LlmModels ?? [];
        return ModelPickerViewModel.Combine(Recommended(models), models, Session.CachedServerModels("llmFallback", ModelKind.Llm));
    }
}
