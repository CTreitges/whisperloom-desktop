using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Services;

/// <summary>
/// Verlauf im Speicher, bis WLD-INT den Datei-Verlauf aus dem Core verdrahtet. Beachtet „Verlauf aus“ und die
/// Höchstzahl aus den Einstellungen wie der Vertrag (<see cref="IHistoryStore"/>).
/// </summary>
public sealed class InMemoryHistoryStore : IHistoryStore
{
    private readonly Func<HistorySettings> _settings;
    private readonly List<HistoryEntry> _entries = [];
    private readonly object _gate = new();

    public InMemoryHistoryStore(Func<HistorySettings> settings)
    {
        _settings = settings;
    }

    public Task AddAsync(HistoryEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var settings = _settings();
        if (!settings.Enabled)
        {
            return Task.CompletedTask;
        }
        var max = HistorySettings.Sizes.Contains(settings.Size) ? settings.Size : HistorySettings.DefaultSize;
        lock (_gate)
        {
            _entries.Add(entry);
            _entries.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            while (_entries.Count > max)
            {
                _entries.RemoveAt(0);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoryEntry>> ListAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<HistoryEntry>>(_entries.AsEnumerable().Reverse().ToList());
        }
    }

    public Task<HistoryEntry?> GetAsync(string id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));
        }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.RemoveAll(e => e.Id == id) > 0);
        }
    }

    public Task ClearAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _entries.Clear();
        }
        return Task.CompletedTask;
    }

    /// <summary>ID wie Android: Unix-ms (13 Ziffern) + 8 Hex.</summary>
    public static string NewId(DateTimeOffset at) =>
        $"{at.ToUnixTimeMilliseconds():D13}-{Random.Shared.Next():x8}";
}
