using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Services;

namespace WhisperLoom.App.ViewModels;

/// <summary>Eine Kombi in den Einstellungen: Wert, Rekorder-Knopf und Konfliktwarnungen.</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    private readonly HotkeysViewModel _owner;
    private readonly Func<Task<string?>>? _record;

    internal HotkeyRowViewModel(HotkeysViewModel owner, string path, string labelKey, string value, Func<Task<string?>>? record)
    {
        _owner = owner;
        Path = path;
        LabelKey = labelKey;
        _value = value;
        _record = record;
    }

    /// <summary>Settings-Pfad, z. B. <c>desktop.hotkeyHold</c>.</summary>
    public string Path { get; }

    public string LabelKey { get; }

    public string Label => Loc.Instance[LabelKey];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warnings))]
    private string _value;

    [ObservableProperty]
    private bool _isRecording;

    public bool CanRecord => _record is not null;

    /// <summary>Warnungen als Text (eine je Zeile); leer = keine.</summary>
    public string Warnings => _owner.WarningsFor(this);

    internal void Refresh() => OnPropertyChanged(nameof(Warnings));

    partial void OnValueChanged(string value) => _owner.RowChanged();

    [RelayCommand]
    private async Task Record()
    {
        if (_record is null || IsRecording)
        {
            return;
        }
        IsRecording = true;
        try
        {
            if (await _record() is { } combo)
            {
                Value = combo;
            }
        }
        finally
        {
            IsRecording = false;
        }
    }
}

/// <summary>
/// Einstellungsseite „Tastenkürzel“ (nur Windows): an/aus, Halten, Umschalten, Stufe, erneut einfügen, umschreiben. Aufnehmen läuft über den
/// globalen Hook (<see cref="WindowsInput.RecordAsync"/>), damit auch Win-Kombis ankommen. Schreibt in
/// <see cref="DesktopSettings"/>; gespeichert wird mit dem Rest der Einstellungen.
/// </summary>
public sealed partial class HotkeysViewModel : ObservableObject
{
    private readonly DesktopSettings _desktop;

    public HotkeysViewModel(DesktopSettings desktop, Func<Task<string?>>? record)
    {
        _desktop = desktop;
        Hold = new HotkeyRowViewModel(this, "desktop.hotkeyHold", "Hotkey_Hold", desktop.HotkeyHold, record);
        Toggle = new HotkeyRowViewModel(this, "desktop.hotkeyToggle", "Hotkey_Toggle", desktop.HotkeyToggle, record);
        Stage = new HotkeyRowViewModel(this, "desktop.hotkeyStage", "Hotkey_Stage", desktop.HotkeyStage, record);
        Reinsert = new HotkeyRowViewModel(this, "desktop.hotkeyReinsert", "Hotkey_Reinsert", desktop.HotkeyReinsert, record);
        Rewrite = new HotkeyRowViewModel(this, "desktop.hotkeyRewrite", "Hotkey_Rewrite", desktop.HotkeyRewrite, record);
    }

    /// <summary>Seite nur unter Windows; unter Linux steuern Kürzel der Desktop-Umgebung die CLI.</summary>
    public bool Available => OperatingSystem.IsWindows();

    public bool Enabled
    {
        get => _desktop.HotkeysEnabled;
        set
        {
            if (_desktop.HotkeysEnabled != value)
            {
                _desktop.HotkeysEnabled = value;
                OnPropertyChanged();
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Nach jeder Änderung (Einstellungsfenster speichert sofort).</summary>
    public Action? Changed { get; init; }

    /// <summary>Alle Aktionen in fester Reihenfolge; die Seite nutzt ein Template je Aktion.</summary>
    public IReadOnlyList<HotkeyRowViewModel> All => Rows;

    public HotkeyRowViewModel Hold { get; }

    public HotkeyRowViewModel Toggle { get; }

    public HotkeyRowViewModel Stage { get; }

    /// <summary>Letztes Diktat erneut einfügen (WLD-P5b).</summary>
    public HotkeyRowViewModel Reinsert { get; }

    /// <summary>Markierten Text umschreiben (WLD-P5b).</summary>
    public HotkeyRowViewModel Rewrite { get; }

    private HotkeyRowViewModel[] Rows => [Hold, Toggle, Stage, Reinsert, Rewrite];

    /// <summary>Übernimmt die Werte in die Desktop-Einstellungen (vor dem Speichern).</summary>
    public void Apply()
    {
        _desktop.HotkeyHold = Hold.Value;
        _desktop.HotkeyToggle = Toggle.Value;
        _desktop.HotkeyStage = Stage.Value;
        _desktop.HotkeyReinsert = Reinsert.Value;
        _desktop.HotkeyRewrite = Rewrite.Value;
    }

    internal void RowChanged()
    {
        foreach (var row in Rows)
        {
            row.Refresh();
        }
        Changed?.Invoke();
    }

    internal string WarningsFor(HotkeyRowViewModel row)
    {
        var combo = Hotkey.Parse(row.Value);
        if (combo is null)
        {
            return "";
        }
        var others = Rows.Where(r => r != row).Select(r => Hotkey.Parse(r.Value));
        return string.Join("\n", HotkeyConflicts.For(combo, others).Select(k => "⚠ " + Loc.Instance[k]));
    }
}
