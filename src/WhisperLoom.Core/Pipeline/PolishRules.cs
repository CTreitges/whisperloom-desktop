using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Pipeline;

/// <summary>Optionen der Regeln nach der Textverbesserung aus den Einstellungen (<see cref="PolishPlan.Options"/>).</summary>
internal static class PolishRules
{
    /// <param name="refineMode">Ohne KI (aus, gescheitert, übersprungen) <see cref="RefineMode.Off"/>: volle Regeln für den Rohtext.</param>
    /// <param name="paragraphs">Absätze der Stufe; ohne werden Umbrüche, die das Modell trotzdem liefert, zusammengezogen.</param>
    public static PolishOptions For(PolishSettings polish, string language, RefineMode refineMode, bool paragraphs) =>
        PolishPlan.Options(polish.RemoveFillers, polish.AutoCapitalize, language, refineMode, polish.CustomFillers,
            polish.DisabledFillers.ToHashSet(), paragraphs);
}
