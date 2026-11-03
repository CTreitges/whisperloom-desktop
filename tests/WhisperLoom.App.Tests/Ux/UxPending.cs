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
        },
        ["UxKeyboardTest"] = new(StringComparer.Ordinal)
        {
            ["versions"] = "UX-3 Fenster",
        },
        ["UxTargetSizeTest"] = new(StringComparer.Ordinal)
        {
            ["help-section"] = "UX-3 Fenster",
        },
        ["UxTextEdgeTest"] = new(StringComparer.Ordinal)
        {
            ["help-section"] = "UX-3 Fenster",
        },
        ["UxTextScaleTest"] = new(StringComparer.Ordinal)
        {
            ["help-overview"] = "UX-3 Fenster",
            ["help-section"] = "UX-3 Fenster",
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
