namespace WhisperLoom.Core.Contracts;

/// <summary>
/// Was das Sprachmodell nach der Erkennung mit dem Text tun soll. Port von <c>RefineMode.kt</c>.
/// <see cref="Readable"/> ist nirgends wählbar: es ist <see cref="Polish"/> mit der Bereinigung „Lesbar“
/// (<see cref="Refinement.Of"/>). <see cref="Prompt"/> nur, wenn in den Einstellungen eingeschaltet.
/// Gespeichert wird immer der Schlüssel (<see cref="RefineModes.Key"/>), nie der Name.
/// </summary>
public enum RefineMode
{
    Off,
    Polish,
    Beautify,
    Summarize,

    /// <summary>Glätten plus behutsames Lektorat: Satzbau lesbar, Wortwahl und Ton bleiben. Nie gespeichert.</summary>
    Readable,

    /// <summary>Formt das Diktat zu einem Prompt für einen KI-Assistenten.</summary>
    Prompt,
}

public static class RefineModes
{
    /// <summary>Reihenfolge im Einstellungs-Dropdown.</summary>
    public static readonly IReadOnlyList<RefineMode> Settings =
        [RefineMode.Off, RefineMode.Polish, RefineMode.Beautify, RefineMode.Summarize];

    /// <summary>Stufen mit eigenem Textmodell (<see cref="ModelStage"/>).</summary>
    public static readonly IReadOnlyList<RefineMode> ModelStages =
        [RefineMode.Polish, RefineMode.Beautify, RefineMode.Summarize, RefineMode.Prompt];

    /// <summary>Stufen mit eigenem Schalter „Absätze“ beim Diktat.</summary>
    public static readonly IReadOnlyList<RefineMode> ParagraphStages = [RefineMode.Polish, RefineMode.Beautify];

    /// <summary>Gespeicherter Schlüssel: off, polish, beautify, summarize, readable, prompt.</summary>
    public static string Key(this RefineMode mode) => mode switch
    {
        RefineMode.Off => "off",
        RefineMode.Polish => "polish",
        RefineMode.Beautify => "beautify",
        RefineMode.Summarize => "summarize",
        RefineMode.Readable => "readable",
        RefineMode.Prompt => "prompt",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>
    /// Unter welcher Stufe das Textmodell eingestellt ist: „Lesbar“ rechnet mit dem Glätten-Modell.
    /// null = aus, kein Modell.
    /// </summary>
    public static RefineMode? ModelStage(this RefineMode mode) => mode switch
    {
        RefineMode.Off => null,
        RefineMode.Polish or RefineMode.Readable => RefineMode.Polish,
        _ => mode,
    };

    /// <summary>Die wählbaren Stufen — „Prompt“ nur für die, die sie eingeschaltet haben.</summary>
    public static IReadOnlyList<RefineMode> SettingsFor(bool promptEnabled) =>
        promptEnabled ? [.. Settings, RefineMode.Prompt] : Settings;

    /// <summary>Unbekanntes und null gelten als <see cref="RefineMode.Off"/>.</summary>
    public static RefineMode FromKey(string? key) =>
        Enum.GetValues<RefineMode>().FirstOrDefault(m => m.Key() == key, RefineMode.Off);
}
