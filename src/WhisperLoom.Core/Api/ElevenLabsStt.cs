using System.Globalization;
using System.Text.RegularExpressions;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Was an ElevenLabs Speech-to-Text (Scribe) geht — Endpunkt, Key-Header und Multipart-Felder (Port von
/// <c>api/ElevenLabsStt.kt</c>; Quelle dort: elevenlabs.io/docs/api-reference/speech-to-text/convert).
/// </summary>
public static partial class ElevenLabsStt
{
    public const string Path = "/speech-to-text";
    public const string AuthHeader = "xi-api-key";

    /// <summary>Rohes PCM, 16 bit, 16 kHz, mono, little-endian — ohne WAV-Kopf.</summary>
    public const string FileFormat = "pcm_s16le_16";
    public const string FileName = "audio.pcm";
    public const string FileType = "application/octet-stream";

    /// <summary>Ab 101 Begriffen rechnet ElevenLabs jede Anfrage mit mindestens 20 s ab — also bei 100 kappen.</summary>
    public const int MaxKeyterms = 100;
    private const int MaxKeytermChars = 49;
    private const int MaxKeytermWords = 5;

    [GeneratedRegex(@"[<>{}\[\]\\]")]
    private static partial Regex Forbidden();

    /// <summary>Endet ein Stück auf ein Satzzeichen, ist es ein Satz(teil), kein Begriff (auch das „…“ der Kappung).</summary>
    [GeneratedRegex("[.!?:;…]$")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static readonly Lazy<Dictionary<string, string>> Iso3ToIso1 = new(() =>
        CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => c.TwoLetterISOLanguageName.Length == 2 && c.ThreeLetterISOLanguageName.Length == 3)
            .GroupBy(c => c.ThreeLetterISOLanguageName)
            .ToDictionary(g => g.Key, g => g.First().TwoLetterISOLanguageName));

    public static string Url(ApiAccess access) => Http.Endpoint(access.BaseUrl, Path);

    /// <summary>
    /// Textfelder in Sendereihenfolge (die Datei kommt zuletzt): keine Sprache bei "auto", keine Audio-Ereignisse
    /// („(Lachen)“), keine Wort-Zeiten, kein <c>enable_logging</c>, Vokabular als <c>keyterms</c>.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Fields(ApiAccess access, string language, string prompt)
    {
        var fields = new List<(string Name, string Value)> { ("model_id", access.Model) };
        var lang = language.Trim();
        if (lang.Length > 0 && lang != "auto") fields.Add(("language_code", lang));
        fields.Add(("tag_audio_events", "false"));
        fields.Add(("timestamps_granularity", "none"));
        fields.Add(("file_format", FileFormat));
        fields.AddRange(Keyterms(prompt).Select(t => ("keyterms", t)));
        return fields;
    }

    /// <summary>
    /// Vokabular (kommagetrennt) als keyterms: was ElevenLabs ablehnen würde, fällt weg, statt das Diktat zu kippen;
    /// ebenso Satzstücke. Von mehr als <see cref="MaxKeyterms"/> bleiben die letzten — dort stehen die eigenen Begriffe.
    /// </summary>
    public static IReadOnlyList<string> Keyterms(string prompt)
    {
        var terms = prompt.Split(',')
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 &&
                t.Length <= MaxKeytermChars &&
                Whitespace().Split(t).Length <= MaxKeytermWords &&
                !Forbidden().IsMatch(t) &&
                !SentenceEnd().IsMatch(t))
            .Distinct()
            .ToList();
        return terms.Count > MaxKeyterms ? terms[^MaxKeyterms..] : terms;
    }

    /// <summary>Erkannte Sprache 2- oder 3-stellig ("de"/"deu") → ISO 639-1; Unbekanntes bleibt.</summary>
    public static string IsoLanguage(string code)
    {
        var c = code.Trim().ToLowerInvariant();
        return c.Length == 3 && Iso3ToIso1.Value.TryGetValue(c, out var iso1) ? iso1 : c;
    }
}
