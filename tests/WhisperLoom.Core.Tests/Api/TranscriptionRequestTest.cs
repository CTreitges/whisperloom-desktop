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
        Assert.Equal(["model", "response_format", "languages[]", "prompt"], Names(f));
        Assert.Contains(("languages[]", "de"), f);
        Assert.Contains(("prompt", "Namen: Chris"), f);
        Assert.DoesNotContain("language", Names(f));
    }

    [Fact]
    public void AltesOpenAiModellSendetLanguage() =>
        Assert.Equal(
            [("model", "gpt-4o-transcribe"), ("response_format", "json"), ("language", "de")],
            TranscriptionRequest.Fields(Stt("openai", "gpt-4o-transcribe"), "de", ""));

    [Fact]
    public void GroqBekommtAlleFelder()
    {
        var f = TranscriptionRequest.Fields(Stt("groq"), "en", "ctx");
        Assert.Equal(["model", "response_format", "language", "prompt"], Names(f));
        Assert.Equal("whisper-large-v3-turbo", f[0].Value);
    }

    [Fact]
    public void MistralBekommtWederPromptNochResponseFormat() =>
        Assert.Equal([("model", "voxtral-mini-latest"), ("language", "de")], TranscriptionRequest.Fields(Stt("mistral"), "de", "ctx"));

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
