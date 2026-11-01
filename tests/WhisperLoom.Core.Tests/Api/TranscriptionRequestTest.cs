using WhisperLoom.Core.Api;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/TranscriptionRequestTest.kt</c>: Multipart-Felder je Anbieter und Modell.</summary>
public class TranscriptionRequestTest
{
    private static ApiAccess Stt(string provider, string model = "", string url = "") =>
        AccessResolver.ResolveStt(provider, url, Keys.K1, model, 0);

    private static List<string> Names(IReadOnlyList<(string Name, string Value)> fields) => [.. fields.Select(f => f.Name)];

    [Fact]
    public void GptTranscribeSendetLanguagesArray()
    {
        var f = TranscriptionRequest.Fields(Stt("openai", "gpt-transcribe"), "de", "Namen: Chris");
        // CTR-273: das Vokabular geht zusätzlich als keywords[] raus.
        Assert.Equal(["model", "response_format", "languages[]", "prompt", "keywords[]"], Names(f));
        Assert.Contains(("languages[]", "de"), f);
        Assert.Contains(("prompt", "Namen: Chris"), f);
        Assert.Contains(("keywords[]", "Namen: Chris"), f);
        Assert.DoesNotContain("language", Names(f));
    }

    // --- Vokabular als Wortliste (CTR-273, F5/F6) ----------------------------------------

    [Fact]
    public void GptTranscribeBekommtPromptUndKeywords() =>
        Assert.Equal(
            [
                ("model", "gpt-transcribe"),
                ("response_format", "json"),
                ("prompt", "Treitges, WhisperLoom, Voice Command"),
                ("keywords[]", "Treitges"),
                ("keywords[]", "WhisperLoom"),
                ("keywords[]", "Voice Command"),
            ],
            TranscriptionRequest.Fields(Stt("openai", "gpt-transcribe"), "auto", "Treitges, WhisperLoom, Voice Command"));

    /// <summary>Ein Begriff mit &lt; oder &gt; ließe OpenAI die ganze Anfrage ablehnen — er fällt weg, prompt bleibt.</summary>
    [Fact]
    public void KeywordMitSpitzerKlammerFaelltWeg()
    {
        var f = TranscriptionRequest.Fields(Stt("openai", "gpt-transcribe"), "de", "<b>Chris</b>, Anna, a>b, Satz am Ende.");
        Assert.Equal(["Anna"], f.Where(x => x.Name == "keywords[]").Select(x => x.Value));
        Assert.Contains(("prompt", "<b>Chris</b>, Anna, a>b, Satz am Ende."), f);
    }

    [Fact]
    public void AndereOpenAiModelleOhneKeywords()
    {
        Assert.DoesNotContain("keywords[]", Names(TranscriptionRequest.Fields(Stt("openai", "whisper-1"), "de", "Chris")));
        Assert.DoesNotContain("keywords[]", Names(TranscriptionRequest.Fields(Stt("groq"), "de", "Chris")));
    }

    [Fact]
    public void MistralBekommtVokabularAlsContextBiasOhnePrompt() =>
        Assert.Equal(
            [("model", "voxtral-mini-latest"), ("language", "de"), ("context_bias[]", "Treitges"), ("context_bias[]", "WhisperLoom")],
            TranscriptionRequest.Fields(Stt("mistral"), "de", "Treitges, WhisperLoom"));

    /// <summary>Mistral-Pattern <c>^[^,\s]+$</c>: mehrteilige Begriffe werden mit _ verbunden (Doku: affordable_health_care).</summary>
    [Fact]
    public void ContextBiasOhneLeerzeichen()
    {
        var bias = TranscriptionRequest.ContextBias("Christof  Treitges, Claude Code, Pro\tWidget");
        Assert.Equal(["Christof_Treitges", "Claude_Code", "Pro_Widget"], bias);
        Assert.All(bias, b => Assert.Matches(@"^[^,\s]+$", b));
    }

    /// <summary>Geschütztes Leerzeichen (U+00A0, U+202F) aus kopiertem Text: auch das wird zu _.</summary>
    [Fact]
    public void ContextBiasAuchOhneUnicodeLeerraum()
    {
        var bias = TranscriptionRequest.ContextBias("Dr Müller, 10 km");
        Assert.Equal(["Dr_Müller", "10_km"], bias);
        // .NET-\s ist Unicode-weit (wie Kotlins (?U)).
        Assert.All(bias, b => Assert.Matches(@"^[^,\s]+$", b));
    }

    /// <summary>Freitext ist kein Schlagwort: mehr als 5 Wörter oder 49 Zeichen fallen weg (wie bei ElevenLabs).</summary>
    [Fact]
    public void WortlistenOhneFreitext()
    {
        var prompt = $"Bitte WhisperLoom immer zusammen schreiben und so, {new string('x', 50)}, Chris";
        Assert.Equal(["Chris"], TranscriptionRequest.ContextBias(prompt));
        Assert.Equal(["Chris"], TranscriptionRequest.Keywords(prompt));
        Assert.Equal(["a b c d e"], TranscriptionRequest.Keywords("a b c d e, a b c d e f"));
    }

