namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>Eine Einstellung: Pfad, Seite, Gruppe, Titel und Beschreibung (Lokalisierungs-Schlüssel), hinter „Erweitert“.</summary>
public sealed record SettingEntry(string Path, string Page, string GroupKey, string TitleKey, string? DescriptionKey = null, bool Advanced = false);

/// <summary>
/// Informationsarchitektur der Einstellungen (W2/W2-UI1/ia.md): jede Einstellung genau an einer Stelle. Liefert Titel und
/// Beschreibung der <see cref="Design.SettingRow"/> und den Index der Suche. <c>SettingsCoverageTest</c> prüft, dass jeder
/// Pfad der Einstellungen hier steht und auf seiner Seite gebunden ist – oder in <see cref="Exceptions"/> mit Grund.
/// </summary>
public static class SettingsCatalog
{
    /// <summary>Seiten in Reihenfolge der Seitenleiste: Id, Abschnitt.</summary>
    public static readonly IReadOnlyList<(string Id, string Section)> Pages =
    [
        ("general", "dictation"), ("recognition", "dictation"), ("text", "dictation"), ("cleanup", "dictation"),
        ("providers", "sources"), ("localmodels", "sources"),
        ("output", "usage"), ("hotkeys", "usage"),
        ("privacy", "system"), ("network", "system"), ("company", "system"),
    ];

