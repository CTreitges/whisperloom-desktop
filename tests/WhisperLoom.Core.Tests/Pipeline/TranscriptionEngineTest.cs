using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>
/// Port von <c>TranscriptionEngineTest.kt</c>: Backend-Wahl und der ganze Diktat-Pfad gegen einen „eigenen Server“
/// (Fake): Multipart-Felder, Header, Chat-Body, Nacharbeit. Meist mit Aufzeichnern geprüft (was die Pipeline an Polisher
/// und Prompt-Builder übergibt); die Kotlin-Text-Literale prüfen die Fälle mit <c>RealText</c> (<see cref="CoreText"/>).
/// </summary>
public sealed class TranscriptionEngineTest : IAsyncLifetime
{
    private const string Raw = "also ähm hallo welt";

    private readonly PipelineHarness _h = new();
    private FakeRequest? _stt;
    private FakeRequest? _chat;
    private FakeRequest? _ollama;
    private FakeRequest? _eleven;
    private string _sttResponse = $$"""{"text":"{{Raw}}"}""";
    private int _sttStatus = 200;
    private string _chatResponse = """{"choices":[{"message":{"content":"<think>ueberlegen</think>Hallo Welt."}}]}""";
    private int _chatStatus = 200;
    private string _ollamaResponse = """{"message":{"role":"assistant","content":"Hallo Welt.","thinking":"nachdenken"},"done":true}""";
    private int _ollamaStatus = 200;
    private string _elevenResponse = $$"""{"language_code":"deu","language_probability":0.98,"text":"{{Raw}}"}""";
    private int _elevenStatus = 200;

