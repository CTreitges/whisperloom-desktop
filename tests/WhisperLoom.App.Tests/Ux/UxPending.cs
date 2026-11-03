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
        },
        ["UxKeyboardTest"] = new(StringComparer.Ordinal)
        {
            ["versions"] = "UX-3 Fenster",
        },
        ["UxTargetSizeTest"] = new(StringComparer.Ordinal)
        {
        },
        ["UxTextEdgeTest"] = new(StringComparer.Ordinal)
        {
        },
        ["UxTextScaleTest"] = new(StringComparer.Ordinal)
        {
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
