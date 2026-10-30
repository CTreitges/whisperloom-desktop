using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Text;

/// <summary>Entscheidet, was die Regel-Nachbearbeitung noch tun soll. Port von <c>PolishPlan</c> (<c>TextPolisher.kt</c>).</summary>
public static class PolishPlan
{
    /// <summary>
    /// Wortgetreu: nur Whitespace und Leerzeichen vor Satzzeichen normalisieren. Für fremde
    /// Sprachnachrichten — da will man hören, was gesagt wurde.
    /// </summary>
    public static PolishOptions Verbatim(string language) =>
        new(RemoveFillers: false, AutoCapitalize: false, Language: language);

    /// <summary>Wortgetreu, aber ohne Füllwörter — die zweite Fassung der Share-Ansicht.</summary>
    public static PolishOptions Cleaned(
        string language,
        IReadOnlyCollection<string>? customFillers = null,
        IReadOnlySet<string>? disabledFillers = null) =>
        new(
            RemoveFillers: true,
            AutoCapitalize: false,
            Language: language,
            CustomFillers: customFillers ?? [],
            DisabledFillers: disabledFillers ?? new HashSet<string>());

    /// <summary>
    /// Nachbearbeitung nach der Textverbesserung. Die Füllwort-Liste (eingebaut und eigene Wörter) ist das
    /// Sicherheitsnetz: sie läuft nach jeder KI-Stufe, auch wenn die KI selbst über Füllwörter entscheidet.
    /// Ausnahme "Prompt": Füllwörter und Groß-Schreibung erledigt dort das Modell, und diktiertes Material
    /// zwischen <c>&lt;text&gt;</c>-Tags soll unverändert bleiben. Ist die Textverbesserung gescheitert,
    /// übergibt der Aufrufer <see cref="RefineMode.Off"/>: der Rohtext braucht die vollen Regeln.
    /// </summary>
    public static PolishOptions Options(
        bool removeFillers,
        bool autoCapitalize,
        string language,
        RefineMode refineMode,
        IReadOnlyCollection<string>? customFillers = null,
        IReadOnlySet<string>? disabledFillers = null,
        bool paragraphs = true)
    {
        var refined = refineMode != RefineMode.Off;
        var prompt = refineMode == RefineMode.Prompt;
        return new PolishOptions(
            RemoveFillers: removeFillers && !prompt,
            AutoCapitalize: autoCapitalize && !prompt,
            Language: language,
            CustomFillers: customFillers ?? [],
            DisabledFillers: disabledFillers ?? new HashSet<string>(),
            // Das Sprachmodell setzt Absätze/Stichpunkte bewusst — nicht plattziehen. Ohne Absätze werden
            // Umbrüche, die das Modell trotzdem liefert, zu einem Fließtext. "Prompt" ist ausgenommen:
            // ihre Gliederung ist der Zweck, nicht Beiwerk.
            KeepLineBreaks: refined && (paragraphs || prompt));
    }

    /// <summary>Klein geschrieben, getrimmt, ohne Leeres und Duplikate — so werden Füllwörter gespeichert.</summary>
    public static IReadOnlySet<string> NormalizeFillers(IEnumerable<string> words) =>
        words.Select(w => w.Trim().ToLowerInvariant()).Where(w => w.Length > 0).ToHashSet();

    /// <summary>"Ähm, halt,sozusagen" -> {"ähm", "halt", "sozusagen"} — für die Texteingabe im UI.</summary>
    public static IReadOnlySet<string> ParseFillers(string raw) => NormalizeFillers(raw.Split(',', '\n', ';'));
}
