using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Zuletzt geladene Modell-Listen und die Merkliste der Textmodelle, die <c>temperature</c> abgelehnt haben (Port von
/// <c>ModelCache.kt</c>). Mit <paramref name="filePath"/> übersteht die Merkliste einen Neustart (JSON im Datenordner);
/// gespeichert werden nur Anbieter, Adresse und Modell — nie ein Key. Threadsicher.
/// </summary>
/// <param name="now">Uhr (Tests setzen sie).</param>
public sealed class ModelCache(string? filePath = null, Func<DateTimeOffset>? now = null) : IServerModelLookup
{
    /// <summary>Eine geladene Liste.</summary>
    public sealed record Entry(DateTimeOffset FetchedAt, IReadOnlyList<RemoteModel> Models);

    /// <summary>Älter als ein Tag = neu laden.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _lists = [];
    private readonly HashSet<string> _noTemperature = Load(filePath);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    public string? FilePath { get; } = filePath;

    public Entry? Get(string providerId, ModelKind kind, string baseUrl)
    {
        lock (_lock) return _lists.GetValueOrDefault(Key(providerId, kind, baseUrl));
    }

    /// <summary>Legt die Liste mit dem aktuellen Zeitpunkt ab (ersetzt die alte).</summary>
    public Entry Put(ApiAccess access, ModelKind kind, IReadOnlyList<RemoteModel> models)
    {
        var entry = new Entry(_now(), models);
        lock (_lock) _lists[Key(access.Provider.Id, kind, access.BaseUrl)] = entry;
        return entry;
    }

    /// <summary>Lädt die Liste vom Server und legt sie ab. Fehler wie <see cref="ModelLists.LoadAsync"/>; die alte bleibt dann.</summary>
    public async Task<Entry> RefreshAsync(HttpClient http, ApiAccess access, ModelKind kind, CancellationToken ct) =>
        Put(access, kind, await ModelLists.LoadAsync(http, access, kind, ct).ConfigureAwait(false));

    public RemoteModel? Find(string providerId, ModelKind kind, string baseUrl, string id) =>
        Get(providerId, kind, baseUrl)?.Models.FirstOrDefault(m => m.Id == id);

    public bool RejectsTemperature(string providerId, string baseUrl, string id)
    {
        lock (_lock) return _noTemperature.Contains(NoTemperatureKey(providerId, baseUrl, id));
    }

    /// <summary>
    /// Das Textmodell hat <c>temperature</c> abgelehnt und der zweite Versuch ohne kam durch: ab jetzt gleich ohne.
    /// Vergessen ist nie nötig — ohne temperature geht jedes Modell.
    /// </summary>
    public void RememberNoTemperature(ApiAccess access)
    {
        lock (_lock)
        {
            if (_noTemperature.Add(NoTemperatureKey(access.Provider.Id, access.BaseUrl, access.Model))) Save();
        }
    }

    /// <summary>Keine Liste, älter als <see cref="MaxAge"/> oder aus der Zukunft (Uhr verstellt): neu laden.</summary>
    public bool IsStale(Entry? entry)
    {
        if (entry is null) return true;
        var age = _now() - entry.FetchedAt;
        return age < TimeSpan.Zero || age > MaxAge;
    }

    /// <summary><c>&lt;providerId&gt;|&lt;stt|llm&gt;|&lt;baseUrl&gt;</c>, die Adresse ohne Leerraum und Schrägstrich am Ende.</summary>
    public static string Key(string providerId, ModelKind kind, string baseUrl) => $"{providerId}|{kind.Key()}|{baseUrl.Trim().TrimEnd('/')}";

    /// <summary><c>noTemp|&lt;providerId&gt;|&lt;baseUrl&gt;|&lt;Modell&gt;</c>, die Adresse wie bei <see cref="Key"/>.</summary>
    public static string NoTemperatureKey(string providerId, string baseUrl, string model) =>
        $"noTemp|{providerId}|{baseUrl.Trim().TrimEnd('/')}|{model.Trim()}";

    private void Save()
    {
        if (FilePath is null) return;
        var json = new JsonObject { ["noTemperature"] = new JsonArray([.. _noTemperature.Order(StringComparer.Ordinal).Select(k => JsonValue.Create(k))]) };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json.ToJsonString());
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Kaputte oder fehlende Datei = leere Merkliste (es fällt nur temperature 0 weg bzw. ein Fehlversuch mehr).</summary>
    private static HashSet<string> Load(string? path)
    {
        if (path is null || !File.Exists(path)) return [];
        try
        {
            return JsonNode.Parse(File.ReadAllText(path))?["noTemperature"] is JsonArray a
                ? [.. a.Select(n => n?.GetValue<string>()).OfType<string>()]
                : [];
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException)
        {
            return [];
        }
    }
}
