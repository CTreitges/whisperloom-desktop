using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WhisperLoom.App.Design;

/// <summary>Eine Zeile im Stufen-Wähler.</summary>
public sealed partial class StageOption(string key, string label, int number) : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    /// <summary>Ziffer 1–9 (0 = keine, ab der zehnten Stufe).</summary>
    public int Number { get; } = number;

    public string NumberText => Number is > 0 and <= 9 ? Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

    /// <summary>Tastatur-Markierung (↑/↓, Tab).</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>Die aktuell eingestellte Stufe (Häkchen).</summary>
    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>
/// Stufen-Wähler (W2-OUT, wiederverwendbar für Verlauf, Overlay und W2-SEL am Cursor): Liste der Stufen, aktuelle Stufe mit
/// Häkchen, Auswahl per Klick, ↑/↓ bzw. Tab/Umschalt+Tab (rundum), Ziffer 1–9 (wählt sofort), Enter (wählt die markierte),
/// Esc (bricht ab). Wählen löst <see cref="Chosen"/> aus, Abbrechen <see cref="Cancelled"/>; was dann passiert, entscheidet
/// der Aufrufer.
/// </summary>
public sealed partial class StagePickerViewModel : ObservableObject
{
    public StagePickerViewModel(IEnumerable<(string Key, string Label)> stages, string? current = null)
    {
        var n = 0;
        foreach (var (key, label) in stages)
        {
            n++;
            Options.Add(new StageOption(key, label, n <= 9 ? n : 0));
        }
        Current = current;
        Highlight(Math.Max(0, IndexOf(current)));
    }

    public ObservableCollection<StageOption> Options { get; } = [];

    /// <summary>Gewählte Stufe (Schlüssel).</summary>
    public event Action<string>? Chosen;

    public event Action? Cancelled;

    [ObservableProperty]
    private string? _current;

    [ObservableProperty]
    private int _highlighted = -1;

    public StageOption? HighlightedOption => Highlighted >= 0 && Highlighted < Options.Count ? Options[Highlighted] : null;

    partial void OnCurrentChanged(string? value)
    {
        foreach (var o in Options)
        {
            o.IsCurrent = o.Key == value;
        }
    }

    partial void OnHighlightedChanged(int value)
    {
        for (var i = 0; i < Options.Count; i++)
        {
            Options[i].IsHighlighted = i == value;
        }
        OnPropertyChanged(nameof(HighlightedOption));
    }

    /// <summary>Markierung um <paramref name="delta"/> verschieben, am Ende rundum.</summary>
    public void Move(int delta)
    {
        if (Options.Count > 0)
        {
            Highlight(((Highlighted + delta) % Options.Count + Options.Count) % Options.Count);
        }
    }

    public void Highlight(int index)
    {
        if (index >= 0 && index < Options.Count)
        {
            Highlighted = index;
        }
    }

    /// <summary>Ziffer 1–9: diese Stufe sofort wählen; false, wenn es sie nicht gibt.</summary>
    public bool ChooseNumber(int number)
    {
        var index = number - 1;
        if (number is < 1 or > 9 || index >= Options.Count)
        {
            return false;
        }
        Highlight(index);
        Confirm();
        return true;
    }

    /// <summary>Enter: die markierte Stufe wählen.</summary>
    [RelayCommand]
    public void Confirm()
    {
        if (HighlightedOption is { } option)
        {
            Current = option.Key;
            Chosen?.Invoke(option.Key);
        }
    }

    /// <summary>Klick auf eine Zeile.</summary>
    [RelayCommand]
    public void Pick(StageOption? option)
    {
        if (option is not null && Options.IndexOf(option) is var index and >= 0)
        {
            Highlight(index);
            Confirm();
        }
    }

    [RelayCommand]
    public void Cancel() => Cancelled?.Invoke();

    private int IndexOf(string? key)
    {
        for (var i = 0; i < Options.Count; i++)
        {
            if (Options[i].Key == key)
            {
                return i;
            }
        }
        return -1;
    }
}
