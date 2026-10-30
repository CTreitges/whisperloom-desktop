using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Pipeline;

/// <summary>
/// Optionen der Regeln nach der Textverbesserung (Port von <c>PolishPlan.options</c>, <c>TextPolisher.kt</c>). Stellvertreter,
/// bis WLD-INT auf <c>PolishPlan</c> aus WLD-P1a umstellt — hier nur, damit die Pipeline ohne P1a läuft.
/// </summary>
internal static class PolishRules
{
    /// <param name="refineMode">Ohne KI (aus, gescheitert, übersprungen) <see cref="RefineMode.Off"/>: volle Regeln für den Rohtext.</param>
    /// <param name="paragraphs">Absätze der Stufe; ohne werden Umbrüche, die das Modell trotzdem liefert, zusammengezogen.</param>
    public static PolishOptions For(PolishSettings polish, string language, RefineMode refineMode, bool paragraphs)
    {
        var refined = refineMode != RefineMode.Off;
        // „Prompt“: Füllwörter und Großschreibung erledigt das Modell, die Gliederung ist der Zweck.
        var prompt = refineMode == RefineMode.Prompt;
        return new PolishOptions(
            RemoveFillers: polish.RemoveFillers && !prompt,
            AutoCapitalize: polish.AutoCapitalize && !prompt,
            Language: language,
            CustomFillers: polish.CustomFillers,
            DisabledFillers: polish.DisabledFillers.ToHashSet(),
            KeepLineBreaks: refined && (paragraphs || prompt));
    }
}
