using System.Globalization;
using System.Text;

namespace WhisperLoom.App.Services;

/// <summary>
/// Log nur mit Metadaten (Dauer, Anbieter, Status, Fehlerklasse) in <c>&lt;Datenordner&gt;/logs/whisperloom.log</c>.
/// Es gibt bewusst keine Methode für freien Text: Werte sind Zahlen oder kurze Kennungen, nie Diktattext, Keys
/// oder Header. Längere oder mehrzeilige Werte werden verworfen.
/// </summary>
public sealed class AppLog
{
    private const int MaxValueLength = 64;
    private readonly object _gate = new();

    public AppLog(string dataDir)
    {
        FilePath = Path.Combine(dataDir, "logs", "whisperloom.log");
    }

    public string FilePath { get; }

    public void Event(string name, params (string Key, object? Value)[] fields)
    {
        var line = new StringBuilder();
        line.Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append(' ').Append(name);
        foreach (var (key, value) in fields)
        {
            line.Append(' ').Append(key).Append('=').Append(Format(value));
        }
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line.Append('\n').ToString());
            }
            catch (IOException)
            {
                // Ein Log darf die App nie anhalten.
            }
        }
    }

    internal static string Format(object? value)
    {
        var text = value switch
        {
            null => "-",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "-",
        };
        return text.Length > MaxValueLength || text.Any(char.IsWhiteSpace) ? "(verworfen)" : text;
    }
}
