using System.Text;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Text;

/// <summary>
/// Liest die verknüpfte Vokabular-Datei (.md/.txt). Port der reinen Teile von <c>VocabularySource.kt</c>;
/// Android liest über den ContentResolver, der Desktop über einen Dateipfad. Die Datei wird bei jedem
/// Diktat neu gelesen; eine verschwundene oder unlesbare Datei bricht nie ein Diktat ab.
/// </summary>
public static class VocabularySource
{
    /// <summary>Dateiendungen, die als Vokabular taugen.</summary>
    private static readonly string[] Extensions = [".md", ".markdown", ".txt"];

    // Doku: https://learn.microsoft.com/dotnet/api/system.text.codepagesencodingprovider (abgerufen 2026-10-30)
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    /// <summary>Was an den Erkenner geht: eigene Einträge plus Datei-Begriffe (eine unlesbare Datei zählt als leer).</summary>
    public static VocabularyPrompt Prompt(SttSettings stt) =>
        Vocabulary.Prompt(Vocabulary.Entries(stt.Vocabulary), FileTerms(stt.VocabularyFile) ?? []);

    /// <summary>Begriffe der Datei; leer ohne Datei (leerer Pfad), null wenn sie nicht lesbar ist.</summary>
    public static IReadOnlyList<string>? FileTerms(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Vocabulary.MaxFileBytes];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return Vocabulary.ParseFile(Decode(buffer[..read]));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// UTF-8, sonst Windows-1252: eine unter Windows gespeicherte .txt mit Umlauten käme sonst mit
    /// Ersatzzeichen beim Erkenner an. Ein am Leselimit angeschnittenes UTF-8-Zeichen zählt nicht.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        var utf8 = Encoding.UTF8.GetString(bytes);
        var body = bytes.Length >= Vocabulary.MaxFileBytes && utf8.Length > 0 ? utf8[..^1] : utf8;
        return body.Contains('�') ? Windows1252.GetString(bytes) : utf8;
    }

    /// <summary>Taugt die Datei als Vokabular? Endung .md/.txt oder ein MIME-Typ "text/…"; unbekannter Name wird zugelassen.</summary>
    public static bool IsTextFile(string? name, string? mimeType = null) =>
        name == null ||
        Extensions.Any(e => name.ToLowerInvariant().EndsWith(e, StringComparison.Ordinal)) ||
        mimeType?.StartsWith("text/", StringComparison.Ordinal) == true;
}
