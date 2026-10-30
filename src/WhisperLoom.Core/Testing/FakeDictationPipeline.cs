using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Testing;

/// <summary>Welcher Fehler der <see cref="FakeDictationPipeline"/> beim nächsten Auftrag auftritt.</summary>
public enum FakeFailure
{
    None,

    /// <summary>Erkennung scheitert: <see cref="ApiHttpException"/> mit Status 500.</summary>
    SttHttp,

    /// <summary>Erkennung nicht erreichbar: <see cref="ApiNetworkException"/>.</summary>
    SttNetwork,

    /// <summary>Erkennung nicht eingerichtet: <see cref="ApiNotConfiguredException"/>.</summary>
    SttNotConfigured,

    /// <summary>Verbesserung scheitert: Ergebnis = Rohtext, <see cref="RefinedText.Skipped"/> gesetzt (kein Wurf).</summary>
    Refine,
}

/// <summary>
/// Pipeline ohne Netz für App-Shell, UI-Tests und Smokes, solange der Core-Port (Phase 1) fehlt.
/// Rohtext = <see cref="RawText"/>; Ergebnis bei Stufe aus = Rohtext, sonst <c>"[&lt;stufe&gt;] &lt;roh&gt;"</c>.
/// Fehler und Verzögerung sind steuerbar; <see cref="CancellationToken"/> wird beachtet.
/// </summary>
public sealed class FakeDictationPipeline : IDictationPipeline
{
    public const string ModelLabel = "Fake-LLM";
    public const string SttLabel = "Fake-STT";

    /// <summary>Was die Fake-Erkennung „hört“.</summary>
    public string RawText { get; set; } = "Das ist ein Testdiktat.";

    /// <summary>Fehler für alle folgenden Aufträge, bis wieder <see cref="FakeFailure.None"/> gesetzt wird.</summary>
    public FakeFailure Failure { get; set; }

    /// <summary>Künstliche Verarbeitungszeit (für Lade-Anzeigen und Abbruch-Tests).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Anzahl der Aufrufe von <see cref="RunAsync"/>.</summary>
    public int Runs { get; private set; }

    public async Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Runs++;
        await Task.Delay(Delay, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        switch (Failure)
        {
            case FakeFailure.SttHttp:
                throw new ApiHttpException(500, "Fake-Erkennung: HTTP 500");
            case FakeFailure.SttNetwork:
                throw new ApiNetworkException("Fake-Erkennung: nicht erreichbar");
            case FakeFailure.SttNotConfigured:
                throw new ApiNotConfiguredException();
        }

        var language = request.Language == "auto" ? "de" : request.Language;
        var result = Refine(RawText, request.Refinement);
        return new DictationResult(RawText, language, request.Audio.DurationMs, result, SttLabel);
    }

    public async Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(refinement);
        await Task.Delay(Delay, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return Refine(raw, refinement);
    }

    private RefinedText Refine(string raw, Refinement refinement)
    {
        if (refinement.Mode == RefineMode.Off || raw.Length == 0)
        {
            return new RefinedText(refinement, raw);
        }
        if (Failure == FakeFailure.Refine)
        {
            return new RefinedText(refinement, raw, Skipped: "Fake-Verbesserung gescheitert");
        }
        return new RefinedText(refinement, $"[{refinement.Mode.Key()}] {raw}", Model: ModelLabel);
    }
}
