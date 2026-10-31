using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace WhisperLoom.Core.Local;

/// <summary>
/// Download fehlgeschlagen (Port von <c>DownloadException</c>). <see cref="Retryable"/>: ein neuer Versuch setzt an der
/// Teildatei an (Netz-/Serverfehler); sonst wurde die Teildatei verworfen oder der Nutzer muss erst etwas tun (Platz).
/// </summary>
public sealed class DownloadException(string message, bool retryable, DownloadException.Kinds kind = DownloadException.Kinds.Other, Exception? inner = null)
    : Exception(message, inner)
{
    public enum Kinds
    {
        Network,
        Checksum,
        Storage,
        Other,
    }

    public bool Retryable { get; } = retryable;

    public Kinds Kind { get; } = kind;
}

/// <summary>Fortschritt; <paramref name="bytesPerSec"/> = Rate seit dem letzten Aufruf (0 beim Abschluss).</summary>
public delegate void DownloadProgress(long bytes, long total, long bytesPerSec);

/// <summary>
/// Lädt eine Datei von ihrer <see cref="OfflineModel.Url"/> in den <see cref="ModelStore"/> (Port von <c>ModelDownloader.kt</c>):
/// <list type="bullet">
/// <item>Teildatei <c>&lt;datei&gt;.part</c>; Fortsetzung per <c>Range: bytes=n-</c> (206 + Content-Range), bei 200 von vorn.</item>
/// <item>SHA256 beim Schreiben; vorhandene Teilbytes werden vorher eingerechnet.</item>
/// <item>Fertig nur bei Größe == Katalog und Prüfsumme == Katalog, dann atomares Umbenennen.</item>
/// <item>Platzprüfung vorab, Netzfehler mit verdoppelndem Backoff wiederholt und fortgesetzt.</item>
/// </list>
/// </summary>
// Doku: https://learn.microsoft.com/dotnet/api/system.net.http.httpclient (abgerufen 2026-10-31),
// https://learn.microsoft.com/dotnet/api/system.security.cryptography.incrementalhash (abgerufen 2026-10-31)
public sealed class ModelDownloader(
    HttpClient http,
    int readTimeoutMs = 30_000,
    int retries = 3,
    int backoffMs = 2_000,
    int progressIntervalMs = 250,
    Func<string, long>? freeSpace = null)
{
    public const string MsgNet = "Netzwerkfehler beim Laden";
    public const string MsgChecksum = "Datei beschädigt — erneut laden";
    public const string MsgStorage = "Nicht genug Speicherplatz";
    public const string MsgRename = "Modell-Datei konnte nicht gespeichert werden";

    private readonly Func<string, long> _freeSpace = freeSpace ?? (dir => new ModelStore(dir).FreeBytes());

    public static string MsgSize(long bytes) => $"Server liefert eine andere Dateigröße ({bytes} Bytes) als erwartet";

    public static string MsgHttp(int status) => $"Server antwortet mit HTTP {status}";

    /// <summary>HttpClient für Downloads: Connect-Zeitlimit 15 s, keine Kompression, Weiterleitungen folgen.</summary>
    public static HttpClient CreateClient(HttpMessageHandler? handler = null) =>
        new(handler ?? new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), AutomaticDecompression = DecompressionMethods.None })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

    /// <returns>true = Datei vollständig und geprüft; false = über <paramref name="ct"/> abgebrochen, die Teildatei bleibt.</returns>
    /// <exception cref="DownloadException">Netz-, Server-, Speicher- oder Prüfsummenfehler.</exception>
    public async Task<bool> DownloadAsync(OfflineModel model, ModelStore store, DownloadProgress? onProgress = null, CancellationToken ct = default)
    {
        if (store.IsInstalled(model)) return true;
        if (!store.EnsureDir()) throw new DownloadException(MsgStorage, false, DownloadException.Kinds.Storage);
        var attempt = 0;
        while (true)
        {
            try
            {
                return await TryOnceAsync(model, store, onProgress ?? ((_, _, _) => { }), ct).ConfigureAwait(false);
            }
            catch (DownloadException e)
            {
                if (!e.Retryable && e.Kind != DownloadException.Kinds.Storage) File.Delete(store.PartFile(model));
                throw;
            }
            catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException)
            {
                if (ct.IsCancellationRequested) return false;
                if (IsOutOfSpace(e)) throw new DownloadException(MsgStorage, false, DownloadException.Kinds.Storage, e);
                if (attempt >= retries) throw new DownloadException(MsgNet, true, DownloadException.Kinds.Network, e);
                try
                {
                    await Task.Delay(backoffMs << attempt, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                attempt++;
            }
        }
    }

    private async Task<bool> TryOnceAsync(OfflineModel model, ModelStore store, DownloadProgress onProgress, CancellationToken ct)
    {
        var part = store.PartFile(model);
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0L;
        if (existing > model.Bytes)
        {
            File.Delete(part); // größer als das Ziel: kann nicht zu dieser Datei gehören
            existing = 0;
        }
        if (_freeSpace(store.Dir) < model.Bytes + model.ExtraDiskBytes - existing)
        {
            throw new DownloadException(MsgStorage, false, DownloadException.Kinds.Storage);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (existing > 0)
        {
            await using var input = File.OpenRead(part);
            await FeedAsync(input, hash, ct).ConfigureAwait(false);
        }
        if (existing < model.Bytes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
            using var response = await WithReadTimeout(t => http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, t), ct).ConfigureAwait(false);
            var resume = existing > 0;
            var code = (int)response.StatusCode;
            switch (code)
            {
                case 206:
                    var total = response.Content.Headers.ContentRange?.Length ?? -1;
                    if (total != model.Bytes) throw new DownloadException(MsgSize(total), false);
                    break;
                case 200:
                    var length = response.Content.Headers.ContentLength ?? -1;
                    if (length > 0 && length != model.Bytes) throw new DownloadException(MsgSize(length), false);
                    if (resume)
                    {
                        // Server kann kein Range → von vorn.
                        hash.GetHashAndReset();
                        existing = 0;
                        resume = false;
                    }
                    break;
                case 416:
                    File.Delete(part); // Teildatei passt nicht zum Server: Neustart über den Wiederholungsweg
                    throw new IOException("HTTP 416");
                case 429 or >= 500 and <= 599:
                    throw new IOException($"HTTP {code}");
                default:
                    throw new DownloadException(MsgHttp(code), false);
            }
            if (!await CopyAsync(response, part, resume, existing, model.Bytes, hash, onProgress, ct).ConfigureAwait(false)) return false;
        }
        Finish(part, store.File(model), model, hash);
        return true;
    }

    /// <returns>false = abgebrochen (Teildatei bleibt).</returns>
    private async Task<bool> CopyAsync(
        HttpResponseMessage response, string part, bool append, long startBytes, long total, IncrementalHash hash, DownloadProgress onProgress, CancellationToken ct)
    {
        var bytes = startBytes;
        var lastAt = Environment.TickCount64;
        var lastBytes = bytes;
        var buf = new byte[64 * 1024];
        await using (var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            while (true)
            {
                if (ct.IsCancellationRequested) return false;
                int n;
                try
                {
                    n = await WithReadTimeout(t => input.ReadAsync(buf, t).AsTask(), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return false;
                }
                if (n <= 0) break;
                if (bytes + n > total) throw new DownloadException(MsgSize(bytes + n), false);
                await output.WriteAsync(buf.AsMemory(0, n), CancellationToken.None).ConfigureAwait(false);
                hash.AppendData(buf, 0, n);
                bytes += n;
                var now = Environment.TickCount64;
                if (now - lastAt >= progressIntervalMs)
                {
                    onProgress(bytes, total, (bytes - lastBytes) * 1000 / Math.Max(1, now - lastAt));
                    lastAt = now;
                    lastBytes = bytes;
                }
            }
        }
        if (bytes < total) throw new IOException($"Verbindung endete vorzeitig ({bytes} von {total} Bytes)");
        onProgress(bytes, total, 0);
        return true;
    }

    private static void Finish(string part, string target, OfflineModel model, IncrementalHash hash)
    {
        var length = new FileInfo(part).Length;
        if (length != model.Bytes) throw new DownloadException(MsgSize(length), false);
        var hex = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!hex.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new DownloadException(MsgChecksum, false, DownloadException.Kinds.Checksum);
        }
        try
        {
            File.Move(part, target, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new DownloadException(MsgRename, false, DownloadException.Kinds.Storage, e);
        }
    }

    /// <summary>Hängt der Server, bricht das Lese-Zeitlimit die Anfrage ab (als <see cref="IOException"/>).</summary>
    private async Task<T> WithReadTimeout<T>(Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(readTimeoutMs);
        try
        {
            return await read(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new IOException($"Keine Daten seit {readTimeoutMs} ms", e);
        }
    }

    private static async Task FeedAsync(Stream input, IncrementalHash hash, CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        int n;
        while ((n = await input.ReadAsync(buf, ct).ConfigureAwait(false)) > 0) hash.AppendData(buf, 0, n);
    }

    private static bool IsOutOfSpace(Exception e) =>
        e.Message.Contains("ENOSPC", StringComparison.Ordinal) ||
        e.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase) ||
        e.HResult == unchecked((int)0x80070070); // ERROR_DISK_FULL

    /// <summary>"bytes 1000-189999/190000" → 190000; fehlt oder "*" → -1.</summary>
    public static long ContentRangeTotal(string? header)
    {
        var slash = header?.IndexOf('/') ?? -1;
        return slash >= 0 && long.TryParse(header![(slash + 1)..].Trim(), out var total) ? total : -1;
    }
}
