using WhisperLoom.Core.Api;
using WhisperLoom.Core.Text;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/ElevenLabsSttTest.kt</c>: Felder, keyterms und Sprachcodes für ElevenLabs.</summary>
public class ElevenLabsSttTest
{
    private static ApiAccess Stt(string model = "", string url = "") =>
        AccessResolver.ResolveStt(ProviderCatalog.ElevenLabsId, url, Keys.K1, model, 0);

    private static List<string> Names(IReadOnlyList<(string Name, string Value)> fields) => [.. fields.Select(f => f.Name)];

    [Fact]
    public void EndpunktUndKeyHeader()
    {
        Assert.Equal("https://api.elevenlabs.io/v1/speech-to-text", ElevenLabsStt.Url(Stt()));
        Assert.Equal("http://127.0.0.1:8/v1/speech-to-text", ElevenLabsStt.Url(Stt(url: "http://127.0.0.1:8/v1/")));
        Assert.Equal("xi-api-key", ElevenLabsStt.AuthHeader);
    }

    [Fact]
    public void FelderFuerScribeInFesterReihenfolge() =>
        Assert.Equal(
            [
                ("model_id", "scribe_v2"), ("language_code", "de"), ("tag_audio_events", "false"),
                ("timestamps_granularity", "none"), ("file_format", "pcm_s16le_16"), ("keyterms", "Anna"), ("keyterms", "Kubernetes"),
            ],
            ElevenLabsStt.Fields(Stt(), "de", "Anna, Kubernetes"));

    [Fact]
    public void GewaehltesModellGehtAlsModelId()
    {
        var f = ElevenLabsStt.Fields(Stt("scribe_v2_medical"), "en", "");
        Assert.Equal(("model_id", "scribe_v2_medical"), f[0]);
        Assert.DoesNotContain("model", Names(f));
    }

    [Fact]
    public void AutoLaesstDieSpracheWeg()
    {
        Assert.DoesNotContain("language_code", Names(ElevenLabsStt.Fields(Stt(), "auto", "")));
        Assert.DoesNotContain("language_code", Names(ElevenLabsStt.Fields(Stt(), " ", "")));
    }

    [Fact]
    public void OhneVokabularKeinKeytermsUndNieLoggingOderPrompt()
    {
        var f = Names(ElevenLabsStt.Fields(Stt(), "de", "  "));
        Assert.DoesNotContain("keyterms", f);
        Assert.DoesNotContain("prompt", f);
        Assert.DoesNotContain("enable_logging", f);
        Assert.DoesNotContain("response_format", f);
    }

    [Fact]
    public void KeytermsNachDenRegelnVonElevenLabs()
    {
        var grenze = new string('a', 49);
        var prompt = string.Join(", ",
            "  Anna ", "Dr. Hans Peter Müller Jr", "eins zwei drei vier fünf sechs", grenze, new string('b', 50),
            "a<b", "x{y}", "[z]", "c\\d", "", "Anna");
        Assert.Equal(["Anna", "Dr. Hans Peter Müller Jr", grenze], ElevenLabsStt.Keyterms(prompt));
    }

    [Fact]
    public void HoechstensHundertKeytermsUndDieLetztenGewinnen()
    {
        Assert.Equal(100, ElevenLabsStt.MaxKeyterms);
        var terms = ElevenLabsStt.Keyterms(string.Join(", ", Enumerable.Range(1, 150).Select(i => $"t{i}")));
        Assert.Equal(100, terms.Count);
        Assert.Equal("t51", terms[0]);
        Assert.Equal("t150", terms[^1]);
        Assert.Equal(100, ElevenLabsStt.Keyterms(string.Join(", ", Enumerable.Range(1, 100).Select(i => $"t{i}"))).Count);
    }

    [Fact]
    public void SatzstueckeMitSatzzeichenAmEndeFallenWeg() =>
        Assert.Equal(
            ["Anna", "Node.js", "C++", "Dr. Hans Peter"],
            ElevenLabsStt.Keyterms("Projekt X., Wirklich?, Achtung!, Teilnehmer:, siehe oben;, und so weiter…, Anna, Node.js, C++, Dr. Hans Peter"));

    /// <summary>
    /// Kotlin kappt über <c>Vocabulary.prompt</c> (WLD-P1a). Hier der Teil, der ElevenLabs betrifft: ein am Schnitt
    /// mit „…“ markiertes Stück kommt nicht an — mit und ohne „…“. Die Kappung selbst prüft der Fall unten.
    /// </summary>
    [Fact]
    public void KappungDesVokabularsVerwirftDasAngeschnitteneStueck()
    {
        var prompt = string.Join(", ", Enumerable.Range(1, 40).Select(i => $"Begriff Nummer {i}")) + ", Begriff Num…";
        var terms = ElevenLabsStt.Keyterms(prompt);
        Assert.NotEmpty(terms);
        Assert.All(terms, t => Assert.Matches(@"^Begriff Nummer \d+$", t));
        Assert.DoesNotContain("Begriff Num", terms);
    }

    [Fact]
    public void KappungDesVokabularsVerwirftDasAngeschnitteneStueckMitVocabulary()
    {
        // Alter Freitext in einer Zeile, länger als 800 Zeichen: Vocabulary schneidet ihn am Wortende.
        var legacy = string.Join(", ", Enumerable.Range(1, 200).Select(i => $"Begriff Nummer {i}"));
        var prompt = Vocabulary.Prompt([legacy]);
        Assert.True(prompt.Cut);
        var cutPiece = prompt.Text[(prompt.Text.LastIndexOf(", ", StringComparison.Ordinal) + 2)..];
        Assert.EndsWith("…", cutPiece, StringComparison.Ordinal);
        var terms = ElevenLabsStt.Keyterms(prompt.Text);
        Assert.NotEmpty(terms);
        // Nur ganze Begriffe — das Stück am Schnitt kommt weder mit noch ohne „…“ an.
        Assert.All(terms, t => Assert.Matches(@"^Begriff Nummer \d+$", t));
        Assert.DoesNotContain(cutPiece[..^1], terms);
    }

    [Fact]
    public void ErkannteSpracheWirdZuIso6391()
    {
        Assert.Equal("de", ElevenLabsStt.IsoLanguage("deu"));
        Assert.Equal("en", ElevenLabsStt.IsoLanguage("eng"));
        Assert.Equal("fr", ElevenLabsStt.IsoLanguage("FRA"));
        Assert.Equal("de", ElevenLabsStt.IsoLanguage("de"));
        Assert.Equal("xyz", ElevenLabsStt.IsoLanguage("xyz"));
    }
}
