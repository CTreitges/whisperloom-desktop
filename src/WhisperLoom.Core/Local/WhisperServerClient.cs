using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Local;

/// <summary>
/// HTTP-Teil des whisper.cpp-Begleitprozesses (<c>whisper-server</c> auf 127.0.0.1): <c>GET /health</c> und
/// <c>POST /inference</c>. Parameter wie Android (<c>WhisperContext.transcribe</c>): Beam 5 bei „genau“, sonst Greedy,
/// Zeitstempel erst über einem 30-s-Fenster, unter 1 s mit Stille aufgefüllt. Das Audio geht nur in den Speicher des
/// Begleitprozesses, nie auf die Platte.
/// </summary>
// Doku: https://github.com/ggml-org/whisper.cpp/blob/master/examples/server/README.md (abgerufen 2026-10-31)
public static class WhisperServerClient
{
    /// <summary>Beam-Breite für „genau“ (whisper-cli-Standard, Android <c>BEAM_SIZE</c>).</summary>
    public const int BeamSize = 5;

    /// <summary>whisper.cpp erkennt in Fenstern von 30 s.</summary>
    public const int WindowSamples = 30 * AudioBuffer.DefaultSampleRate;

    /// <summary>whisper warnt unter 100 ms und rechnet ohnehin ein 30-s-Fenster: auffüllen auf 1 s.</summary>
    public const int MinSamples = AudioBuffer.DefaultSampleRate;

    /// <summary>true = der Server antwortet auf /health mit 200 (Modell geladen). Wirft nur bei Abbruch.</summary>
    public static async Task<bool> HealthAsync(HttpClient http, string baseUrl, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(2_000);
            using var response = await http.GetAsync(baseUrl.TrimEnd('/') + "/health", cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <param name="language">ISO-Code oder "auto" (dann meldet der Server die erkannte Sprache).</param>
    /// <param name="prompt">Kontext-Wörter als Anfangs-Prompt; "" = keiner.</param>
    /// <exception cref="ApiNetworkException">Server nicht erreicht oder Zeitlimit.</exception>
    /// <exception cref="ApiHttpException">Fehlerstatus des Servers.</exception>
    public static async Task<TranscriptResult> TranscribeAsync(
        HttpClient http, string baseUrl, AudioBuffer audio, string language, string prompt, bool accurate, CancellationToken ct)
    {
        using var form = Form(audio, language, prompt, accurate);
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(baseUrl.TrimEnd('/') + "/inference", form, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new ApiNetworkException("Lokaler Erkennungsdienst nicht erreichbar", e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new ApiNetworkException("Lokaler Erkennungsdienst antwortet nicht", e);
        }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiHttpException((int)response.StatusCode, $"Lokaler Erkennungsdienst: HTTP {(int)response.StatusCode}");
            }
            return Parse(body, language);
        }
    }

    /// <summary>Formular für /inference (alle Parameter je Anfrage, der Server merkt sie sich sonst).</summary>
    public static MultipartFormDataContent Form(AudioBuffer audio, string language, string prompt, bool accurate)
    {
        var samples = audio.Samples;
        if (samples.Length < MinSamples)
        {
            var padded = new short[MinSamples];
            samples.Span.CopyTo(padded);
            samples = padded;
        }
        var wav = new ByteArrayContent(new WavUpload(samples, audio.SampleRate).WavBytes());
        wav.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        var form = new MultipartFormDataContent
        {
            { wav, "file", "audio.wav" },
            { new StringContent("verbose_json"), "response_format" },
            { new StringContent("0.0"), "temperature" },
            { new StringContent(string.IsNullOrWhiteSpace(language) ? "auto" : language), "language" },
            { new StringContent((accurate ? BeamSize : 1).ToString(CultureInfo.InvariantCulture)), "beam_size" },
            { new StringContent(samples.Length > WindowSamples ? "false" : "true"), "no_timestamps" },
        };
        if (!string.IsNullOrWhiteSpace(prompt)) form.Add(new StringContent(prompt), "prompt");
        return form;
    }

    /// <summary>Text (getrimmt) und bei "auto" die erkannte Sprache aus der verbose_json-Antwort.</summary>
    public static TranscriptResult Parse(string body, string language)
    {
        var json = JsonNode.Parse(body) as JsonObject ?? throw new ApiHttpException(200, "Lokaler Erkennungsdienst: unerwartete Antwort");
        if (json["error"] is JsonValue error) throw new ApiHttpException(500, $"Lokaler Erkennungsdienst: {error}");
        var text = json["text"] is JsonValue t && t.TryGetValue<string>(out var s) ? s.Trim() : "";
        string? detected = null;
        if (language == "auto" && json["language"] is JsonValue l && l.TryGetValue<string>(out var lang) && !string.IsNullOrWhiteSpace(lang))
        {
            detected = lang.Length > 2 ? Iso(lang) : lang;
        }
        return new TranscriptResult(text, detected);
    }

    /// <summary>whisper meldet die Sprache als Namen ("english") oder Code ("en").</summary>
    private static string Iso(string name) => name.ToLowerInvariant() switch
    {
        "german" => "de",
        "english" => "en",
        "french" => "fr",
        "spanish" => "es",
        "italian" => "it",
        "dutch" => "nl",
        "polish" => "pl",
        "portuguese" => "pt",
        "turkish" => "tr",
        var other => other,
    };
}
