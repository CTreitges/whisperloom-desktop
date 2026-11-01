using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Design;

/// <summary>
/// Baustein „Auswahlliste mit ‚Eigenes …‘“: feste Optionen plus ein letzter Eintrag, der ein Textfeld öffnet. Der Wert
/// geht über <c>commit</c> hinaus (gibt einen Fehlertext zurück oder null); ein leeres „Eigenes …“ ist ungültig und wird
/// nicht übernommen. Optionen lassen sich ersetzen (z. B. nach Anbieterwechsel); ein Wert außerhalb der Liste gilt als eigener.
/// </summary>
public sealed partial class CustomChoiceViewModel : ObservableObject
{
    /// <summary>Interner Wert des Eintrags „Eigenes …“ (kein gültiger Modellname).</summary>
    public const string CustomValue = "\u0001custom";

    private readonly Func<string> _read;
    private readonly Func<string, string?> _commit;
    private readonly string _customLabelKey;
    private bool _loading;

    /// <param name="read">Aktueller Wert ("" = Standard-Eintrag).</param>
    /// <param name="commit">Übernimmt einen Wert; Rückgabe = Fehlertext oder null.</param>
    /// <param name="customLabelKey">Lokalisierung des letzten Eintrags, z. B. „Eigenes Modell …“.</param>
    public CustomChoiceViewModel(IEnumerable<Choice> options, Func<string> read, Func<string, string?> commit, string customLabelKey)
    {
        _read = read;
        _commit = commit;
        _customLabelKey = customLabelKey;
        SetOptions(options);
    }

    public ObservableCollection<Choice> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    private Choice? _selected;

    [ObservableProperty]
    private string _customText = "";

    [ObservableProperty]
    private string _error = "";

    public bool IsCustom => Selected?.Value == CustomValue;

    /// <summary>Ersetzt die Optionen und wählt den aktuellen Wert wieder aus (oder „Eigenes …“ mit dem Wert im Textfeld).</summary>
    public void SetOptions(IEnumerable<Choice> options)
    {
        _loading = true;
        try
        {
            Items.Clear();
            foreach (var option in options)
            {
                Items.Add(option);
            }
            Items.Add(new Choice(CustomValue, Loc.Instance[_customLabelKey]));
            var value = _read();
            var match = Items.FirstOrDefault(c => c.Value == value && c.Value != CustomValue);
            CustomText = match is null ? value : "";
            Selected = match ?? Items[^1];
            Error = "";
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedChanged(Choice? value)
    {
        if (_loading || value is null)
        {
            return;
        }
        Commit(value.Value == CustomValue ? CustomText : value.Value, custom: value.Value == CustomValue);
    }

    partial void OnCustomTextChanged(string value)
    {
        if (!_loading && IsCustom)
        {
            Commit(value, custom: true);
        }
    }

    private void Commit(string value, bool custom)
    {
        var trimmed = value.Trim();
        if (custom && trimmed.Length == 0)
        {
            Error = Loc.Instance["Validation_CustomEmpty"];
            return;
        }
        Error = _commit(trimmed) ?? "";
    }
}
