using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Erkennung per HTTP (Port von <c>api/ApiTranscriber.kt</c>): OpenAI-kompatibel POST /audio/transcriptions mit WAV,
/// ElevenLabs mit rohem PCM, <c>xi-api-key</c> und eigenen Feldern. Multipart von Hand, damit die Teile genau wie auf
/// Android aussehen (ohne Content-Type je Textfeld).
/// </summary>
/// <param name="prompt">Vokabular für die Erkennung (Namen, Fachbegriffe); ElevenLabs bekommt es als keyterms.</param>
public sealed class ApiTranscriber(HttpClient http, ApiAccess access, string prompt = "")
{
    /// <exception cref="ApiNotConfiguredException">Der Anbieter braucht einen Key und es ist keiner da.</exception>
    public Task<TranscriptResult> TranscribeAsync(WavUpload upload, string language, CancellationToken ct)
    {
        if (access.Provider.NeedsKey && string.IsNullOrWhiteSpace(access.ApiKey)) throw new ApiNotConfiguredException();
        return access.Provider.Api == ApiStyle.ElevenLabs ? ElevenLabsAsync(upload, language, ct) : OpenAiAsync(upload, language, ct);
    }

    private async Task<TranscriptResult> OpenAiAsync(WavUpload upload, string language, CancellationToken ct)
    {
        var body = await MultipartAsync(TranscriptionRequest.Url(access), TranscriptionRequest.Fields(access, language, prompt),
            "audio.wav", "audio/wav", upload.WavBytes(), new HttpCallOptions(access.ReadTimeoutMs), ct).ConfigureAwait(false);
        var json = Parse(body);
        // Nur verbose_json bzw. eigene Server liefern die erkannte Sprache.
        return new TranscriptResult(Text(json, "text").Trim(), NonBlank(Text(json, "language")));
    }

    private async Task<TranscriptResult> ElevenLabsAsync(WavUpload upload, string language, CancellationToken ct)
    {
        // Eigener Key-Header: keiner Weiterleitung folgen; Fehler mit den Hinweisen für ElevenLabs.
        var options = new HttpCallOptions(access.ReadTimeoutMs, FollowRedirects: false, AuthHeader: ElevenLabsStt.AuthHeader, Style: ApiStyle.ElevenLabs);
        var body = await MultipartAsync(ElevenLabsStt.Url(access), ElevenLabsStt.Fields(access, language, prompt),
            ElevenLabsStt.FileName, ElevenLabsStt.FileType, upload.PcmBytes(), options, ct).ConfigureAwait(false);
        var json = Parse(body);
        var code = NonBlank(Text(json, "language_code"));
        return new TranscriptResult(Text(json, "text").Trim(), code is null ? null : ElevenLabsStt.IsoLanguage(code));
    }

    /// <summary>multipart/form-data: erst die Textfelder, zuletzt die Datei.</summary>
    private Task<string> MultipartAsync(
        string url,
        IReadOnlyList<(string Name, string Value)> fields,
        string fileName,
        string fileType,
        byte[] file,
        HttpCallOptions options,
        CancellationToken ct)
    {
        var boundary = $"----whisperloom{Guid.NewGuid():N}";
        using var stream = new MemoryStream(file.Length + 1024);
        void Write(string s) => stream.Write(Encoding.UTF8.GetBytes(s));
        foreach (var (name, value) in fields)
        {
            Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n");
        }
        Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\n");
        Write($"Content-Type: {fileType}\r\n\r\n");
        stream.Write(file);
        Write($"\r\n--{boundary}--\r\n");
        return Http.PostAsync(http, url, access.ApiKey, stream.ToArray(), $"multipart/form-data; boundary={boundary}", options, ct);
    }

    /// <summary>Keine JSON-Antwort ist ein Fehler des Servers, kein leeres Diktat.</summary>
    private static JsonObject Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject ?? throw new JsonException("keine JSON-Objekt-Antwort");
        }
        catch (JsonException e)
        {
            throw ApiErrors.Network(new IOException("Antwort des Servers ist kein JSON", e));
        }
    }

    private static string Text(JsonObject json, string key) =>
        json[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static string? NonBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
