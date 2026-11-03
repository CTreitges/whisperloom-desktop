using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.History;

namespace WhisperLoom.App.Features.History;

/// <summary>Chip im Detail: der Ursprung (Rohtext bzw. bearbeiteter Ursprung) oder eine Fassung.</summary>
public sealed partial class VersionChip(string key, string label, bool edited) : ObservableObject
{
    /// <summary>Kennung für den Ursprung.</summary>
    public const string Origin = "origin";

    public string Key { get; } = key;

    public string Label { get; } = label;

    public bool Edited { get; } = edited;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Detail eines Verlaufseintrags (W2-OUT, Android <c>HistoryDetailScreen</c>): Chips Ursprung/Fassungen, Text der gewählten,
/// Bearbeiten (F2) und „Neu verarbeiten“ über den Stufen-Wähler – im Hintergrund, mit Rückfrage, bevor eine bearbeitete
/// Fassung ersetzt würde. Der Ursprung (Rohtext) wird nie überschrieben: Bearbeiten legt die Fassung „bearbeitet“ an.
/// </summary>
public sealed partial class HistoryDetailViewModel : ObservableObject
{
    private readonly HistoryActions _actions;

    public HistoryDetailViewModel(HistoryEntry entry, HistoryActions actions, string? select = null)
    {
        _actions = actions;
        _entry = entry;
        Rebuild(select ?? HistoryVersions.MainKey(entry));
        if (actions.Reprocess is not null)
        {
            StagePicker = new StagePickerViewModel(actions.Stages, HistoryVersions.MainKey(entry));
            StagePicker.Chosen += key => _ = RequestReprocessAsync(key);
            StagePicker.Cancelled += () => IsStagePickerOpen = false;
        }
    }

    [ObservableProperty]
    private HistoryEntry _entry;

    public ObservableCollection<VersionChip> Chips { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(IsOrigin))]
    private VersionChip? _selectedChip;

    /// <summary>Text des gewählten Chips.</summary>
    public string Text => SelectedChip is null ? Entry.Text : TextOf(SelectedChip.Key);

    public bool IsOrigin => SelectedChip?.Key == VersionChip.Origin;

    public string Header => $"{HistoryRow.Day(DateOnly.FromDateTime(Entry.CreatedAt.ToLocalTime().Date), DateOnly.FromDateTime(DateTime.Now.Date))} · " +
        $"{Entry.CreatedAt.ToLocalTime().ToString("t", HistoryRow.Culture)}";

    // --- Bearbeiten -----------------------------------------------------------------------

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editText = "";

    [RelayCommand]
    public void BeginEdit()
    {
        EditText = Text;
        IsEditing = true;
    }

    [RelayCommand]
    public void CancelEdit() => IsEditing = false;

    [RelayCommand]
    public async Task SaveEdit()
    {
        if (!IsEditing || SelectedChip is null)
        {
            return;
        }
        var key = SelectedChip.Key == VersionChip.Origin ? HistoryVersions.Edited : SelectedChip.Key;
        var updated = await _actions.Edit(Entry.Id, key, EditText);
        IsEditing = false;
        if (updated is null)
        {
            Status = Loc.Instance["History_Status_gone"];
            return;
        }
        Entry = updated;
        Rebuild(SelectedChip.Key);
        Status = Loc.Instance["History_Status_edited"];
        _actions.Changed?.Invoke(updated);
    }

    // --- Neu verarbeiten -------------------------------------------------------------------

    public StagePickerViewModel? StagePicker { get; }

    public bool CanReprocess => StagePicker is not null;

    [ObservableProperty]
    private bool _isStagePickerOpen;

    /// <summary>Stufe, deren bearbeitete Fassung nach Rückfrage ersetzt würde; null = keine Rückfrage offen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmReplacePending))]
    private string? _pendingStage;

    public bool ConfirmReplacePending => PendingStage is not null;

    /// <summary>Läuft gerade eine Rechnung für diesen Eintrag (Ladeanzeige).</summary>
    [ObservableProperty]
    private string? _runningStage;

    [ObservableProperty]
    private string _status = "";

    [RelayCommand]
    public void ToggleStagePicker() => IsStagePickerOpen = CanReprocess && !IsStagePickerOpen;

    /// <summary>Stufe gewählt: bei bearbeiteter Fassung erst fragen, sonst im Hintergrund rechnen.</summary>
    public Task RequestReprocessAsync(string stage)
    {
        IsStagePickerOpen = false;
        if (HistoryVersions.NeedsConfirmBeforeReplace(Entry, stage))
        {
            PendingStage = stage;
            return Task.CompletedTask;
        }
        return ReprocessAsync(stage);
    }

    [RelayCommand]
    public Task ConfirmReplace()
    {
        var stage = PendingStage;
        PendingStage = null;
        return stage is null ? Task.CompletedTask : ReprocessAsync(stage);
    }

