using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.History;

/// <summary>
/// Der Verlauf als eine JSON-Datei je Eintrag in <see cref="Directory"/>. Port der Ablage von
/// <c>history/History.kt</c>: ein Diktat schreibt nur seine eigene kleine Datei und löscht die ältesten am
/// Dateinamen, ohne den Rest zu lesen. Nur Text, nie Audio. Ist der Verlauf aus, schreibt
/// <see cref="AddAsync"/> nichts; die Höchstzahl kommt bei jedem Schreiben frisch aus den Einstellungen.
/// Jeder Dateizugriff läuft unter einer Sperre; Schreiben geht über eine <c>.tmp</c>-Datei, Reste
/// abgebrochener Schreibvorgänge werden beim Lesen weggeräumt (sie enthalten Text). Nie Text loggen.
/// </summary>
/// <param name="directory">Ordner nur für den Verlauf, z. B. <c>&lt;Datenordner&gt;/history</c>.</param>
/// <param name="settings">Liefert die aktuellen Verlaufs-Einstellungen (an/aus, Größe).</param>
public sealed partial class FileHistoryStore(string directory, Func<HistorySettings> settings) : IHistoryStore
{
    private const string Suffix = ".json";
    private const string TempSuffix = ".tmp";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim gate = new(1, 1);

    public string Directory { get; } = directory;

    /// <summary>Hat <paramref name="s"/> die Form einer Eintrags-Id ("&lt;Unix-ms, 13 Ziffern&gt;-&lt;8 Hex&gt;")?</summary>
    public static bool IsId(string s) => IdPattern().IsMatch(s);

    /// <summary>Neue Id, sortiert wie die Zeit; der Zufallsteil trennt Diktate derselben Millisekunde.</summary>
    public static string NewId(DateTimeOffset createdAt) =>
        $"{createdAt.ToUnixTimeMilliseconds():D13}-{RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue):x8}";

    /// <summary>Schreibt den Eintrag unter seiner Id (auch „Rückgängig“ nach Löschen) und kürzt auf die Höchstzahl.</summary>
    /// <exception cref="ArgumentException">Die Id hat nicht die Form von <see cref="NewId"/>.</exception>
    public async Task AddAsync(HistoryEntry entry, CancellationToken ct)
    {
        if (!IsId(entry.Id))
        {
            throw new ArgumentException("Keine Verlaufs-Id", nameof(entry));
        }
        if (string.IsNullOrWhiteSpace(entry.Raw))
        {
            return;
        }
        await Locked(async () =>
        {
            // Unter der Sperre gelesen: sonst schriebe ein Diktat noch nach dem Ausschalten.
            var limit = Limit(settings());
            if (limit <= 0)
            {
                return 0;
            }
            System.IO.Directory.CreateDirectory(Directory);
            var file = PathOf(entry.Id);
            var temp = file + TempSuffix;
            await File.WriteAllTextAsync(temp, HistoryJson.Write(entry), Utf8NoBom, ct);
            File.Move(temp, file, overwrite: true);
            Trim(limit);
            return 0;
        }, ct);
    }

    public Task<IReadOnlyList<HistoryEntry>> ListAsync(CancellationToken ct) => Locked(async () =>
    {
        Sweep();
        var entries = new List<HistoryEntry>();
        foreach (var id in Ids())
        {
            if (await Read(id, ct) is { } entry)
            {
                entries.Add(entry);
            }
        }
        return (IReadOnlyList<HistoryEntry>)entries;
    }, ct);

    public Task<HistoryEntry?> GetAsync(string id, CancellationToken ct) => Locked(() => Read(id, ct), ct);

    public Task<bool> DeleteAsync(string id, CancellationToken ct) => Locked(() =>
    {
        if (!IsId(id) || !File.Exists(PathOf(id)))
        {
            return Task.FromResult(false);
        }
        File.Delete(PathOf(id));
        return Task.FromResult(true);
    }, ct);

    /// <summary>Alles löschen, samt Resten abgebrochener Schreibvorgänge.</summary>
    public Task ClearAsync(CancellationToken ct) => Locked(() =>
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        return Task.FromResult(0);
    }, ct);

    [GeneratedRegex("^[0-9]{13}-[0-9a-f]{8}$")]
    private static partial Regex IdPattern();

    /// <summary>0 = nichts schreiben; eine nicht wählbare Größe gilt als Standard.</summary>
    private static int Limit(HistorySettings s) =>
        !s.Enabled ? 0 : HistorySettings.Sizes.Contains(s.Size) ? s.Size : HistorySettings.DefaultSize;

    private string PathOf(string id) => Path.Combine(Directory, id + Suffix);

    /// <summary>Ids der Einträge, neueste zuerst — die Id beginnt mit der Zeit.</summary>
    private List<string> Ids()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }
        return System.IO.Directory.EnumerateFiles(Directory, "*" + Suffix)
            .Select(Path.GetFileName)
            .Where(name => name!.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(name => name![..^Suffix.Length])
            .Where(IsId)
            .OrderDescending(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<HistoryEntry?> Read(string id, CancellationToken ct)
    {
        if (!IsId(id) || !File.Exists(PathOf(id)))
        {
            return null;
        }
        return HistoryJson.Read(id, await File.ReadAllTextAsync(PathOf(id), Encoding.UTF8, ct));
    }

    /// <summary>Auf die neuesten <paramref name="limit"/> kürzen.</summary>
    private void Trim(int limit)
    {
        Sweep();
        foreach (var id in Ids().Skip(limit))
        {
            File.Delete(PathOf(id));
        }
    }

    /// <summary>Reste abgebrochener Schreibvorgänge enthalten Text — weg damit. Unter der Sperre schreibt niemand.</summary>
    private void Sweep()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*" + TempSuffix))
        {
            File.Delete(file);
        }
    }

    /// <summary>Unter der Sperre ausführen; Zugriffsfehler der Ablage kommen als <see cref="IOException"/>.</summary>
    private async Task<T> Locked<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException("Verlauf nicht zugreifbar", e);
        }
        finally
        {
            gate.Release();
        }
    }
}
