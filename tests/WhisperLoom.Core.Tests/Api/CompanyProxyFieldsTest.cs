using System.Text.RegularExpressions;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>
/// W3-REST (4): Der Firmen-Eintrag schickt dem Proxy Vokabular (<c>keywords[]</c>) und „Deutsch + Englisch“ (zweimal
/// <c>languages[]</c>) in neutraler Form; der Server übersetzt je Anbieter (docs/server-api.md §3.7, W3-REST).
/// Fake-Server statt Firmen-Server, nur Fake-Token.
/// </summary>
public sealed partial class CompanyProxyFieldsTest
{
    private const string Vocabulary = "Treitges, WhisperLoom, Firmen Manager, <tag>, Das ist ein ganzer Satz.";

    [GeneratedRegex("name=\"([^\"]+)\"\r\n\r\n([^\r]*)\r\n")]
    private static partial Regex Part();

    private static ApiAccess Company(string serverUrl, string model = "")
    {
        var s = new WhisperLoomSettings();
        s.Management.ServerUrl = serverUrl;
        s.Management.State = "approved";
        var dir = ProviderDirectory.For(s).WithKeys(new Dictionary<string, string> { [CompanyProvider.EntryId] = "wld_test-token-not-real-1" });
        return AccessResolver.ResolveStt(CompanyProvider.EntryId, "", "", model, providers: dir);
    }

    [Fact]
    public async Task Firmen_Eintrag_sendet_keywords_und_beide_Sprachen_an_den_Proxy()
    {
        await using var server = new FakeServer();
        FakeRequest? seen = null;
        server.Map("/api/v1/proxy/audio/transcriptions", r =>
        {
            seen = r;
            return new FakeResponse(200, """{"text":"ok","language":"de"}""");
        });
        var access = Company(server.Base, "gpt-transcribe");

        var result = await new ApiTranscriber(new HttpClient(), access, Vocabulary)
            .TranscribeAsync(new WavUpload(new short[1600]), WhisperLoomSettings.LanguageDeEn, CancellationToken.None);

        Assert.Equal("ok", result.Text);
        var fields = Part().Matches(seen!.Latin1).Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        Assert.Equal(
            [
                ("model", "gpt-transcribe"),
                ("response_format", "json"),
                ("languages[]", "de"),
                ("languages[]", "en"),
                ("prompt", Vocabulary),
                ("keywords[]", "Treitges"),
                ("keywords[]", "WhisperLoom"),
                ("keywords[]", "Firmen Manager"),
            ],
            fields);
        Assert.Equal("Bearer wld_test-token-not-real-1", seen.Header("Authorization"));
    }

    [Fact]
    public void Eine_Sprache_bleibt_language_und_ohne_Vokabular_keine_keywords()
    {
        var f = TranscriptionRequest.Fields(Company("https://wl.example.test"), "de", "");
        Assert.Contains(("language", "de"), f);
        Assert.DoesNotContain(f, x => x.Name is "languages[]" or "keywords[]" or "context_bias[]");
    }

    [Fact]
    public void Hoechstens_100_Begriffe_die_letzten_wie_bei_ElevenLabs()
    {
        var many = string.Join(", ", Enumerable.Range(0, 150).Select(i => $"wort{i}"));
        var keywords = TranscriptionRequest.Fields(Company("https://wl.example.test"), "auto", many)
            .Where(x => x.Name == "keywords[]").Select(x => x.Value).ToList();
        Assert.Equal(100, keywords.Count);
        Assert.Equal(("wort50", "wort149"), (keywords[0], keywords[^1]));
    }

    [Fact]
    public void Eigener_Server_ohne_Firma_bleibt_unveraendert()
    {
        var f = TranscriptionRequest.Fields(AccessResolver.ResolveStt("custom", "https://eigen.example.test/v1", Keys.K1, "whisper-x", 0),
            WhisperLoomSettings.LanguageDeEn, "Treitges, WhisperLoom");
        Assert.DoesNotContain(f, x => x.Name is "languages[]" or "keywords[]" or "language");
    }
}
