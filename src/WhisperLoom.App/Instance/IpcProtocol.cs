using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhisperLoom.App.Instance;

/// <summary>Ein Befehl an die laufende Instanz. Enthält nie Keys (die setzt <c>key set</c> im eigenen Prozess).</summary>
public sealed record IpcRequest(string Verb, IReadOnlyList<string> Args);

/// <summary>Antwort der Instanz. <see cref="Status"/> ist der Stand nach dem Befehl (wie <c>status --json</c>).</summary>
public sealed record IpcResponse(bool Ok, string Message, JsonObject? Status = null);

/// <summary>Eine JSON-Zeile je Nachricht, UTF-8. Anfrage → Antwort, dann schließt der Client.</summary>
internal static class IpcProtocol
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Obergrenze einer Zeile, damit ein fremder Client keinen Speicher füllt.</summary>
    public const int MaxLineChars = 64 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(message, Options) + "\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <returns>null bei leerem Strom oder ungültiger Nachricht.</returns>
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken ct) where T : class
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (buffer.Count < MaxLineChars)
        {
            var n = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (n == 0 || one[0] == (byte)'\n')
            {
                break;
            }
            buffer.Add(one[0]);
        }
        if (buffer.Count == 0)
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString([.. buffer]), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
