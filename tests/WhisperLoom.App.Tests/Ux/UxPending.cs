namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Ansichten, die eine Ux-Regel noch nicht erfüllen, weil ihr Umbau in einem späteren PR des UX-Laufs kommt. Die Liste
/// schrumpft mit jedem PR und ist am Ende leer (Goal UX, Akzeptanz 6). Eine Ansicht hier wird übersprungen, nicht bestanden –
/// und sobald sie die Regel erfüllt, schlägt <see cref="UxPendingTest"/> an, damit der Eintrag verschwindet.
/// </summary>
internal static class UxPending
{
    /// <summary>Regel → Ansicht → Grund (PR, der sie umbaut).</summary>
    public static readonly Dictionary<string, Dictionary<string, string>> Rules = new(StringComparer.Ordinal)
    {
        ["UxAccessibilityTest"] = new(StringComparer.Ordinal)
        {
            ["setup-language"] = "UX-3 Fenster",
            ["setup-microphone"] = "UX-3 Fenster",
            ["setup-recognition"] = "UX-3 Fenster",
            ["setup-stage"] = "UX-3 Fenster",
        },
        ["UxContrastTest"] = new(StringComparer.Ordinal)
        {
            ["help-overview/dark"] = "UX-3 Fenster",
            ["help-overview/light"] = "UX-3 Fenster",
            ["help-section/dark"] = "UX-3 Fenster",
            ["help-section/light"] = "UX-3 Fenster",
            ["history-empty/dark"] = "UX-3 Fenster",
            ["history-empty/light"] = "UX-3 Fenster",
            ["history/dark"] = "UX-3 Fenster",
            ["history/light"] = "UX-3 Fenster",
            ["settings-cleanup/dark"] = "UX-2 Einstellungen",
            ["settings-cleanup/light"] = "UX-2 Einstellungen",
            ["settings-company-connected/dark"] = "UX-2 Einstellungen",
            ["settings-company-connected/light"] = "UX-2 Einstellungen",
            ["settings-company-join/dark"] = "UX-2 Einstellungen",
            ["settings-company-join/light"] = "UX-2 Einstellungen",
            ["settings-general/dark"] = "UX-2 Einstellungen",
            ["settings-general/light"] = "UX-2 Einstellungen",
            ["settings-hotkeys/dark"] = "UX-2 Einstellungen",
            ["settings-hotkeys/light"] = "UX-2 Einstellungen",
            ["settings-localmodels/dark"] = "UX-2 Einstellungen",
            ["settings-localmodels/light"] = "UX-2 Einstellungen",
            ["settings-network/dark"] = "UX-2 Einstellungen",
            ["settings-network/light"] = "UX-2 Einstellungen",
            ["settings-output/dark"] = "UX-2 Einstellungen",
            ["settings-output/light"] = "UX-2 Einstellungen",
            ["settings-privacy/dark"] = "UX-2 Einstellungen",
            ["settings-privacy/light"] = "UX-2 Einstellungen",
            ["settings-providers/dark"] = "UX-2 Einstellungen",
            ["settings-providers/light"] = "UX-2 Einstellungen",
            ["settings-recognition-managed/dark"] = "UX-2 Einstellungen",
            ["settings-recognition-managed/light"] = "UX-2 Einstellungen",
            ["settings-recognition/dark"] = "UX-2 Einstellungen",
            ["settings-recognition/light"] = "UX-2 Einstellungen",
            ["settings-text/dark"] = "UX-2 Einstellungen",
            ["settings-text/light"] = "UX-2 Einstellungen",
        },
        ["UxKeyboardTest"] = new(StringComparer.Ordinal)
        {
            ["versions"] = "UX-3 Fenster",
        },
        ["UxTargetSizeTest"] = new(StringComparer.Ordinal)
        {
            ["help-section"] = "UX-3 Fenster",
            ["settings-recognition"] = "UX-2 Einstellungen",
            ["settings-recognition-managed"] = "UX-2 Einstellungen",
            ["settings-text"] = "UX-2 Einstellungen",
            ["setup-autostart"] = "UX-3 Fenster",
            ["setup-company"] = "UX-3 Fenster",
            ["setup-done"] = "UX-3 Fenster",
            ["setup-hotkey"] = "UX-3 Fenster",
            ["setup-language"] = "UX-3 Fenster",
            ["setup-microphone"] = "UX-3 Fenster",
            ["setup-recognition"] = "UX-3 Fenster",
            ["setup-stage"] = "UX-3 Fenster",
            ["setup-usage"] = "UX-3 Fenster",
        },
        ["UxTextEdgeTest"] = new(StringComparer.Ordinal)
        {
            ["help-section"] = "UX-3 Fenster",
            ["settings-cleanup"] = "UX-2 Einstellungen",
            ["settings-company-connected"] = "UX-2 Einstellungen",
            ["settings-company-join"] = "UX-2 Einstellungen",
            ["settings-general"] = "UX-2 Einstellungen",
            ["settings-hotkeys"] = "UX-2 Einstellungen",
            ["settings-localmodels"] = "UX-2 Einstellungen",
            ["settings-network"] = "UX-2 Einstellungen",
            ["settings-output"] = "UX-2 Einstellungen",
            ["settings-privacy"] = "UX-2 Einstellungen",
            ["settings-providers"] = "UX-2 Einstellungen",
            ["settings-recognition"] = "UX-2 Einstellungen",
            ["settings-recognition-managed"] = "UX-2 Einstellungen",
            ["settings-search"] = "UX-2 Einstellungen",
            ["settings-text"] = "UX-2 Einstellungen",
            ["setup-autostart"] = "UX-3 Fenster",
            ["setup-company"] = "UX-3 Fenster",
            ["setup-done"] = "UX-3 Fenster",
            ["setup-hotkey"] = "UX-3 Fenster",
            ["setup-language"] = "UX-3 Fenster",
            ["setup-microphone"] = "UX-3 Fenster",
            ["setup-recognition"] = "UX-3 Fenster",
            ["setup-stage"] = "UX-3 Fenster",
            ["setup-usage"] = "UX-3 Fenster",
        },
        ["UxTextScaleTest"] = new(StringComparer.Ordinal)
        {
            ["help-overview"] = "UX-3 Fenster",
            ["help-section"] = "UX-3 Fenster",
            ["settings-cleanup"] = "UX-2 Einstellungen",
            ["settings-company-connected"] = "UX-2 Einstellungen",
            ["settings-company-join"] = "UX-2 Einstellungen",
            ["settings-general"] = "UX-2 Einstellungen",
            ["settings-hotkeys"] = "UX-2 Einstellungen",
            ["settings-localmodels"] = "UX-2 Einstellungen",
            ["settings-network"] = "UX-2 Einstellungen",
            ["settings-output"] = "UX-2 Einstellungen",
            ["settings-privacy"] = "UX-2 Einstellungen",
            ["settings-providers"] = "UX-2 Einstellungen",
            ["settings-recognition"] = "UX-2 Einstellungen",
            ["settings-recognition-managed"] = "UX-2 Einstellungen",
            ["settings-text"] = "UX-2 Einstellungen",
        },
    };

    public static string? Reason(string rule, string view) =>
        Rules.TryGetValue(rule, out var views) && views.TryGetValue(view, out var reason) ? reason : null;

    /// <summary>Überspringt den Fall, wenn die Ansicht für die Regel noch aussteht; sonst nichts.</summary>
    public static void SkipIfPending(string rule, string view)
    {
        if (Reason(rule, view) is { } reason)
        {
            Assert.Skip($"ausstehend ({reason})");
        }
    }
}
