using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>Optionen eines Aufrufs (Port der Parameter von <c>Http.post/get</c>).</summary>
/// <param name="ReadTimeoutMs">Wartezeit auf die Antwort nach dem Verbindungsaufbau (eigener Server: 600 s).</param>
/// <param name="FollowRedirects">false lässt Key und Header nicht auf einen anderen Host wandern.</param>
/// <param name="AuthHeader">Eigener Key-Header ohne „Bearer “ (ElevenLabs: xi-api-key); null = Authorization: Bearer.</param>
/// <param name="Headers">Weitere Header (Anthropic: anthropic-version).</param>
/// <param name="ConnectTimeoutMs">Kürzer, wenn bei Fehlschlag ein Ersatz bereitsteht.</param>
/// <param name="Style">Protokoll für die Hinweise der Fehlermeldung.</param>
/// <param name="Hints">Eigener Hinweis je Status statt <see cref="ApiErrors.Hint"/> (Modell-Liste von ElevenLabs).</param>
public sealed record HttpCallOptions(
    int ReadTimeoutMs = Http.DefaultReadTimeoutMs,
    bool FollowRedirects = true,
    string? AuthHeader = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    int ConnectTimeoutMs = Http.ConnectTimeoutMs,
    ApiStyle Style = ApiStyle.OpenAi,
    IReadOnlyDictionary<int, string>? Hints = null);

public enum ProxyMode
{
    /// <summary>System-Proxy (Standard; Windows: Interneteinstellungen, Linux: http(s)_proxy).</summary>
    System,

    /// <summary>Eigene Proxy-Adresse.</summary>
    Manual,

    /// <summary>Direkt, ohne Proxy.</summary>
    None,
}

/// <summary>Proxy des gemeinsamen Handlers. Anmeldung mit dem Konto des Nutzers (DefaultCredentials) oder Name/Passwort.</summary>
public sealed record HttpProxyOptions(
    ProxyMode Mode = ProxyMode.System,
    string Address = "",
    bool UseDefaultCredentials = true,
    string UserName = "",
    string Password = "")
{
    /// <summary>Ohne Passwort.</summary>
    public override string ToString() => $"HttpProxyOptions(mode={Mode}, address={Address}, defaultCredentials={UseDefaultCredentials})";
}

/// <summary>
/// HTTP-Helfer für alle Aufrufe (Port von <c>api/Http.kt</c>): ein gemeinsamer <see cref="SocketsHttpHandler"/> mit
/// Proxy, Verbindungs-Zeitlimit je Aufruf und eigener Weiterleitungs-Regel. Fehlerstatus → <see cref="ApiHttpException"/>,
/// Netz/Zeitlimit → <see cref="ApiNetworkException"/>, Abbruch über den Token bleibt <see cref="OperationCanceledException"/>.
/// </summary>
public static class Http
{
    public const int ConnectTimeoutMs = 15_000;
    public const int DefaultReadTimeoutMs = 90_000;
    private const int MaxRedirects = 5;

    private static readonly HttpRequestOptionsKey<int> ConnectTimeoutKey = new("WhisperLoom.ConnectTimeoutMs");
    private static readonly HttpRequestOptionsKey<ReadTimer> ReadTimerKey = new("WhisperLoom.ReadTimer");

    /// <summary>Eigene Kennung statt des .NET-Standards (ollama.com sperrte auf Android die Dalvik-Kennung).</summary>
    public static string UserAgent { get; } = $"WhisperLoom/{typeof(Http).Assembly.GetName().Version?.ToString(3) ?? "0"} (Desktop)";

