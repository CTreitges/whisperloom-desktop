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

    public TextPageViewModel(SettingsViewModel session) : base("text", "TextStage", session)
    {
        var loc = Loc.Instance;
        CleanupChoices = [.. Cleanups.Select(c => new Choice(c, loc["Cleanup_" + c], loc["Cleanup_" + c + "_Desc"]))];
        FormChoices = [.. Forms.Select(f => new Choice(f, loc["Form_" + f], loc["Form_" + f + "_Desc"]))];
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
        Local.PropertyChanged += OnLocalChanged;
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

    /// <summary>„Bei lokaler Erkennung“ gilt nur, wenn die Erkennung lokal läuft; sonst eine Hinweiszeile mit Sprung (B25).</summary>
    public bool IsLocalRecognition => Local.Engine.Value == "offline";

    public bool IsOnlineRecognition => !IsLocalRecognition;

    /// <summary>Ein lokales Textmodell braucht es nur, wenn lokal verbessert werden darf (nicht bei „Online, sonst ohne KI“).</summary>
    public bool UsesLocalTextModel => Local.Rule.Value != "skip";

    // Bereinigung (früher eigene Seite): Regeln ohne KI, laufen nach jedem Diktat.
    private PolishSettings Polish => Session.Working.Polish;

    public bool RemoveFillers
    {
        get => Polish.RemoveFillers;
        set
        {
            Write(() => Polish.RemoveFillers = value);
            OnPropertyChanged();
        }
    }

    public bool AutoCapitalize { get => Polish.AutoCapitalize; set => Write(() => Polish.AutoCapitalize = value); }

    public bool TrailingSpace { get => Polish.TrailingSpace; set => Write(() => Polish.TrailingSpace = value); }

    /// <summary>Eigene Füllwörter, durch Komma getrennt; gespeichert klein geschrieben, ohne Duplikate.</summary>
    public string CustomFillers { get => string.Join(", ", Polish.CustomFillers); set => Write(() => Polish.CustomFillers = Words(value)); }

    /// <summary>Eingebaute Füllwörter, die stehen bleiben.</summary>
    public string DisabledFillers { get => string.Join(", ", Polish.DisabledFillers); set => Write(() => Polish.DisabledFillers = Words(value)); }

    internal static List<string> Words(string text) =>
        [.. text.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant()).Distinct()];

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
        Local.PropertyChanged -= OnLocalChanged;
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

    private void OnLocalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalViewModel.Engine))
        {
            OnPropertyChanged(nameof(IsLocalRecognition));
            OnPropertyChanged(nameof(IsOnlineRecognition));
        }
        else if (e.PropertyName == nameof(LocalViewModel.Rule))
        {
            OnPropertyChanged(nameof(UsesLocalTextModel));
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
