using System.Runtime.ExceptionServices;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Pipeline;

/// <summary>Erkennung über den Zugang eines Anbieters (Port von <c>OnlineBackend</c>, <c>TranscriptionBackend.kt</c>).</summary>
/// <param name="prompt">Vokabular für die Erkennung.</param>
public sealed class OnlineBackend(HttpClient http, ApiAccess access, string prompt = "") : ITranscriptionBackend
{
    public string Label => access.Provider.Name;

    public Task<TranscriptResult> TranscribeAsync(AudioBuffer audio, string language, CancellationToken ct) =>
        new ApiTranscriber(http, access, prompt).TranscribeAsync(WavUpload.FromBuffer(audio), language, ct);
}

/// <summary>
/// Fallback der Erkennung (Port von <c>FallbackBackend</c>, Android 3.9.1): scheitert <paramref name="primary"/>, erkennt
/// <paramref name="fallback"/> dieselbe Aufnahme; danach bleibt es für diesen Auftrag beim Fallback. Scheitern beide, geht
/// der Fehler des ersten raus, der des Fallbacks hängt daran (<see cref="Suppressed"/>). Nach Abbruch kein zweiter Upload.
/// </summary>
public sealed class FallbackBackend(ITranscriptionBackend primary, ITranscriptionBackend fallback) : ITranscriptionBackend
{
    private const string SuppressedKey = "WhisperLoom.FallbackError";

    private volatile bool _primaryFailed;
    private volatile bool _usedFallback;

    /// <summary>Der erste ist in diesem Auftrag gescheitert, der Fallback hat (mindestens einmal) erkannt.</summary>
    public bool UsedFallback => _usedFallback;

    /// <summary>Auftrag hinfällig (abgelöst oder verworfen) — dann kein zweiter Upload.</summary>
    public Func<bool> Cancelled { get; set; } = () => false;

    /// <summary>Womit zuletzt erkannt wurde — nach einem Ausfall der Fallback.</summary>
    public string Label => _usedFallback ? fallback.Label : primary.Label;

    /// <summary>Name des Fallback-Anbieters für den Hinweis.</summary>
    public string FallbackLabel => fallback.Label;

    /// <summary>Der Fehler des Fallbacks, wenn beide gescheitert sind (Kotlin: <c>suppressed</c>).</summary>
    public static Exception? Suppressed(Exception e) => e.Data[SuppressedKey] as Exception;

    public async Task<TranscriptResult> TranscribeAsync(AudioBuffer audio, string language, CancellationToken ct)
    {
        if (_primaryFailed) return await FallbackAsync(audio, language, ct).ConfigureAwait(false);
        Exception error;
        try
        {
            return await primary.TranscribeAsync(audio, language, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && !Cancelled())
        {
            error = e;
        }
        _primaryFailed = true;
        try
        {
            return await FallbackAsync(audio, language, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            error.Data[SuppressedKey] = e;
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
    }

    private async Task<TranscriptResult> FallbackAsync(AudioBuffer audio, string language, CancellationToken ct)
    {
        var result = await fallback.TranscribeAsync(audio, language, ct).ConfigureAwait(false);
        _usedFallback = true;
        return result;
    }
}