    [Fact]
    public void ContextBiasHoechstensHundertUndDieLetztenGewinnen()
    {
        var prompt = string.Join(", ", Enumerable.Range(1, 130).Select(i => $"w{i}"));
        var bias = TranscriptionRequest.ContextBias(prompt);
        Assert.Equal(100, bias.Count);
        Assert.Equal("w31", bias[0]);
        Assert.Equal("w130", bias[^1]);
    }

    [Fact]
    public void OhneVokabularKeineWortlisten()
    {
        Assert.DoesNotContain("context_bias[]", Names(TranscriptionRequest.Fields(Stt("mistral"), "de", "  ")));
        Assert.DoesNotContain("keywords[]", Names(TranscriptionRequest.Fields(Stt("openai", "gpt-transcribe"), "de", "")));
        // OpenRouter (auch Voxtral) kennt context_bias nicht.
        Assert.DoesNotContain("context_bias[]", Names(TranscriptionRequest.Fields(Stt("openrouter"), "de", "Chris")));
    }

    // --- „Deutsch + Englisch“ (CTR-273, F3) -----------------------------------------------

    [Fact]
    public void DeutschUndEnglischGehtAnGptTranscribeAlsZweiSprachen() =>
        Assert.Equal(
            [("model", "gpt-transcribe"), ("response_format", "json"), ("languages[]", "de"), ("languages[]", "en")],
            TranscriptionRequest.Fields(Stt("openai", "gpt-transcribe"), "de+en", ""));

    [Fact]
    public void DeutschUndEnglischOhneSprachfeldBeiEinemCode()
    {
        // whisper-1, Groq, Mistral, eigener Server kennen nur einen Code → der Anbieter erkennt selbst.
        foreach (var access in new[] { Stt("openai", "whisper-1"), Stt("groq"), Stt("mistral"), Stt("custom", "m", "http://s:8000/v1") })
        {
            Assert.DoesNotContain(Names(TranscriptionRequest.Fields(access, "de+en", "")), n => n.StartsWith("language", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AltesOpenAiModellSendetLanguage() =>
        Assert.Equal(
            [("model", "whisper-1"), ("response_format", "json"), ("language", "de")],
            TranscriptionRequest.Fields(Stt("openai", "whisper-1"), "de", ""));

    [Fact]
    public void GroqBekommtAlleFelder()
    {
        var f = TranscriptionRequest.Fields(Stt("groq"), "en", "ctx");
        Assert.Equal(["model", "response_format", "language", "prompt"], Names(f));
        Assert.Equal("whisper-large-v3-turbo", f[0].Value);
    }

    [Fact]
    public void MistralBekommtWederPromptNochResponseFormat() =>
        // CTR-273: das Vokabular kommt als context_bias statt als prompt an.
        Assert.Equal(
            [("model", "voxtral-mini-latest"), ("language", "de"), ("context_bias[]", "ctx")],
            TranscriptionRequest.Fields(Stt("mistral"), "de", "ctx"));

    [Fact]
    public void OpenRouterBekommtKeinenPrompt() =>
        Assert.Equal(["model", "response_format", "language"], Names(TranscriptionRequest.Fields(Stt("openrouter"), "de", "ctx")));

    [Fact]
    public void AutoLaesstDieSpracheWeg()
    {
        Assert.DoesNotContain(Names(TranscriptionRequest.Fields(Stt("openai", "gpt-transcribe"), "auto", "")), n => n.StartsWith("language", StringComparison.Ordinal));
        Assert.DoesNotContain("language", Names(TranscriptionRequest.Fields(Stt("groq"), "", "")));
    }

    [Fact]
    public void LeererPromptWirdNichtGesendet() =>
        Assert.DoesNotContain("prompt", Names(TranscriptionRequest.Fields(Stt("groq"), "de", "   ")));

    [Fact]
    public void EigenerServerMitFreiemModell() =>
        Assert.Equal(
            [("model", "Systran/faster-whisper-medium"), ("response_format", "json"), ("language", "de"), ("prompt", "ctx")],
            TranscriptionRequest.Fields(Stt("custom", "Systran/faster-whisper-medium", "http://s:8000/v1"), "de", "ctx"));

    [Fact]
    public void EigenerServerOhneModellSendetKeinLeeresModelFeld()
    {
        Assert.Equal([("response_format", "json"), ("language", "de")], TranscriptionRequest.Fields(Stt("custom", "", "http://s:8000/v1"), "de", ""));
        Assert.DoesNotContain("model", Names(TranscriptionRequest.Fields(Stt("custom", "   ", "http://s:8000/v1"), "auto", "")));
    }

    [Fact]
    public void EndpunktAusBaseUrlOderOverride()
    {
        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", TranscriptionRequest.Url(Stt("openai")));
        Assert.Equal("http://s:8000/v1/audio/transcriptions", TranscriptionRequest.Url(Stt("custom", url: "http://s:8000/v1/")));
        Assert.Equal("https://api.deepinfra.com/v1/audio/transcriptions", TranscriptionRequest.Url(Stt("deepinfra")));
    }
}
