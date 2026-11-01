using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>
/// Port von <c>api/TranscriptionResponseTest.kt</c> (CTR-273, 273-1): erkannte Sprache aus der Antwort von
/// POST /audio/transcriptions (<see cref="TranscriptionRequest.DetectedLanguage"/>).
/// </summary>
public class TranscriptionResponseTest
{
    private static string? Detected(string body) => TranscriptionRequest.DetectedLanguage(JsonNode.Parse(body)!.AsObject());

    /// <summary>OpenAI gpt-transcribe meldet <c>languages: [{code}]</c> statt <c>language</c>.</summary>
    [Fact]
    public void GptTranscribeMeldetDieSpracheAlsListe() =>
        Assert.Equal("de", Detected("""{"text":"Hallo Welt","languages":[{"code":"de"}]}"""));

    /// <summary>OpenAI nimmt auch ISO-639-3 an; kommt es so zurück, rechnet die App mit ISO-639-1.</summary>
    [Fact]
    public void DreistelligerCodeWirdZweistellig()
    {
        Assert.Equal("de", Detected("""{"text":"Hallo","languages":[{"code":"deu"}]}"""));
        Assert.Equal("en", Detected("""{"text":"Hi","languages":[{"code":"EN"}]}"""));
    }

    [Fact]
    public void BeiMehrerenSprachenZaehltDieErste() =>
        Assert.Equal("en", Detected("""{"text":"Meeting um zehn","languages":[{"code":"en"},{"code":"de"}]}"""));

    /// <summary>Unsicher erkannt: OpenAI liefert eine leere Liste.</summary>
    [Fact]
    public void LeereListeHeisstKeineSprache()
    {
        Assert.Null(Detected("""{"text":"…","languages":[]}"""));
        Assert.Null(Detected("""{"text":"…","languages":[{"code":""}]}"""));
    }

    /// <summary>verbose_json und eigene Server (Whisper-Familie): wie bisher das Feld <c>language</c>.</summary>
    [Fact]
    public void VerboseJsonLiefertLanguage()
    {
        Assert.Equal("german", Detected("""{"text":"Hallo","language":"german"}"""));
        Assert.Equal("de", Detected("""{"text":"Hallo","language":"de"}"""));
    }

    [Fact]
    public void OhneSprachfeldNull()
    {
        Assert.Null(Detected("""{"text":"Hallo"}"""));
        Assert.Null(Detected("""{"text":"Hallo","language":""}"""));
    }
}
