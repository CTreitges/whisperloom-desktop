using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Local;

/// <summary>Das Modell der lokalen Erkennung: Datei und Anzeige, oder warum es nicht geht (<see cref="Error"/>).</summary>
/// <param name="Path">Pfad der Modelldatei ("" bei Fehler).</param>
/// <param name="Catalog">Katalog-Modell; null = eigene Datei.</param>
/// <param name="Error">Klare Meldung für den Nutzer; null = bereit.</param>
public sealed record WhisperModelChoice(string Path, string Label, OfflineModel? Catalog, string? Error);

/// <summary>
/// Löst <c>offline.model</c> auf (W2): Katalog-ID (unbekannt → Small, wie Android) oder <c>custom:&lt;name&gt;</c> aus
/// <c>offline.customModels</c>. Eine eigene Datei fällt nie still auf Small zurück: fehlt der Eintrag oder die Datei,
/// oder ist sie keine ggml/gguf-Datei, gibt es eine klare Meldung.
/// </summary>
// Doku: https://github.com/ggml-org/ggml/blob/master/docs/gguf.md (abgerufen 2026-11-01), GGML_FILE_MAGIC in
// https://github.com/ggml-org/whisper.cpp/blob/master/src/whisper.cpp (abgerufen 2026-11-01)
public static class WhisperModels
{
    public const string MsgModelMissing = "Lokales Erkennungsmodell fehlt — unter „Lokale Modelle“ laden";

    /// <summary>Dateiendungen der Dateiauswahl.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".bin", ".gguf"];

    /// <summary>Kleinste sinnvolle Modelldatei (tiny q5_1 hat 32 MB) — darunter ist es kein Modell.</summary>
    public const long MinBytes = 1_000_000;

    public static string MsgNotListed(string name) => $"Eigene Whisper-Datei „{name}“ ist nicht eingetragen — unter „Lokale Modelle“ hinzufügen";

    public static string MsgFileMissing(string name) => $"Eigene Whisper-Datei „{name}“ nicht gefunden — Pfad unter „Lokale Modelle“ prüfen";

    public static string MsgNoModel(string name) => $"Eigene Whisper-Datei „{name}“ ist keine ggml/gguf-Modelldatei";

    public static WhisperModelChoice Resolve(OfflineSettings offline, ModelStore store)
    {
        var id = offline.Model ?? "";
        if (id.StartsWith(OfflineSettings.CustomModelPrefix, StringComparison.Ordinal))
        {
            var name = id[OfflineSettings.CustomModelPrefix.Length..];
            var entry = offline.CustomModels.FirstOrDefault(m => m.Name == name);
            if (entry is null) return new WhisperModelChoice("", name, null, MsgNotListed(name));
            return new WhisperModelChoice(entry.Path, name, null, CheckFile(entry.Path) switch
            {
                FileCheck.Ok => null,
                FileCheck.Missing => MsgFileMissing(name),
                _ => MsgNoModel(name),
            });
        }
        var model = ModelCatalog.ById(id);
        return new WhisperModelChoice(store.File(model), model.Label, model, store.IsInstalled(model) ? null : MsgModelMissing);
    }

    public enum FileCheck
    {
        Ok,
        Missing,
        NotAModel,
    }

    /// <summary>
    /// Vorhanden, groß genug und mit ggml-Magic (<c>0x67676d6c</c>, little-endian „lmgg“) bzw. GGUF-Magic („GGUF“) am Anfang.
    /// </summary>
    public static FileCheck CheckFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return FileCheck.Missing;
            using var f = File.OpenRead(path);
            if (f.Length < MinBytes) return FileCheck.NotAModel;
            Span<byte> head = stackalloc byte[4];
            f.ReadExactly(head);
            return head.SequenceEqual("lmgg"u8) || head.SequenceEqual("GGUF"u8) ? FileCheck.Ok : FileCheck.NotAModel;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return FileCheck.Missing;
        }
    }

    /// <summary>Vorschlag für den Namen einer neuen Datei: Dateiname ohne Endung, eindeutig in <paramref name="existing"/>.</summary>
    public static string SuggestName(string path, IEnumerable<CustomWhisperModel> existing)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(path).Trim();
        if (stem.Length == 0) stem = "modell";
        var names = existing.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var name = stem;
        for (var i = 2; names.Contains(name); i++) name = $"{stem} ({i})";
        return name;
    }
}