    /// <summary>
    /// Der gemeinsame Handler. Weiterleitungen folgt <see cref="SendAsync"/> selbst (nur GET, Key nur zum selben Host).
    /// </summary>
    // Doku: https://learn.microsoft.com/dotnet/api/system.net.http.socketshttphandler.connectcallback (abgerufen 2026-10-30)
    public static HttpMessageHandler CreateHandler(HttpProxyOptions? proxy = null)
    {
        proxy ??= new HttpProxyOptions();
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = ConnectAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseProxy = proxy.Mode != ProxyMode.None,
        };
        ICredentials? credentials = proxy.UseDefaultCredentials
            ? CredentialCache.DefaultCredentials
            : new NetworkCredential(proxy.UserName, proxy.Password);
        if (proxy.Mode == ProxyMode.Manual)
        {
            handler.Proxy = new WebProxy(proxy.Address) { Credentials = credentials };
        }
        else if (proxy.Mode == ProxyMode.System)
        {
            // Proxy = null heißt System-Proxy (HttpClient.DefaultProxy).
            handler.DefaultProxyCredentials = credentials;
        }
        return handler;
    }

    /// <summary>Client über einem neuen Handler, ohne eigenes Gesamt-Zeitlimit (die Zeitlimits setzt jeder Aufruf).</summary>
    public static HttpClient CreateClient(HttpProxyOptions? proxy = null) =>
        new(CreateHandler(proxy), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

    public static string Endpoint(string baseUrl, string path) => baseUrl.Trim().TrimEnd('/') + path;

    /// <param name="apiKey">Leer = kein Key-Header (eigene Server brauchen keinen).</param>
    public static Task<string> PostAsync(
        HttpClient http,
        string url,
        string apiKey,
        byte[] body,
        string contentType,
        HttpCallOptions? options,
        CancellationToken ct) =>
        SendAsync(http, HttpMethod.Post, url, apiKey, () =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            return content;
        }, options ?? new HttpCallOptions(), ct);

    /// <summary>GET ohne Body (Modell-Listen). Parameter und Fehler wie <see cref="PostAsync"/>.</summary>
    public static Task<string> GetAsync(HttpClient http, string url, string apiKey, HttpCallOptions? options, CancellationToken ct) =>
        SendAsync(http, HttpMethod.Get, url, apiKey, () => null, options ?? new HttpCallOptions(), ct);

    private static async Task<string> SendAsync(
        HttpClient http,
        HttpMethod method,
        string url,
        string apiKey,
        Func<HttpContent?> content,
        HttpCallOptions o,
        CancellationToken ct)
    {
        Uri uri;
        try
        {
            uri = ParseUrl(url);
        }
        catch (UriFormatException e)
        {
            throw ApiErrors.Network(e);
        }
        var origin = uri;
        using var timer = new ReadTimer(o.ReadTimeoutMs, ct);
        timer.Start(o.ConnectTimeoutMs + o.ReadTimeoutMs);
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(method, uri) { Content = content() };
                request.Options.Set(ConnectTimeoutKey, o.ConnectTimeoutMs);
                request.Options.Set(ReadTimerKey, timer);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                // Key und eigene Header nur zum ursprünglichen Host.
                if (SameOrigin(uri, origin))
                {
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        request.Headers.TryAddWithoutValidation(o.AuthHeader ?? "Authorization", o.AuthHeader is null ? $"Bearer {apiKey}" : apiKey);
                    }
                    foreach (var (name, value) in o.Headers ?? new Dictionary<string, string>())
                    {
                        request.Headers.TryAddWithoutValidation(name, value);
                    }
                }
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timer.Token).ConfigureAwait(false);
                var code = (int)response.StatusCode;
                if (o.FollowRedirects && method == HttpMethod.Get && code is >= 300 and < 400 &&
                    response.Headers.Location is { } location && redirects < MaxRedirects)
                {
                    uri = new Uri(uri, location);
                    continue;
                }
                var body = await response.Content.ReadAsStringAsync(timer.Token).ConfigureAwait(false);
                if (code is < 200 or > 299)
                {
                    throw o.Hints is not null && o.Hints.TryGetValue(code, out var hint)
                        ? ApiErrors.HttpWithHint(code, ErrorDetail(body), hint)
                        : ApiErrors.Http(code, ErrorDetail(body), o.Style);
                }
                return body;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException e)
        {
            // Zeitlimit des Aufrufs, nicht der Token: als Netzfehler, damit der Fallback greift (Vertrag „Threading“).
            throw ApiErrors.Network(new TimeoutException("Read timed out", e));
        }
        catch (HttpRequestException e)
        {
            throw ApiErrors.Network(e);
        }
        catch (IOException e)
        {
            throw ApiErrors.Network(e);
        }
    }

    private static Uri ParseUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new UriFormatException("Keine http(s)-URL");
        }
        return uri;
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        a.Scheme == b.Scheme && a.Port == b.Port && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>Baut die Verbindung mit dem Zeitlimit des Aufrufs auf; danach läuft die Lese-Frist neu an.</summary>
    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var options = context.InitialRequestMessage.Options;
        var connectMs = options.TryGetValue(ConnectTimeoutKey, out var ms) ? ms : ConnectTimeoutMs;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(connectMs);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(context.DnsEndPoint, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException("connect timed out");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
        if (options.TryGetValue(ReadTimerKey, out var timer)) timer.Restart();
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>
    /// Lesbare Meldung aus einer Fehlerantwort statt rohem JSON: OpenAI <c>{"error":{"message":…}}</c>, Ollama
    /// <c>{"error":"…"}</c>, ElevenLabs <c>{"detail":{"message":…}}</c> / <c>{"detail":[{"msg":…}]}</c> / <c>{"detail":"…"}</c>.
    /// </summary>
    internal static string ErrorDetail(string body)
    {
        var fallback = body.Length > 200 ? body[..200] : body;
        if (string.IsNullOrWhiteSpace(body)) return "keine Antwort";
        try
        {
            if (JsonNode.Parse(body) is not JsonObject json) return fallback;
            return NonBlank(json["error"] is JsonObject e ? Text(e["message"]) : null)
                ?? NonBlank(Text(json["error"]))
                ?? DetailMessage(json["detail"])
                ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static string? DetailMessage(JsonNode? detail) => NonBlank(detail switch
    {
        JsonObject o => Text(o["message"]),
        JsonArray a => string.Join("; ", a.OfType<JsonObject>().Select(m => Text(m["msg"])).Where(m => !string.IsNullOrWhiteSpace(m))),
        _ => Text(detail),
    });

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string? NonBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Frist eines Aufrufs: erst Verbindung + Lesen, nach dem Verbindungsaufbau nur noch die Lese-Frist.</summary>
    private sealed class ReadTimer(int readTimeoutMs, CancellationToken ct) : IDisposable
    {
        private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        public CancellationToken Token => _cts.Token;

        public void Start(int ms) => _cts.CancelAfter(ms);

        public void Restart()
        {
            try
            {
                _cts.CancelAfter(readTimeoutMs);
            }
            catch (ObjectDisposedException)
            {
                // Aufruf schon fertig (die Verbindung bleibt im Pool).
            }
        }

        public void Dispose() => _cts.Dispose();
    }
}
