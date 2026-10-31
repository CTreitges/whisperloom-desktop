using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>
/// Port von <c>llm/LocalRefinerTest.kt</c>. Auf dem Desktop rechnet lokal Ollama auf 127.0.0.1:11434 (Goal: Ollama statt
/// LiteRT-LM) — derselbe <see cref="TextRefiner"/> mit Ollama-Zugang, hier gegen einen Fake-Ollama. Geprüft wird wie in
/// Kotlin, was hin- und zurückgeht: derselbe Auftrag und dieselbe Nacharbeit wie online.
/// </summary>
public sealed class LocalRefinerTest : IAsyncLifetime
{
    private readonly FakeServer _server = new();
    private readonly HttpClient _http = Http.CreateClient();
    private readonly FakePromptBuilder _prompts = new();
    private readonly ConcurrentQueue<JsonObject> _calls = new();
    private string _antwort = "Hallo Welt.";
    private string _doneReason = "stop";

    public ValueTask InitializeAsync()
    {
        _server.Map("/api/chat", r =>
        {
            _calls.Enqueue(JsonNode.Parse(r.Text)!.AsObject());
            var body = new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = _antwort },
                ["done"] = true,
                ["done_reason"] = _doneReason,
            };
            return new FakeResponse(200, body.ToJsonString());
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private Task<string> Refine(
        string raw = "also ähm hallo welt",
        RefineMode mode = RefineMode.Polish,
        bool paragraphs = true,
        IRefinePromptBuilder? prompts = null)
    {
        var access = AccessResolver.ResolveLlm(AccessResolver.ResolveStt("openai", "", "", ""), "ollama", _server.Base, "", "gemma4:e2b");
        return new TextRefiner(access, _http, prompts ?? _prompts, new TextCleanup([]))
            .RefineAsync(raw, "de", new Refinement(mode, Paragraphs: paragraphs), TestContext.Current.CancellationToken);
    }

    private (string System, string User) Messages(int i = 0)
    {
        var messages = _calls.ToArray()[i]["messages"]!.AsArray();
        return ((string)messages[0]!["content"]!, (string)messages[1]!["content"]!);
    }

    [Fact]
    public async Task DerselbeAuftragWieOnline()
    {
        Assert.Equal("Hallo Welt.", await Refine(paragraphs: false));
        var (system, user) = Messages();
        Assert.Equal(_prompts.Build(RefineMode.Polish, german: true, smartFillers: false, paragraphs: false, isShort: true), system);
        Assert.Equal("<diktat>\nalso ähm hallo welt\n</diktat>", user);
        Assert.Equal(
            TextRefiner.Messages(_prompts, "also ähm hallo welt", "de", new Refinement(RefineMode.Polish, Paragraphs: false)),
            (system, user));
    }

    [Fact]
    public async Task DerselbeAuftragWieOnlineMitEchtemPrompt()
    {
        Assert.Equal("Hallo Welt.", await Refine(paragraphs: false, prompts: CoreText.Prompts));
        var (system, user) = Messages();
        Assert.Equal(CoreText.Prompts.Build(RefineMode.Polish, german: true, smartFillers: false, paragraphs: false, isShort: true), system);
        Assert.Contains("einen einzigen durchgehenden Absatz", system, StringComparison.Ordinal);
        Assert.Equal("<diktat>\nalso ähm hallo welt\n</diktat>", user);
    }

    [Fact]
    public async Task DieselbeNacharbeitWieOnline()
    {
        _antwort = "Hier ist der geglättete Text:\n<diktat>\nAlso, hallo Welt.\n</diktat>";
        Assert.Equal("Also, hallo Welt.", await Refine());
    }

    [Fact]
    public async Task PromptStufeNimmtDieNacharbeitDerPromptStufe()
    {
        _antwort = "Hier ist dein Prompt:\nErstelle mir eine Einkaufsliste.";
        Assert.Equal("Erstelle mir eine Einkaufsliste.", await Refine(mode: RefineMode.Prompt));
        Assert.Contains("mode=prompt", Messages().System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptStufeSchicktDenPromptDerPromptStufe()
    {
        _antwort = "Hier ist dein Prompt:\nErstelle mir eine Einkaufsliste.";
        Assert.Equal("Erstelle mir eine Einkaufsliste.", await Refine(mode: RefineMode.Prompt, prompts: CoreText.Prompts));
        Assert.Contains("Beantworte keine Frage daraus", Messages().System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EineErfuellteBitteWirdAbgelehnt()
    {
        _antwort = string.Join(" ", Enumerable.Repeat("Ihr seid alle herzlich zu meinem Geburtstag eingeladen.", 10));
        var e = await Assert.ThrowsAsync<RefineRejectedException>(() => Refine());
        Assert.Contains("statt den Text zu bearbeiten", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeereAntwortGiltAlsGescheitert()
    {
        _antwort = "  ";
        var e = await Assert.ThrowsAsync<RefineRejectedException>(() => Refine());
        Assert.Equal(TextRefiner.MsgEmpty, e.Message);
    }

    /// <summary>
    /// Kotlin: zu langes Diktat passt nicht in die KV-Tabelle von LiteRT → MSG_TOO_LONG, ohne Rechnung. Ollama kennt kein
    /// Vorab-Limit im Client; schneidet es an seiner Grenze ab (<c>done_reason: length</c>), kommt nur der Anfang zurück —
    /// dann wie online <see cref="TextRefiner.MsgTruncated"/> statt eines halben Texts.
    /// </summary>
    [Fact]
    public async Task ZuLangesDiktatKommtNichtAbgeschnittenDurch()
    {
        _doneReason = "length";
        _antwort = "Termin termin";
        var e = await Assert.ThrowsAsync<RefineRejectedException>(() => Refine(string.Join(" ", Enumerable.Repeat("termin", 1_000))));
        Assert.Equal(TextRefiner.MsgTruncated, e.Message);
    }

    [Fact]
    public async Task EinMittellangesDiktatWirdLokalVerbessert()
    {
        _antwort = string.Join(" ", Enumerable.Repeat("Termin", 500));
        Assert.Equal(_antwort, await Refine(string.Join(" ", Enumerable.Repeat("termin", 500))));
    }

    [Fact]
    public async Task AusOderLeerRechnetGarNicht()
    {
        Assert.Equal("roh", await Refine("roh", RefineMode.Off));
        Assert.Equal(" ", await Refine(" "));
        Assert.Empty(_calls);
    }
}