    public ValueTask InitializeAsync()
    {
        _h.Server.Map("/v1/audio/transcriptions", r =>
        {
            _stt = r;
            return new FakeResponse(_sttStatus, _sttResponse);
        });
        _h.Server.Map("/v1/chat/completions", r =>
        {
            _chat = r;
            return new FakeResponse(_chatStatus, _chatResponse);
        });
        _h.Server.Map("/v1/speech-to-text", r =>
        {
            _eleven = r;
            return new FakeResponse(_elevenStatus, _elevenResponse);
        });
        _h.Server.Map("/api/chat", r =>
        {
            _ollama = r;
            return new FakeResponse(_ollamaStatus, _ollamaResponse);
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> Transcribe(Action<string>? onSkipped = null)
    {
        var d = await _h.TranscribeAsync();
        if (d.Result.Skipped is { } s) onSkipped?.Invoke(s);
        return d.Text;
    }

    private static (string System, string User) Messages(FakeRequest r)
    {
        var m = JsonNode.Parse(r.Text)!["messages"]!.AsArray();
        return ((string)m[0]!["content"]!, (string)m[1]!["content"]!);
    }

    private string Build(RefineMode mode, bool smartFillers = false, bool paragraphs = true, bool german = true) =>
        _h.Prompts.Build(mode, german, smartFillers, paragraphs, isShort: true);

    private void AssertRawWithFullRules(string text)
    {
        Assert.Equal(Raw, text);
        Assert.Equal(Raw, _h.Polisher.Last.Text);
        Assert.True(_h.Polisher.Last.Options.RemoveFillers);
        Assert.False(_h.Polisher.Last.Options.KeepLineBreaks);
    }

    private void UseOllama(string provider, string key = "", string model = "gemma4:31b")
    {
        _h.UseLocalServer();
        _h.Stage(RefineMode.Polish);
        _h.Settings.Llm.ProviderId = provider;
        _h.Settings.Llm.BaseUrl = _h.V1;
        _h.Settings.Llm.Model = model;
        if (key.Length > 0) _h.Secrets.Set(SecretNames.Llm, key);
    }

    private void UseElevenLabs()
    {
        _h.Online();
        _h.Settings.Stt.ProviderId = "elevenlabs";
        _h.Settings.Stt.BaseUrl = _h.V1;
        _h.Secrets.Set(SecretNames.Stt, Keys.K1);
        _h.Settings.Language = "auto";
    }

    private void UseTogether()
    {
        _h.Online();
        _h.Settings.Stt.ProviderId = "together";
        _h.Settings.Stt.BaseUrl = _h.V1;
        _h.Secrets.Set(SecretNames.Stt, Keys.K2);
        _h.Stage(RefineMode.Polish);
    }

    [Fact]
    public async Task OhneEngineNichtKonfiguriert()
    {
        _h.Secrets.Set(SecretNames.Stt, Keys.K1);
        Assert.False(await _h.Pipeline.IsConfiguredAsync(Ct));
        await Assert.ThrowsAsync<ApiNotConfiguredException>(() => Transcribe());
    }

    [Fact]
    public async Task OnlineOhneKeyBeiCloudAnbieterNichtKonfiguriert()
    {
        _h.Online();
        Assert.False(await _h.Pipeline.IsConfiguredAsync(Ct));
        _h.Secrets.Set(SecretNames.Stt, Keys.K1);
        Assert.True(await _h.Pipeline.IsConfiguredAsync(Ct));
    }

    [Fact]
    public async Task EigenerServerBrauchtNurEineUrl()
    {
        _h.Online();
        _h.Settings.Stt.ProviderId = "custom";
        Assert.False(await _h.Pipeline.IsConfiguredAsync(Ct));
        _h.Settings.Stt.BaseUrl = "http://192.168.1.50:8000/v1";
        Assert.True(await _h.Pipeline.IsConfiguredAsync(Ct));
    }

    [Fact]
    public async Task OfflineOhneModellWirftLocalNotAvailable()
    {
        _h.Settings.Engine = "offline";
        _h.LocalStt.Ready = false;
        Assert.False(await _h.Pipeline.IsConfiguredAsync(Ct));
        Assert.Equal("Lokal · Small", (await _h.Pipeline.BackendAsync(Ct)).Label);
        await Assert.ThrowsAsync<LocalNotAvailableException>(() => Transcribe());
    }

    /// <summary>Kotlin: Modelldatei auf der Platte. Desktop: der Begleitprozess (Fake) meldet sich bereit.</summary>
    [Fact]
    public async Task OfflineMitBereitemModellIstKonfiguriert()
    {
        _h.Settings.Engine = "offline";
        Assert.True(await _h.Pipeline.IsConfiguredAsync(Ct));
        var d = await _h.TranscribeAsync();
        Assert.Equal(Raw, d.Raw);
        Assert.Equal("Lokal · Small", d.SttLabel);
        Assert.Equal(1, _h.LocalStt.Calls);
        _h.LocalStt.Ready = false;
        Assert.False(await _h.Pipeline.IsConfiguredAsync(Ct));
    }

    [Fact]
    public async Task OnlineBackendTraegtDenAnbieternamen()
    {
        _h.Online();
        _h.Settings.Stt.ProviderId = "groq";
        Assert.Equal("Groq", (await _h.Pipeline.BackendAsync(Ct)).Label);
    }

    [Fact]
    public async Task DiktatGegenEigenenServerOhneKeyUndOhneKi()
    {
        _h.UseLocalServer();
        AssertRawWithFullRules(await Transcribe());
        Assert.Null(_stt!.Header("Authorization"));
        var body = _stt.Latin1;
        Assert.Contains("name=\"model\"\r\n\r\nwhisper-1\r\n", body, StringComparison.Ordinal);
        Assert.Contains("name=\"language\"\r\n\r\nde\r\n", body, StringComparison.Ordinal);
        Assert.Contains("name=\"response_format\"\r\n\r\njson\r\n", body, StringComparison.Ordinal);
        Assert.Contains("filename=\"audio.wav\"", body, StringComparison.Ordinal);
        Assert.Contains("RIFF", body, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"prompt\"", body, StringComparison.Ordinal);
        Assert.Null(_chat);
        Assert.Single(_h.Trimmed);
    }

    [Fact]
    public async Task DiktatOhneKiNachDenVollenRegeln()
    {
        _h.UseLocalServer();
        _h.RealText = true;
        Assert.Equal("Also hallo welt", await Transcribe()); // Füllwort weg, Satzanfang groß
    }

    [Fact]
    public async Task DiktatMitKiGlaettungGegenEigenenServer()
    {
        _h.UseLocalServer();
        _h.Stage(RefineMode.Polish);
        _h.Vocabulary = "Chris";
        Assert.Equal("Hallo Welt.", await Transcribe());
        Assert.Contains("name=\"prompt\"\r\n\r\nChris\r\n", _stt!.Latin1, StringComparison.Ordinal);
        var chat = JsonNode.Parse(_chat!.Text)!.AsObject();
        Assert.Equal("qwen3:8b", (string?)chat["model"]);
        Assert.Equal(0, (int)chat["temperature"]!);
        Assert.Equal("none", (string?)chat["reasoning_effort"]);
        Assert.False(chat.ContainsKey("max_completion_tokens"));
        Assert.Equal((Build(RefineMode.Polish), $"<diktat>\n{Raw}\n</diktat>"), Messages(_chat));
    }

    [Fact]
    public async Task GlaettenSchicktDenKorrekturPrompt()
    {
        _h.UseLocalServer();
        _h.RealText = true;
        _h.Stage(RefineMode.Polish);
        Assert.Equal("Hallo Welt.", await Transcribe());
        var (system, user) = Messages(_chat!);
        Assert.Contains("Du korrigierst in diktiertem Text nur Satzzeichen", system, StringComparison.Ordinal);
        Assert.Equal("<diktat>\nalso ähm hallo welt\n</diktat>", user);
    }

    [Fact]
    public async Task DiktatMitOllamaImHeimnetz()
    {
        UseOllama("ollama");
        Assert.Equal("Hallo Welt.", await Transcribe());
        Assert.Null(_chat);
        Assert.Null(_ollama!.Header("Authorization"));
        var chat = JsonNode.Parse(_ollama.Text)!.AsObject();
        Assert.Equal("gemma4:31b", (string?)chat["model"]);
        Assert.False((bool)chat["stream"]!);
        Assert.False(chat.ContainsKey("think"));
        Assert.Equal(0, (int)chat["options"]!["temperature"]!);
        Assert.Equal((Build(RefineMode.Polish), $"<diktat>\n{Raw}\n</diktat>"), Messages(_ollama));
    }

    [Fact]
    public async Task DiktatMitOllamaCloudSendetDenKey()
    {
        UseOllama("ollama-cloud", Keys.K3, "gpt-oss:20b");
        Assert.Equal("Hallo Welt.", await Transcribe());
        Assert.Equal($"Bearer {Keys.K3}", _ollama!.Header("Authorization"));
        Assert.Equal("low", (string?)JsonNode.Parse(_ollama.Text)!["think"]);
    }

    [Fact]
    public async Task OllamaFehlerLiefertDenRohtextMitHinweis()
    {
        UseOllama("ollama-cloud", Keys.K4);
        _ollamaStatus = 401;
        _ollamaResponse = """{"error":"Unauthorized"}""";
        var hinweis = "";
        AssertRawWithFullRules(await Transcribe(s => hinweis = s));
        Assert.Contains("Unauthorized", hinweis, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KiFehlerMitIntelligentenFuellwoerternRaeumtTrotzdemAuf()
    {
        UseOllama("ollama");
        _h.Settings.Refine.PolishCleanup = "clean";
        _ollamaStatus = 500;
        AssertRawWithFullRules(await Transcribe());
    }

    [Fact]
    public async Task OhneFuellwoerterSchicktDenZweigUndDieListeFaengtReste()
    {
        UseOllama("ollama");
        _h.Settings.Refine.PolishCleanup = "clean";
        _h.Settings.Polish.CustomFillers = ["sozusagen"];
        _ollamaResponse = """{"message":{"content":"Also, ähm, hallo Welt, sozusagen."}}""";
        Assert.Equal("Also, ähm, hallo Welt, sozusagen.", await Transcribe());
        var (text, options) = _h.Polisher.Last;
        Assert.Equal("Also, ähm, hallo Welt, sozusagen.", text);
        Assert.True(options.RemoveFillers);
        Assert.Contains("sozusagen", options.CustomFillers!);
        Assert.Equal(Build(RefineMode.Polish, smartFillers: true), Messages(_ollama!).System);
    }

    [Fact]
    public async Task DieFuellwortListeFaengtResteNachDerKi()
    {
        UseOllama("ollama");
        _h.RealText = true;
        _h.Settings.Refine.PolishCleanup = "clean";
        _h.Settings.Polish.CustomFillers = ["sozusagen"];
        // Ein kleines Modell übersieht ein „ähm“ — die Liste fängt es danach.
        _ollamaResponse = """{"message":{"content":"Also, ähm, hallo Welt, sozusagen."}}""";
        Assert.Equal("Also, hallo Welt.", await Transcribe());
        Assert.Contains("nie Wörter außer Füllwörtern", Messages(_ollama!).System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeereKiAntwortGiltAlsOhneKiMitHinweis()
    {
        UseOllama("ollama");
        _h.Settings.Refine.PolishCleanup = "clean";
        _h.Settings.Refine.ParagraphsPolish = true;
        _ollamaResponse = """{"message":{"content":""},"done":true}""";
        string? hinweis = null;
        AssertRawWithFullRules(await Transcribe(s => hinweis = s));
        Assert.Equal(TextRefiner.MsgEmpty, hinweis);
    }

    [Fact]
    public async Task LeereAntwortUeberChatCompletionsGiltAlsOhneKi()
    {
        _h.UseLocalServer();
        _h.Stage(RefineMode.Beautify);
        _chatResponse = """{"choices":[{"message":{"content":"<think>hm</think>  "},"finish_reason":"stop"}]}""";
        string? hinweis = null;
        AssertRawWithFullRules(await Transcribe(s => hinweis = s));
        Assert.Equal(TextRefiner.MsgEmpty, hinweis);
    }

    [Fact]
    public async Task VerschoenernBekommtKeinenFuellwortZusatz()
    {
        UseOllama("ollama");
        _h.Stage(RefineMode.Beautify);
        _h.Settings.Refine.PolishCleanup = "clean";
        _ollamaResponse = """{"message":{"content":"Also, ähm, hallo Welt."}}""";
        await Transcribe();
        Assert.True(_h.Polisher.Last.Options.RemoveFillers);
        Assert.Equal(Build(RefineMode.Beautify), Messages(_ollama!).System);
    }

    [Fact]
    public async Task DiktatIgnoriertDieBereinigungDerSprachnachrichten()
    {
        UseOllama("ollama");
        _h.Settings.Share.Stage = "polish";
        _h.Settings.Share.PolishCleanup = "clean";
        await Transcribe();
        Assert.Equal(Build(RefineMode.Polish), Messages(_ollama!).System);
    }

    [Fact]
    public async Task AbsaetzeBleibenStandardmaessigErhalten()
    {
        UseOllama("ollama");
        _ollamaResponse = """{"message":{"content":"Erster Absatz.\n\nZweiter Absatz."}}""";
        Assert.Equal("Erster Absatz.\n\nZweiter Absatz.", await Transcribe());
        Assert.True(_h.Polisher.Last.Options.KeepLineBreaks);
    }

    [Fact]
    public async Task OhneAbsaetzeKommtEinFliesstext()
    {
        UseOllama("ollama");
        _h.Settings.Refine.ParagraphsPolish = false;
        _ollamaResponse = """{"message":{"content":"Erster Absatz.\n\nZweiter Absatz."}}""";
        await Transcribe();
        Assert.False(_h.Polisher.Last.Options.KeepLineBreaks);
        Assert.Equal(Build(RefineMode.Polish, paragraphs: false), Messages(_ollama!).System);
    }

    [Fact]
    public async Task AbsaetzeGeltenJeStufe()
    {
        UseOllama("ollama");
        _h.Settings.Refine.ParagraphsBeautify = false;
        _ollamaResponse = """{"message":{"content":"Erster Absatz.\n\nZweiter Absatz."}}""";
        await Transcribe();
        Assert.True(_h.Polisher.Last.Options.KeepLineBreaks);
        _h.Stage(RefineMode.Beautify);
        await Transcribe();
        Assert.False(_h.Polisher.Last.Options.KeepLineBreaks);
        Assert.Equal(Build(RefineMode.Beautify, paragraphs: false), Messages(_ollama!).System);
    }

    [Fact]
    public async Task ZusammenfassenAlsFliesstextOhneListe()
    {
        UseOllama("ollama");
        _h.Stage(RefineMode.Summarize);
        _h.Settings.Refine.SummarizeForm = "prose";
        _ollamaResponse = """{"message":{"content":"Kurz gesagt.\n- Eins.\n- Zwei."}}""";
        await Transcribe();
        Assert.False(_h.Polisher.Last.Options.KeepLineBreaks);
        Assert.Equal(Build(RefineMode.Summarize, paragraphs: false), Messages(_ollama!).System);
        _h.Settings.Refine.SummarizeForm = "auto";
        Assert.Equal("Kurz gesagt.\n- Eins.\n- Zwei.", await Transcribe());
        Assert.True(_h.Polisher.Last.Options.KeepLineBreaks);
    }

    [Fact]
    public async Task AbsaetzeZiehtDerPolisherZusammen()
    {
        UseOllama("ollama");
        _h.RealText = true;
        // Auch wenn das Modell die Anweisung ignoriert: die Nachbearbeitung zieht zusammen.
        _h.Settings.Refine.ParagraphsPolish = false;
        _ollamaResponse = """{"message":{"content":"Erster Absatz.\n\nZweiter Absatz."}}""";
        Assert.Equal("Erster Absatz. Zweiter Absatz.", await Transcribe());
        Assert.Contains("einen einzigen durchgehenden Absatz", Messages(_ollama!).System, StringComparison.Ordinal);

        _h.Stage(RefineMode.Summarize);
        _h.Settings.Refine.SummarizeForm = "prose";
        _ollamaResponse = """{"message":{"content":"Kurz gesagt.\n- Eins.\n- Zwei."}}""";
        Assert.Equal("Kurz gesagt. - Eins. - Zwei.", await Transcribe());
        Assert.Contains("ohne Liste und ohne Zeilenumbruch", Messages(_ollama!).System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LesbarerGlaettenSchicktDenLesbarPromptUndDieWortlisteFaengtReste()
    {
        UseOllama("ollama");
        _h.Settings.Refine.PolishCleanup = "readable";
        _ollamaResponse = """{"message":{"content":"Also, ähm, hallo Welt."}}""";
        await Transcribe();
        Assert.True(_h.Polisher.Last.Options.RemoveFillers);
        Assert.Equal((Build(RefineMode.Readable), $"<diktat>\n{Raw}\n</diktat>"), Messages(_ollama!));
        Assert.Equal("polish", _h.Settings.Refine.Stage);
    }

    [Fact]
    public async Task OhneLesbarBleibtGlaettenKorrektorat()
    {
        UseOllama("ollama");
        await Transcribe();
        Assert.Equal(Build(RefineMode.Polish), Messages(_ollama!).System);
    }

    [Fact]
    public async Task DiktatIgnoriertDenLesbarSchalterDerSprachnachrichten()
    {
        UseOllama("ollama");
        _h.Settings.Share.Stage = "polish";
        _h.Settings.Share.PolishCleanup = "readable";
        await Transcribe();
        Assert.Equal(Build(RefineMode.Polish), Messages(_ollama!).System);
    }

    [Fact]
    public async Task KiDieEineBitteErfuelltLiefertDenRohtextMitHinweis()
    {
        UseOllama("ollama");
        var einladung = string.Join(" ", Enumerable.Repeat("Ihr seid alle herzlich zu meinem Geburtstag eingeladen.", 10));
        _ollamaResponse = new JsonObject { ["message"] = new JsonObject { ["content"] = einladung } }.ToJsonString();
        var hinweis = "";
        AssertRawWithFullRules(await Transcribe(s => hinweis = s));
        Assert.Contains("statt den Text zu bearbeiten", hinweis, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkierungUndVorredeKommenNieInsTextfeld()
    {
        UseOllama("ollama");
        _ollamaResponse = """{"message":{"content":"Hier ist der geglättete Text:\n<diktat>\nAlso, hallo Welt.\n</diktat>"}}""";
        Assert.Equal("Also, hallo Welt.", await Transcribe());
    }

    [Fact]
    public async Task PromptStufeSchicktDasDiktatMarkiertUndBehaeltDieGliederung()
    {
        UseOllama("ollama");
        _h.Stage(RefineMode.Prompt);
        _h.Settings.Refine.ParagraphsPolish = false;
        _ollamaResponse = """{"message":{"content":"Hier ist dein Prompt:\nErstelle mir eine Einkaufsliste.\n- 12 Personen\n- Budget höchstens 100 Euro"}}""";
        Assert.Equal("Erstelle mir eine Einkaufsliste.\n- 12 Personen\n- Budget höchstens 100 Euro", await Transcribe());
        var options = _h.Polisher.Last.Options;
        Assert.False(options.RemoveFillers);
        Assert.False(options.AutoCapitalize);
        Assert.True(options.KeepLineBreaks);
        Assert.Equal((Build(RefineMode.Prompt), $"<diktat>\n{Raw}\n</diktat>"), Messages(_ollama!));
    }

    [Fact]
    public async Task PromptStufeDieAntwortetLiefertDenRohtextMitHinweis()
    {
        UseOllama("ollama");
        _h.Stage(RefineMode.Prompt);
        var gedicht = string.Join(" ", Enumerable.Repeat("Die Welt ist schön, das Licht ist hell.", 10));
        _ollamaResponse = new JsonObject { ["message"] = new JsonObject { ["content"] = gedicht } }.ToJsonString();
        var hinweis = "";
        AssertRawWithFullRules(await Transcribe(s => hinweis = s));
        Assert.Contains("statt einen Prompt", hinweis, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptOhneSchalterLaeuftAlsGlaetten()
    {
        UseOllama("ollama");
        _h.Settings.Refine.Stage = "prompt";
        _h.Settings.Refine.PromptEnabled = false;
        await Transcribe();
        Assert.Equal((Build(RefineMode.Polish), $"<diktat>\n{Raw}\n</diktat>"), Messages(_ollama!));
    }

    /// <summary>Kotlin liest Liste + Datei über VocabularySource (WLD-P1a). Hier: der Vokabular-Prompt wird bei jedem Diktat neu geholt.</summary>
    [Fact]
    public async Task VokabularWirdBeiJedemDiktatNeuGeholt()
    {
        _h.UseLocalServer();
        _h.Vocabulary = "Treitges, Anna, Kubernetes";
        await Transcribe();
        Assert.Contains("name=\"prompt\"\r\n\r\nTreitges, Anna, Kubernetes\r\n", _stt!.Latin1, StringComparison.Ordinal);
        _h.Vocabulary = "Treitges, WhisperLoom, Anna, Kubernetes";
        await Transcribe();
        Assert.Contains("name=\"prompt\"\r\n\r\nTreitges, WhisperLoom, Anna, Kubernetes\r\n", _stt!.Latin1, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VokabularAusListeUndDateiGehtAlsPrompt()
    {
        _h.UseLocalServer();
        var dir = Directory.CreateTempSubdirectory("wl-vocab-");
        try
        {
            var file = Path.Combine(dir.FullName, "vokabular.md");
            await File.WriteAllTextAsync(file, "# Namen\n- Treitges\n- anna\n", Ct);
            _h.Settings.Stt.Vocabulary = "Anna\nKubernetes";
            _h.Settings.Stt.VocabularyFile = file;
            var pipeline = new DictationPipeline(() => _h.Settings, _h.Secrets, _h.Http, _h.Prompts, _h.Polisher, CoreText.Options());
            var request = new DictationRequest(PipelineHarness.Speech, "de", Refinement.Off);

            await pipeline.RunAsync(request, Ct);
            // Datei vorn, eigene Begriffe am Ende (Whisper beachtet das Ende); „anna“ doppelt → fällt weg.
            Assert.Contains("name=\"prompt\"\r\n\r\nTreitges, Anna, Kubernetes\r\n", _stt!.Latin1, StringComparison.Ordinal);

            // Bei jedem Diktat neu gelesen: eine Änderung an der Datei wirkt sofort.
            await File.WriteAllTextAsync(file, "- Treitges\n- WhisperLoom\n", Ct);
            await pipeline.RunAsync(request, Ct);
            Assert.Contains("name=\"prompt\"\r\n\r\nTreitges, WhisperLoom, Anna, Kubernetes\r\n", _stt!.Latin1, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task VerschwundeneDateiBrichtKeinDiktatAb()
    {
        _h.UseLocalServer();
        var pipeline = new DictationPipeline(() => _h.Settings, _h.Secrets, _h.Http, _h.Prompts, _h.Polisher, new DictationPipelineOptions
        {
            VocabularyPrompt = _ => throw new FileNotFoundException("gibtsnicht.md"),
        });
        var d = await pipeline.RunAsync(new DictationRequest(PipelineHarness.Speech, "de", Refinement.Off), Ct);
        Assert.Equal(Raw, d.Raw);
        Assert.DoesNotContain("name=\"prompt\"", _stt!.Latin1, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LlmFehlerLiefertDenRohtextMitHinweis()
    {
        _h.UseLocalServer();
        _h.Stage(RefineMode.Polish);
        _chatStatus = 401;
        _chatResponse = """{"error":{"message":"invalid api key"}}""";
        string? hint = null;
        AssertRawWithFullRules(await Transcribe(s => hint = s));
        Assert.Contains("401", hint, StringComparison.Ordinal);
        Assert.NotNull(_chat);
    }

    [Fact]
    public async Task LlmServerfehlerUndUnbrauchbareAntwortLiefernDenRohtext()
    {
        _h.UseLocalServer();
        _h.Stage(RefineMode.Beautify);
        _chatStatus = 500;
        string? hint = null;
        AssertRawWithFullRules(await Transcribe(s => hint = s));
        Assert.Contains("500", hint, StringComparison.Ordinal);
        _chatStatus = 200;
        _chatResponse = "<html>Not JSON</html>";
        hint = null;
        AssertRawWithFullRules(await Transcribe(s => hint = s));
        Assert.NotNull(hint);
    }

    [Fact]
    public async Task AbgeschnitteneKiAntwortLiefertDenRohtextMitHinweis()
    {
        _h.UseLocalServer();
        _h.Stage(RefineMode.Polish);
        _chatResponse = """{"choices":[{"message":{"content":"Also hallo"},"finish_reason":"length"}]}""";
        string? hint = null;
        AssertRawWithFullRules(await Transcribe(s => hint = s));
        Assert.Equal(TextRefiner.MsgTruncated, hint);
        _chatResponse = """{"choices":[{"message":{"content":"Hallo Welt."},"finish_reason":"stop"}]}""";
        hint = null;
        Assert.Equal("Hallo Welt.", await Transcribe(s => hint = s));
        Assert.Null(hint);
    }

    [Fact]
    public async Task AbgeschnitteneOllamaAntwortLiefertDenRohtextMitHinweis()
    {
        UseOllama("ollama");
        _ollamaResponse = """{"message":{"content":"Also hallo"},"done":true,"done_reason":"length"}""";
        string? hint = null;
        AssertRawWithFullRules(await Transcribe(s => hint = s));
        Assert.Equal(TextRefiner.MsgTruncated, hint);
    }

    [Fact]
    public async Task LlmOhneBaseUrlWirdUebersprungenUndSttFehlerBleibtEinFehler()
    {
        _h.UseLocalServer();
        _h.Stage(RefineMode.Polish);
        var llm = AccessResolver.ResolveLlm(AccessResolver.ResolveStt("groq", "", Keys.K1, "", 0), "custom", "", "", "qwen3:8b");
        Assert.Equal("", llm.BaseUrl);
        await Assert.ThrowsAsync<ApiNotConfiguredException>(() =>
            new TextRefiner(llm, _h.Http, _h.Prompts, new TextCleanup([])).RefineAsync("hallo", "de", new Refinement(RefineMode.Polish), Ct));

        _h.Settings.Llm.ProviderId = "same";
        _sttStatus = 500;
        _sttResponse = """{"error":{"message":"boom"}}""";
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => Transcribe());
        Assert.Equal(500, e.StatusCode);
    }

    [Fact]
    public async Task DiktatMitElevenLabsSchicktRohesPcmUndKeyterms()
    {
        UseElevenLabs();
        _h.Vocabulary = "Treitges, WhisperLoom";
        var d = await _h.TranscribeAsync();
        Assert.Equal("de", d.Language);
        Assert.Equal("de", _h.Polisher.Last.Options.Language);
        Assert.Equal(Keys.K1, _eleven!.Header("xi-api-key"));
        Assert.Null(_eleven.Header("Authorization"));
        var body = _eleven.Latin1;
        Assert.Contains("name=\"model_id\"\r\n\r\nscribe_v2\r\n", body, StringComparison.Ordinal);
        Assert.Contains("name=\"tag_audio_events\"\r\n\r\nfalse\r\n", body, StringComparison.Ordinal);
        Assert.Contains("name=\"timestamps_granularity\"\r\n\r\nnone\r\n", body, StringComparison.Ordinal);
        Assert.Contains("name=\"file_format\"\r\n\r\npcm_s16le_16\r\n", body, StringComparison.Ordinal);
        Assert.Contains("name=\"keyterms\"\r\n\r\nTreitges\r\n", body, StringComparison.Ordinal);
        Assert.Contains("name=\"keyterms\"\r\n\r\nWhisperLoom\r\n", body, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"language_code\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"model\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"prompt\"", body, StringComparison.Ordinal);
        Assert.Contains("filename=\"audio.pcm\"", body, StringComparison.Ordinal);
        var start = body.IndexOf("Content-Type: application/octet-stream\r\n\r\n", StringComparison.Ordinal) + 42;
        var file = body[start..body.LastIndexOf("\r\n--", StringComparison.Ordinal)];
        Assert.False(file.StartsWith("RIFF", StringComparison.Ordinal));
        Assert.Equal(PipelineHarness.Speech.Samples.Length * 2, file.Length);
    }

    [Fact]
    public async Task ElevenLabsSchicktDieGewaehlteSprache()
    {
        UseElevenLabs();
        _h.Settings.Language = "de";
        await Transcribe();
        Assert.Contains("name=\"language_code\"\r\n\r\nde\r\n", _eleven!.Latin1, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ElevenLabsOhneEigenenTextZugangUeberspringtDieVerbesserung()
    {
        UseElevenLabs();
        _h.Stage(RefineMode.Polish);
        string? hint = null;
        await Transcribe(s => hint = s);
        Assert.Equal(TextRefiner.MsgNoLlm, hint);
        Assert.Null(_chat);
    }

    [Fact]
    public async Task ElevenLabsMitAltemTextModellSchicktTrotzdemKeinenChat()
    {
        UseElevenLabs();
        _h.Stage(RefineMode.Polish);
        _h.Settings.Llm.Model = "gpt-4o-mini";
        string? hint = null;
        await Transcribe(s => hint = s);
        Assert.Equal(TextRefiner.MsgNoLlm, hint);
        Assert.Null(_chat);
    }

    [Fact]
    public async Task ElevenLabsMitEigenemTextZugangVerbessertAufDeutsch()
    {
        UseElevenLabs();
        _h.Stage(RefineMode.Polish);
        _h.OwnAccess();
        Assert.Equal("Hallo Welt.", await Transcribe());
        Assert.Equal(Build(RefineMode.Polish, german: true), Messages(_chat!).System);
    }

    [Fact]
    public async Task ElevenLabsFehlerSindLesbarMitHinweis()
    {
        UseElevenLabs();
        _elevenStatus = 401;
        _elevenResponse = """{"detail":{"type":"authentication_error","code":"unauthorized","message":"Invalid API key","status":"invalid_api_key"}}""";
        var e = await Assert.ThrowsAsync<ApiHttpException>(() => Transcribe());
        Assert.Equal(401, e.StatusCode);
        Assert.StartsWith("API-Fehler 401: Invalid API key", e.Message, StringComparison.Ordinal);
        Assert.Contains("„Speech to Text“", e.Message, StringComparison.Ordinal);

        _elevenStatus = 402;
        _elevenResponse = """{"detail":{"code":"insufficient_credits","message":"You have run out of credits"}}""";
        e = await Assert.ThrowsAsync<ApiHttpException>(() => Transcribe());
        Assert.Equal(402, e.StatusCode);
        Assert.Contains("Guthaben", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeyWirdAlsBearerGesendet()
    {
        _h.UseLocalServer();
        _h.Secrets.Set(SecretNames.Stt, Keys.K2);
        await Transcribe();
        Assert.Equal($"Bearer {Keys.K2}", _stt!.Header("Authorization"));
    }

    [Fact]
    public async Task AutoSpracheNimmtDieErkannteFuerDiePolitur()
    {
        _h.UseLocalServer();
        _h.Settings.Language = "auto";
        _sttResponse = """{"text":"i um think so","language":"en"}""";
        var d = await _h.TranscribeAsync();
        Assert.Equal("en", d.Language);
        Assert.Equal("en", _h.Polisher.Last.Options.Language);
        Assert.DoesNotContain("name=\"language\"", _stt!.Latin1, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoSpracheMitEnglischenFuellwoertern()
    {
        _h.UseLocalServer();
        _h.RealText = true;
        _h.Settings.Language = "auto";
        _sttResponse = """{"text":"i um think so","language":"en"}""";
        Assert.Equal("I think so", await Transcribe());
    }

    [Fact]
    public async Task LeereAntwortBleibtLeer()
    {
        _h.UseLocalServer();
        _sttResponse = """{"text":"   "}""";
        Assert.Equal("", await Transcribe());
    }

    [Fact]
    public void EffektiveSprache()
    {
        Assert.Equal("de", DictationPipeline.EffectiveLanguage("de", "en"));
        Assert.Equal("en", DictationPipeline.EffectiveLanguage("auto", "en"));
        Assert.Equal("auto", DictationPipeline.EffectiveLanguage("auto", null));
        Assert.Equal("auto", DictationPipeline.EffectiveLanguage("auto", " "));
    }

    [Fact]
    public async Task TogetherWieErkennungMitEingetipptemModellSchicktDenChatRequest()
    {
        UseTogether();
        _h.Settings.Llm.Model = "meta-llama/Llama-3.3-70B-Instruct-Turbo";
        Assert.Equal("Hallo Welt.", await Transcribe());
        Assert.Equal("meta-llama/Llama-3.3-70B-Instruct-Turbo", (string?)JsonNode.Parse(_chat!.Text)!["model"]);
    }

    [Fact]
    public async Task TogetherWieErkennungOhneModellUeberspringtMitHinweis()
    {
        UseTogether();
        string? hint = null;
        AssertRawWithFullRules(await Transcribe(s => hint = s));
        Assert.Equal(TextRefiner.MsgNoModel, hint);
        Assert.Null(_chat);
    }

    /// <summary>Desktop (Vertrag): Neu verarbeiten aus dem Rohtext wirft nur bei Abbruch — auch ohne eingerichtete Erkennung.</summary>
    [Fact]
    public async Task NeuVerarbeitenAusDemRohtextOhneAudio()
    {
        _h.Settings.Engine = "online";
        _h.OwnAccess();
        var r = await _h.Pipeline.RefineAsync(Raw, "de", new Refinement(RefineMode.Polish), Ct);
        Assert.Equal("Hallo Welt.", r.Text);
        Assert.Equal("qwen3:8b", r.Model);
        Assert.Null(_stt);
    }
}