    [RelayCommand]
    public void CancelReplace() => PendingStage = null;

    /// <summary>Letzte Rechnung (Tests warten darauf).</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    private Task ReprocessAsync(string stage) => Running = ReprocessCoreAsync(stage);

    private async Task ReprocessCoreAsync(string stage)
    {
        if (_actions.Reprocess is null || RunningStage is not null)
        {
            return;
        }
        RunningStage = stage;
        Status = Loc.Instance.Format("History_Status_running", Loc.Instance["Stage_" + stage]);
        Services.ReprocessOutcome result;
        try
        {
            result = await _actions.Reprocess(Entry.Id, stage);
        }
        catch (ArgumentException)
        {
            result = new Services.ReprocessOutcome("stage-unavailable");
        }
        RunningStage = null;
        if (result.Entry is { } updated)
        {
            Entry = updated;
            Rebuild(result.Code == "saved" ? stage : SelectedChip?.Key ?? stage);
            _actions.Changed?.Invoke(updated);
        }
        Status = Loc.Instance["History_Status_" + result.Code];
    }

    // --- Intern ------------------------------------------------------------------------------

    partial void OnSelectedChipChanged(VersionChip? value)
    {
        foreach (var chip in Chips)
        {
            chip.IsSelected = chip == value;
        }
        IsEditing = false;
    }

    [RelayCommand]
    private void Select(VersionChip? chip)
    {
        if (chip is not null)
        {
            SelectedChip = chip;
        }
    }

    private string TextOf(string key) =>
        key == VersionChip.Origin
            ? HistoryVersions.Find(Entry, HistoryVersions.Edited)?.Text ?? Entry.Raw
            : HistoryVersions.Find(Entry, key)?.Text ?? Entry.Text;

    private void Rebuild(string select)
    {
        Chips.Clear();
        var originEdited = HistoryVersions.Find(Entry, HistoryVersions.Edited) is not null;
        Chips.Add(new VersionChip(VersionChip.Origin, Loc.Instance["History_Origin"] + (originEdited ? " · " + Loc.Instance["History_Edited"] : ""), originEdited));
        foreach (var v in HistoryVersions.All(Entry).Where(v => v.Processing != HistoryVersions.Edited))
        {
            Chips.Add(new VersionChip(v.Processing, Loc.Instance["Stage_" + v.Processing] + (v.Edited ? " · " + Loc.Instance["History_Edited"] : ""), v.Edited));
        }
        SelectedChip = Chips.FirstOrDefault(c => c.Key == select) ?? Chips.FirstOrDefault(c => c.Key == HistoryVersions.MainKey(Entry)) ?? Chips[0];
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Header));
    }
}

/// <summary>Was das Verlaufsfenster mit der App tun kann (Rückrufe; Tests: Fakes).</summary>
/// <param name="Copy">Text verlässlich kopieren (<see cref="AppHost.CopyTextAsync"/>).</param>
/// <param name="Insert">Ins Ziel des letzten Diktats einfügen (Tray-Weg); null = nur kopieren.</param>
/// <param name="Reprocess">Fassung im Hintergrund neu rechnen; null = aus (IT-Sperre).</param>
/// <param name="Edit">Fassung bearbeiten.</param>
/// <param name="Stages">Wählbare Stufen (Schlüssel, Anzeige).</param>
/// <param name="Changed">Eintrag geändert oder gelöscht (Tray-Liste neu laden).</param>
/// <param name="Cleared">Verlauf geleert (auch das letzte Ergebnis vergessen).</param>
/// <param name="StartKeys">Tastenkürzel Halten und Umschalten für den Leerzustand; null = ohne globale Kürzel (Linux).</param>
public sealed record HistoryActions(
    Func<string, Task<bool>> Copy,
    Func<string, Task<string>>? Insert,
    Func<string, string, Task<Services.ReprocessOutcome>>? Reprocess,
    Func<string, string, string, Task<HistoryEntry?>> Edit,
    IReadOnlyList<(string Key, string Label)> Stages,
    Action<HistoryEntry?>? Changed = null,
    Action? Cleared = null,
    IReadOnlyList<string>? StartKeys = null)
{
    /// <summary>
    /// Starttasten für den Leerzustand: Halten und Umschalten unter Windows mit eingeschalteten Tastenkürzeln; Windows ohne
    /// Kürzel leer (Satz mit Tray-Menü und Bühne); Linux null (Satz mit dem Desktop-Kürzel). Prüfer-Funde PR #42.
    /// </summary>
    public static IReadOnlyList<string>? StartKeysFor(Services.DesktopSettings desktop, bool windows) =>
        !windows ? null : desktop.HotkeysEnabled ? [desktop.HotkeyHold, desktop.HotkeyToggle] : [];
}