    public static readonly IReadOnlyList<SettingEntry> Entries =
    [
        // Allgemein
        new("desktop.uiLanguage", "general", "G_Language", "Settings_UiLanguage"),
        new("desktop.microphone", "general", "G_Language", "Settings_Microphone"),
        new("desktop.stageFontSize", "general", "G_Stage", "Set_StageFontSize", "Set_StageFontSize_Desc"),
        // Erkennung
        new("engine", "recognition", "G_Engine", "Set_Engine", "Set_Engine_Desc"),
        new("language", "recognition", "G_Engine", "Set_Language"),
        new("stt.providerId", "recognition", "G_Online", "Settings_Provider"),
        new("secrets.stt", "recognition", "G_Online", "Settings_ApiKey", "Set_ApiKey_Desc"),
        new("stt.model", "recognition", "G_Online", "Set_Model"),
        new("stt.baseUrl", "recognition", "G_Online", "Set_BaseUrl", "Set_BaseUrl_Desc", Advanced: true),
        new("stt.readTimeoutSec", "recognition", "G_Online", "Set_ReadTimeout", "Set_ReadTimeout_Desc", Advanced: true),
        new("offline.model", "recognition", "G_Local", "Set_LocalModel", "Set_LocalModel_Desc"),
        new("offline.accurate", "recognition", "G_Local", "Set_Accurate", "Set_Accurate_Desc"),
        new("local.backend", "recognition", "G_Local", "Local_Backend"),
        new("local.idleMinutes", "recognition", "G_Local", "Set_IdleMinutes", "Set_IdleMinutes_Desc", Advanced: true),
        new("stt.vocabulary", "recognition", "G_Vocabulary", "Set_Vocabulary", "Settings_VocabularyHint"),
        new("stt.vocabularyFile", "recognition", "G_Vocabulary", "Set_VocabularyFile", "Set_VocabularyFile_Desc"),
        new("sttFallback.enabled", "recognition", "G_Fallback", "Set_FallbackEnabled", "Set_FallbackEnabled_Desc"),
        new("sttFallback.providerId", "recognition", "G_Fallback", "Settings_Provider"),
        new("secrets.sttFallback", "recognition", "G_Fallback", "Settings_ApiKey"),
        new("sttFallback.model", "recognition", "G_Fallback", "Set_Model"),
        new("sttFallback.baseUrl", "recognition", "G_Fallback", "Set_BaseUrl", "Set_BaseUrl_Desc", Advanced: true),
        // Textverbesserung
        new("refine.stage", "text", "G_StageChoice", "Stage_Label", "Set_Stage_Desc"),
        new("refine.promptEnabled", "text", "G_StageChoice", "Settings_PromptEnabled"),
        new("refine.polishCleanup", "text", "G_StageOptions", "Set_PolishCleanup"),
        new("refine.paragraphsPolish", "text", "G_StageOptions", "Set_ParagraphsPolish"),
        new("refine.paragraphsBeautify", "text", "G_StageOptions", "Set_ParagraphsBeautify"),
        new("refine.summarizeForm", "text", "G_StageOptions", "Set_SummarizeForm"),
        new("llm.providerId", "text", "G_Llm", "Settings_Provider", "Set_LlmProvider_Desc"),
        new("secrets.llm", "text", "G_Llm", "Settings_ApiKey"),
        new("llm.model", "text", "G_Llm", "Set_Model"),
        new("llm.baseUrl", "text", "G_Llm", "Set_BaseUrl", "Set_BaseUrl_Desc", Advanced: true),
        new("llm.stageModels.polish", "text", "G_Llm", "Set_StageModel_polish", "Set_StageModel_Desc", Advanced: true),
        new("llm.stageModels.beautify", "text", "G_Llm", "Set_StageModel_beautify", "Set_StageModel_Desc", Advanced: true),
        new("llm.stageModels.summarize", "text", "G_Llm", "Set_StageModel_summarize", "Set_StageModel_Desc", Advanced: true),
        new("llm.stageModels.prompt", "text", "G_Llm", "Set_StageModel_prompt", "Set_StageModel_Desc", Advanced: true),
        new("offline.refine", "text", "G_LocalText", "Local_Rule"),
        new("local.textEngine", "text", "G_LocalText", "Set_TextEngine", "Set_TextEngine_Desc"),
        new("local.bundledModel", "text", "G_LocalText", "Set_BundledModel"),
        new("local.textServer", "text", "G_LocalText", "Set_TextServer"),
        new("local.ollamaModel", "text", "G_LocalText", "Local_Ollama"),
        new("local.ollamaUrl", "text", "G_LocalText", "Set_OllamaUrl", "Set_OllamaUrl_Desc", Advanced: true),
        new("llmFallback.enabled", "text", "G_Fallback", "Set_FallbackEnabled", "Set_FallbackEnabled_Desc"),
        new("llmFallback.providerId", "text", "G_Fallback", "Settings_Provider"),
        new("secrets.llmFallback", "text", "G_Fallback", "Settings_ApiKey"),
        new("llmFallback.model", "text", "G_Fallback", "Set_Model"),
        new("llmFallback.baseUrl", "text", "G_Fallback", "Set_BaseUrl", "Set_BaseUrl_Desc", Advanced: true),
        // Bereinigung
        new("polish.removeFillers", "cleanup", "G_Auto", "Set_RemoveFillers", "Set_RemoveFillers_Desc"),
        new("polish.autoCapitalize", "cleanup", "G_Auto", "Set_AutoCapitalize"),
        new("polish.trailingSpace", "cleanup", "G_Auto", "Set_TrailingSpace"),
        new("polish.customFillers", "cleanup", "G_Fillers", "Set_CustomFillers", "Set_FillerList_Desc"),
        new("polish.disabledFillers", "cleanup", "G_Fillers", "Set_DisabledFillers", "Set_DisabledFillers_Desc"),
        // Anbieter und lokale Modelle (W2-LOCAL)
        new("providers.custom", "providers", "G_CustomProviders", "Set_CustomProviders", "Set_CustomProviders_Desc"),
        new("offline.customModels", "localmodels", "G_WhisperModels", "Set_CustomModels", "Set_CustomModels_Desc"),
        new("local.servers", "localmodels", "G_LocalServers", "Set_LocalServers", "Set_LocalServers_Desc"),
        // Ausgabe
        new("desktop.insertMode", "output", "G_Insert", "Set_InsertMode", "Set_InsertMode_Desc"),
        new("output.alwaysCopy", "output", "G_Insert", "Set_AlwaysCopy", "Set_AlwaysCopy_Desc"),
        new("output.pressEnter", "output", "G_Insert", "Set_PressEnter", "Set_PressEnter_Desc"),
        new("desktop.profiles", "output", "G_Profiles", "Settings_Profiles", "Profile_Hint"),
        // Tastenkürzel
        new("desktop.hotkeysEnabled", "hotkeys", "G_HotkeysMain", "Hotkey_Enabled"),
        new("desktop.hotkeyHold", "hotkeys", "G_Actions", "Hotkey_Hold"),
        new("desktop.hotkeyToggle", "hotkeys", "G_Actions", "Hotkey_Toggle"),
        new("desktop.hotkeyStage", "hotkeys", "G_Actions", "Hotkey_Stage"),
        new("desktop.hotkeyReinsert", "hotkeys", "G_Actions", "Hotkey_Reinsert"),
        // Markierung verarbeiten (W2-SEL): alle drei Starter in einer Gruppe; Tray-Eintrag ist immer da.
        new("selection.hotkeyCombo", "hotkeys", "G_Selection", "Hotkey_Selection", "Set_SelectionHotkey_Desc"),
        new("selection.floatingButton", "hotkeys", "G_Selection", "Set_FloatingButton", "Set_FloatingButton_Desc"),
        new("selection.ctrlRightClick", "hotkeys", "G_Selection", "Set_CtrlRightClick", "Set_CtrlRightClick_Desc"),
        // Datenschutz
        new("history.enabled", "privacy", "G_History", "Set_HistoryEnabled", "Set_HistoryEnabled_Desc"),
        new("history.size", "privacy", "G_History", "Settings_HistorySize"),
        // Netzwerk
        new("desktop.proxyMode", "network", "G_Proxy", "Settings_Proxy"),
        new("desktop.proxyUrl", "network", "G_Proxy", "Set_ProxyUrl", "Set_ProxyUrl_Desc"),
    ];

    /// <summary>Pfade ohne Bedienelement, mit Grund (ia.md, Ausnahmeliste).</summary>
    public static readonly IReadOnlyDictionary<string, string> Exceptions = new Dictionary<string, string>
    {
        ["schemaVersion"] = "Format-Version der Datei, keine Einstellung",
        ["share.stage"] = "Weg „Teilen“ nur auf Android; bleibt für den Sync",
        ["share.polishCleanup"] = "Weg „Teilen“ nur auf Android; bleibt für den Sync",
        ["share.summarizeForm"] = "Weg „Teilen“ nur auf Android; bleibt für den Sync",
        ["share.hideFillers"] = "Weg „Teilen“ nur auf Android; bleibt für den Sync",
        ["offline.localLlmModel"] = "LiteRT-Modell der Android-App; der Desktop nutzt local.ollamaModel",
        ["serverModelsEnabled"] = "Vom Desktop nicht ausgewertet; Modelllisten vom Server kommen mit W2-LOCAL",
        ["desktop.output"] = "Nur ein Wert (clipboard); die Ausgabe-Art steht in desktop.insertMode",
    };

    public static SettingEntry? Find(string path) =>
        Entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

    public static string SectionOf(string page) => Pages.First(p => p.Id == page).Section;
}
