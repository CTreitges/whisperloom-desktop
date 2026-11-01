using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace WhisperLoom.App.Management;

/// <summary>Antwort von <c>POST /api/v1/enroll</c>. Das Token kommt nur einmal; <see cref="ToString"/> ohne Token.</summary>
public sealed record EnrollResult(string DeviceId, string DeviceToken, string State, string TenantName, int PollAfterSec)
{
    public override string ToString() => $"EnrollResult(deviceId={DeviceId}, state={State}, tenant={TenantName})";
}

/// <summary>Antwort von <c>GET /api/v1/device</c>.</summary>
public sealed record DeviceInfo(string DeviceId, string State, string TenantName, bool RotateToken, int PollAfterSec);

/// <summary>Antwort von <c>GET /api/v1/config</c>: <see cref="NotModified"/> bei 304, sonst Körper und ETag.</summary>
public sealed record ConfigResponse(bool NotModified, string? ETag, JsonObject? Body);

/// <summary>Der Server antwortete mit Fehler (<c>docs/server-api.md</c> §1.2). <see cref="Error"/> = Code, z. B. <c>device_blocked</c>.</summary>
public sealed class ManagementException(int status, string error, string message) : Exception(message)
{
    public int Status { get; } = status;

    public string Error { get; } = error;
}

/// <summary>Server nicht erreichbar (DNS, TLS, Zeitlimit): die App behält die letzte Konfiguration.</summary>
public sealed class ManagementOfflineException(Exception inner) : Exception("Firmen-Server nicht erreichbar", inner);

/// <summary>
/// Client der Server-API v1 (<c>docs/server-api.md</c>): Enrollment, Gerätezustand, Konfiguration mit ETag, Token-Rotation,
/// Abmelden. Ohne Zustand; Token und Adresse übergibt der Aufrufer. Meldungen nennen nie Token oder Code.
/// </summary>
public sealed class ManagementApi(HttpClient http, string platform, string appVersion)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public string UserAgent => $"WhisperLoom/{appVersion} ({platform})";

    public async Task<EnrollResult> EnrollAsync(string serverUrl, string code, string deviceName, CancellationToken ct)
    {
        var body = new JsonObject { ["code"] = code, ["platform"] = platform, ["appVersion"] = Clip(appVersion, 20) };
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            body["deviceName"] = Clip(deviceName.Trim(), 64);
        }
        var (_, _, json) = await SendAsync(HttpMethod.Post, serverUrl, "/enroll", null, body, null, ct).ConfigureAwait(false);
        return new EnrollResult(Text(json, "deviceId"), Text(json, "deviceToken"), Text(json, "state"), Text(json, "tenantName"),
            Number(json, "pollAfterSec", 10));
    }

    public async Task<DeviceInfo> DeviceAsync(string serverUrl, string token, CancellationToken ct)
    {
        var (_, _, json) = await SendAsync(HttpMethod.Get, serverUrl, "/device", token, null, null, ct).ConfigureAwait(false);
        return new DeviceInfo(Text(json, "deviceId"), Text(json, "state"), Text(json, "tenantName"),
            json?["rotateToken"]?.GetValueKind() == System.Text.Json.JsonValueKind.True, Number(json, "pollAfterSec", 10));
    }

    public async Task<ConfigResponse> ConfigAsync(string serverUrl, string token, string? etag, CancellationToken ct)
    {
        var (status, headers, json) = await SendAsync(HttpMethod.Get, serverUrl, "/config", token, null, etag, ct).ConfigureAwait(false);
        return status == HttpStatusCode.NotModified
            ? new ConfigResponse(true, etag, null)
            : new ConfigResponse(false, headers?.ETag?.Tag, json);
    }

    public async Task<string> RotateTokenAsync(string serverUrl, string token, CancellationToken ct)
    {
        var (_, _, json) = await SendAsync(HttpMethod.Post, serverUrl, "/device/token", token, new JsonObject(), null, ct).ConfigureAwait(false);
        var fresh = Text(json, "deviceToken");
        return fresh.Length > 0 ? fresh : throw new ManagementException(500, "internal_error", "Antwort ohne deviceToken");
    }

    public Task LeaveAsync(string serverUrl, string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, serverUrl, "/device/leave", token, new JsonObject(), null, ct);

    private async Task<(HttpStatusCode, HttpResponseHeaders?, JsonObject?)> SendAsync(HttpMethod method, string serverUrl, string route,
        string? token, JsonObject? body, string? etag, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, serverUrl.TrimEnd('/') + "/api/v1" + route);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(Timeout);
        HttpResponseMessage response;
        string text;
        try
        {
            response = await http.SendAsync(request, timer.Token).ConfigureAwait(false);
            text = await response.Content.ReadAsStringAsync(timer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            throw new ManagementOfflineException(e);
        }
        using (response)
        {
            var status = response.StatusCode;
            var json = Parse(text);
            if (status == HttpStatusCode.NotModified || ((int)status is >= 200 and <= 299))
            {
                return (status, response.Headers, json);
            }
            var error = Text(json, "error");
            throw new ManagementException((int)status, error.Length > 0 ? error : $"http_{(int)status}",
                Clip(Text(json, "message"), 200));
        }
    }

    private static JsonObject? Parse(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonObject? json, string name) =>
        json?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static int Number(JsonObject? json, string name, int fallback) =>
        json?[name] is JsonValue v && v.TryGetValue<int>(out var n) && n > 0 ? n : fallback;

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
