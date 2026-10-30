using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace WhisperLoom.App.Localization;

/// <summary>
/// UI-Texte DE/EN aus <c>Localization/de.json</c> und <c>en.json</c> (eingebettet), zur Laufzeit umschaltbar.
/// XAML bindet über den Indexer: <c>{Binding [Stage_Raw], Source={x:Static l:Loc.Instance}}</c>; ein Sprachwechsel
/// meldet <c>Item[]</c>, alle Bindungen lesen neu. Fehlende Schlüssel erscheinen als <c>!Schlüssel!</c>
/// (<c>tools/check_i18n.py</c> verhindert das).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly IReadOnlyList<string> Languages = ["de", "en"];

    public static Loc Instance { get; } = new();

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables = [];
    private IReadOnlyDictionary<string, string> _current;

    private Loc()
    {
        foreach (var lang in Languages)
        {
            _tables[lang] = LoadTable(lang);
        }
        Language = OsLanguage();
        _current = _tables[Language];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>"de" oder "en".</summary>
    public string Language { get; private set; }

    public string this[string key] => _current.TryGetValue(key, out var text) ? text : $"!{key}!";

    /// <summary>Schaltet um; "" oder Unbekanntes = OS-Sprache.</summary>
    public void SetLanguage(string? language)
    {
        var lang = Languages.Contains(language ?? "") ? language! : OsLanguage();
        if (lang == Language)
        {
            return;
        }
        Language = lang;
        _current = _tables[lang];
        // Avalonia-Indexer-Bindungen hören auf "Item", WPF-artige auf "Item[]": beide melden.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    public string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);

    /// <summary>Deutsch, wenn die OS-Sprache Deutsch ist, sonst Englisch.</summary>
    public static string OsLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";

    internal static IReadOnlyDictionary<string, string> LoadTable(string lang)
    {
        var name = $"WhisperLoom.App.Localization.{lang}.json";
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Ressource fehlt: {name}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
