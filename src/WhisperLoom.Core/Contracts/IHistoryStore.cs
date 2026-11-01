namespace WhisperLoom.Core.Contracts;

/// <summary>Ein Diktat im Verlauf, nur Text: Roh, Ergebnis, Stufe, Anbieter, Zeit. Nie Audio.</summary>
/// <param name="Id">Sortiert wie die Zeit (z. B. "&lt;Unix-ms, 13 Ziffern&gt;-&lt;8 Hex&gt;" wie Android).</param>
/// <param name="Model">Womit verbessert wurde; null = ohne KI.</param>
/// <param name="Skipped">Warum ohne KI, obwohl eine Stufe gewählt war.</param>
/// <param name="Versions">Fassungen je Verarbeitung (W2-OUT, wie Android <c>HistoryEntry.versions</c>); null = nur die damalige
/// Fassung (<see cref="Text"/>), so wie ältere Dateien. <see cref="Raw"/> ist der Ursprung und wird nie überschrieben.</param>
public sealed record HistoryEntry(
    string Id,
    DateTimeOffset CreatedAt,
    string Language,
    long DurationMs,
    string Raw,
    string Text,
    Refinement Refinement,
    string SttLabel,
    string? Model = null,
    string? Skipped = null,
    IReadOnlyList<HistoryVersion>? Versions = null)
{
    /// <summary>Ohne Text: ein Log darf nie ein Diktat zeigen.</summary>
    public override string ToString() =>
        $"HistoryEntry(id={Id}, language={Language}, durationMs={DurationMs}, raw={Raw.Length} Zeichen, " +
        $"text={Text.Length} Zeichen, refinement={Refinement}, sttLabel={SttLabel}, model={Model}, versions={Versions?.Count ?? 0})";
}

/// <summary>Eine Fassung eines Verlaufseintrags (W2-OUT; Android <c>HistoryVersion</c>).</summary>
/// <param name="Processing">Stufen-Schlüssel (<c>off</c>, <c>polish</c>, …) oder <c>edited</c> (bearbeiteter Ursprung); je Eintrag
/// höchstens eine Fassung je Schlüssel.</param>
/// <param name="CreatedAt">Wann sie entstand bzw. zuletzt bearbeitet wurde.</param>
/// <param name="Model">Womit verbessert wurde; null = ohne KI.</param>
/// <param name="Failed">Warum ohne KI, obwohl eine Stufe gewählt war.</param>
/// <param name="Edited">Von Hand bearbeitet.</param>
public sealed record HistoryVersion(
    string Processing,
    string Text,
    DateTimeOffset CreatedAt,
    string? Model = null,
    string? Failed = null,
    bool Edited = false)
{
    /// <summary>Ohne Text: ein Log darf nie ein Diktat zeigen.</summary>
    public override string ToString() =>
        $"HistoryVersion(processing={Processing}, text={Text.Length} Zeichen, model={Model}, failed={Failed is not null}, edited={Edited})";
}

/// <summary>
/// Verlauf als Textdateien im Datenordner (abschaltbar, Standard 50 Einträge). Ist der Verlauf aus, schreibt
/// <see cref="AddAsync"/> nichts. Lese-/Schreibfehler der Ablage werden als <see cref="IOException"/> geworfen.
/// </summary>
public interface IHistoryStore
{
    /// <summary>Hängt an und kürzt auf die Höchstzahl (älteste zuerst weg).</summary>
    Task AddAsync(HistoryEntry entry, CancellationToken ct);

    /// <summary>Neueste zuerst.</summary>
    Task<IReadOnlyList<HistoryEntry>> ListAsync(CancellationToken ct);

    Task<HistoryEntry?> GetAsync(string id, CancellationToken ct);

    /// <returns>false, wenn es den Eintrag nicht gab.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}
