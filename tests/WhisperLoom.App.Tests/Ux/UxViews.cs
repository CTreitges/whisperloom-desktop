using Avalonia.Styling;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Setup;
using WhisperLoom.App.ViewModels.Settings;
using static WhisperLoom.App.Tests.Ux.UxSettingsScenes;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Alle Ansichten der App als eine Liste: Quelle der Vorher-/Nachher-Screenshots (<see cref="UxShotsTest"/>), des Audits und der
/// Ux-Testklassen. Eine neue Ansicht gehört hierher, sonst prüft sie kein Ux-Test (<see cref="UxCatalogTest"/>).
/// Das Tray-Menü ist ein natives Menü des Systems und nicht headless darstellbar; es prüft <c>TrayMenuTest</c>.
/// </summary>
internal static class UxViews
{
    private const string Settings = "Einstellungen";
    private const string Pages = "Views/Settings/";

    public static IReadOnlyList<UxView> All { get; } =
    [
        new("settings-dictation", Settings, Pages + "DictationPage.axaml", t => PageAsync("dictation", t)),
        new("settings-search", Settings, "Views/SettingsWindow.axaml", t => PageAsync("dictation", t, new PageSetup(Act: w =>
        {
            w.Shell!.SearchText = "Taste";
            return Task.CompletedTask;
        }))),
        new("settings-recognition", Settings, Pages + "RecognitionPage.axaml", t => PageAsync("recognition", t)),
        new("settings-recognition-local", Settings, Pages + "RecognitionPage.axaml", t => PageAsync("recognition", t, new PageSetup(LocalDemo))),
        new("settings-recognition-managed", Settings, Pages + "RecognitionPage.axaml", t => PageAsync("recognition", t, new PageSetup(Arrange: Managed))),
        new("settings-recognition-keytarget", Settings, Pages + "RecognitionPage.axaml", t => PageAsync("recognition", t, CompanyTargetKey())),
        new("settings-text", Settings, Pages + "TextPage.axaml", t => PageAsync("text", t)),
        new("settings-text-local", Settings, Pages + "TextPage.axaml", t => PageAsync("text", t, new PageSetup(LocalDemo))),
        new("settings-output", Settings, Pages + "OutputPage.axaml", t => PageAsync("output", t, new PageSetup(WithProfiles))),
        // K2: Profil 1 offen mit Fenstertitel (Vorrang, „Nur bestimmte Fenster“ offen), Programmliste offen, schwebender Knopf an;
        // Outlook gelöscht → Statuszeile mit „Rückgängig“ und Vorschlag für Outlook.
        new("settings-output-open", Settings, Pages + "OutputPage.axaml", t => PageAsync("output", t, new PageSetup(WithProfiles, Act: w =>
        {
            var vm = w.Settings!;
            vm.FloatingButton = true;
            var terminal = vm.Profiles.Rows[0];
            terminal.TitlePattern = "Claude";
            terminal.AdvancedOpen = true;
            terminal.IsOpen = true;
            terminal.RunningPrograms = ["chrome.exe", "notepad.exe", "WINWORD.EXE"];
            terminal.PickOpen = true;
            vm.Profiles.Rows[1].DeleteCommand.Execute(null);
            return Task.CompletedTask;
        }))),
        new("settings-connections", Settings, Pages + "ConnectionsPage.axaml", t => PageAsync("connections", t, new PageSetup(LocalDemo))),
        new("settings-privacy", Settings, Pages + "PrivacyPage.axaml", t => PageAsync("privacy", t)),
        new("settings-system", Settings, Pages + "SystemPage.axaml", t => PageAsync("system", t)),
        new("settings-company-join", Settings, Pages + "CompanyPage.axaml", t => PageAsync("company", t, new PageSetup(Company: true, Act: w =>
        {
            var company = (CompanyPageViewModel)w.Shell!.Page("company");
            company.OpenJoinCommand.Execute(null);
            company.Join!.Server = "https://wl.example.com";
            company.Join.Code = "ABCD-EFGH-JKMN-PQRS";
            return Task.CompletedTask;
        }))),
        new("settings-company-connected", Settings, Pages + "CompanyPage.axaml", t => PageAsync("company", t, new PageSetup(Company: true, Joined: true))),

        new("setup-usage", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupUsage(t, company: false)),
        new("setup-company", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupUsage(t, company: true)),
        new("setup-language", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupAt(t, SetupStep.Language)),
        new("setup-microphone", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupAt(t, SetupStep.Microphone)),
        new("setup-recognition", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupAt(t, SetupStep.Recognition)),
        new("setup-stage", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupAt(t, SetupStep.Stage)),
        new("setup-hotkey", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupAt(t, SetupStep.Hotkey)),
        new("setup-autostart", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupAt(t, SetupStep.Autostart)),
        new("setup-done", "Einrichtung", "Setup/SetupWindow.axaml", t => UxWindowScenes.SetupAt(t, SetupStep.Done)),

        new("history", "Verlauf", "Views/HistoryWindow.axaml", t => UxWindowScenes.History(t, empty: false)),
        new("history-empty", "Verlauf", "Views/HistoryWindow.axaml", t => UxWindowScenes.History(t, empty: true)),
        new("help-overview", "Hilfe", "Views/Help/HelpWindow.axaml", t => UxWindowScenes.Help(t, topic: null)),
        new("help-section", "Hilfe", "Views/Help/HelpWindow.axaml", t => UxWindowScenes.Help(t, topic: "selection")),
        new("stage-recording", "Bühne", "Views/StageWindow.axaml", t => UxWindowScenes.Stage(t, result: false)),
        new("stage-result", "Bühne", "Views/StageWindow.axaml", t => UxWindowScenes.Stage(t, result: true)),
        new("versions", "Updates", "Views/Updates/VersionListWindow.axaml", UxWindowScenes.Versions),
        new("update-prompt", "Updates", "Updates/UpdatePrompt.cs", UxWindowScenes.UpdatePromptScene),

        new("overlay-recording", "Overlay", "Platform/Windows/Overlay/OverlayWindow.axaml", t => UxWindowScenes.Overlay(t, OverlayState.Recording)),
        new("overlay-processing", "Overlay", "Platform/Windows/Overlay/OverlayWindow.axaml", t => UxWindowScenes.Overlay(t, OverlayState.Processing)),
        new("overlay-error", "Overlay", "Platform/Windows/Overlay/OverlayWindow.axaml", t => UxWindowScenes.Overlay(t, OverlayState.Error)),
        new("overlay-stages", "Overlay", "Design/StagePicker.axaml", t => UxWindowScenes.Overlay(t, OverlayState.Recording, picker: true)),
        new("pill", "Overlay", "Platform/Windows/Pill/PillWindow.axaml", t => UxWindowScenes.Pill(t, processing: false)),
        new("pill-processing", "Overlay", "Platform/Windows/Pill/PillWindow.axaml", t => UxWindowScenes.Pill(t, processing: true)),
        new("hint-stage", "Overlay", "Platform/Windows/Pill/HintWindow.axaml", t => UxWindowScenes.Hint(t, toast: false)),
        new("hint-toast", "Overlay", "Platform/Windows/Pill/HintWindow.axaml", t => UxWindowScenes.Hint(t, toast: true)),
        new("selection-button", "Markierung", "Features/Selection/SelectionButtonWindow.axaml", UxWindowScenes.SelectionButton),
        new("selection-picker", "Markierung", "Features/Selection/SelectionPickerWindow.axaml", UxWindowScenes.SelectionPicker),
    ];

    public static IEnumerable<object[]> Ids => All.Select(v => new object[] { v.Id });

    public static UxView Get(string id) => All.Single(v => v.Id == id);

    /// <summary>Hell, dunkel und Hochkontrast (Kontrastdesign des Systems, <see cref="Design.SystemTheme.HighContrast"/>).</summary>
    public static readonly (string Name, ThemeVariant Theme)[] Themes =
        [("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark), ("contrast", Design.SystemTheme.HighContrast)];
}
