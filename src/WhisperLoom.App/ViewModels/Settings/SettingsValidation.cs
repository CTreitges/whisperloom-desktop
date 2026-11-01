using System.ComponentModel;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Api;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Prüfungen für die Eingabe im Einstellungsfenster. Ein ungültiger Wert wird nicht gespeichert; die Zeile zeigt den
/// Fehlertext (lokalisiert). Gleiche Regeln wie der Import (<c>SyncValidator</c>), aber ohne die strengere Regel für
/// unverschlüsseltes http – die eigene Eingabe war schon bisher erlaubt.
/// </summary>
public static class SettingsValidation
{
    /// <summary>Leer (= Standard des Anbieters) oder eine http(s)-Adresse, die der Anbieter erlaubt.</summary>
    public static string? BaseUrl(string url, string providerId)
    {
        if (url.Length == 0)
        {
            return null;
        }
        if (!(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.Host.Length > 0))
        {
            return Loc.Instance["Validation_Url"];
        }
        return ProviderCatalog.Find(providerId) is { } provider && ServerUrlCheck.Check(url, provider) is { Severity: ServerUrlCheck.Severity.Error } problem
            ? problem.Message == ServerUrlCheck.MsgHttpsRequired ? Loc.Instance["Validation_Https"] : Loc.Instance["Validation_Url"]
            : null;
    }

    /// <summary>Proxy-Adresse: leer oder http(s)-Adresse mit Host.</summary>
    public static string? ProxyUrl(string url) =>
        url.Length == 0 || (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.Host.Length > 0)
            ? null
            : Loc.Instance["Validation_Url"];

    /// <summary>Ganze Zahl in einem Bereich; <paramref name="zeroAllowed"/> = 0 steht für „Standard“.</summary>
    public static string? Integer(string text, int min, int max, bool zeroAllowed, out int value)
    {
        if (int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value)
            && ((zeroAllowed && value == 0) || (value >= min && value <= max)))
        {
            return null;
        }
        return zeroAllowed
            ? Loc.Instance.Format("Validation_RangeOrZero", min, max)
            : Loc.Instance.Format("Validation_Range", min, max);
    }
}

/// <summary>Inline-Fehler je Pfad für die Bindung <c>Errors[stt.baseUrl]</c>; "" = kein Fehler.</summary>
public sealed class ErrorMap : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string path] => _errors.GetValueOrDefault(path, "");

    public bool Any => _errors.Count > 0;

    /// <summary>Setzt bzw. löscht (null/leer) den Fehler eines Pfades.</summary>
    public void Set(string path, string? error)
    {
        var changed = string.IsNullOrEmpty(error) ? _errors.Remove(path) : _errors.GetValueOrDefault(path) != error;
        if (!string.IsNullOrEmpty(error))
        {
            _errors[path] = error;
        }
        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}
