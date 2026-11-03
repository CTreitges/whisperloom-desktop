using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Features.History;

/// <summary>
/// Verlaufsfenster (W2-OUT, Android <c>HistoryListScreen</c> + Desktop-Tastatur): Liste nach Tagen, Volltextsuche, Detail mit
/// Fassungen. Tastatur: ↑/↓ wählen, Enter einfügen, Strg+C kopieren, Entf löschen (mit Rückgängig, auch Strg+Z), F2
/// bearbeiten, Strg+F suchen. Kopieren meldet „Kopiert“ erst nach der Gegenprobe der Zwischenablage.
/// </summary>
public sealed partial class HistoryViewModel(IHistoryStore store, HistoryActions actions, Func<DateTimeOffset>? now = null) : ObservableObject
{
    private List<HistoryEntry> _all = [];

    public ObservableCollection<HistoryRow> Rows { get; } = [];

    public HistoryActions Actions => actions;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private HistoryRow? _selected;

    [ObservableProperty]
    private HistoryDetailViewModel? _detail;

    [ObservableProperty]
    private string _status = "";

    /// <summary>Zuletzt gelöschter Eintrag für „Rückgängig“; null = nichts rückgängig zu machen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    private HistoryEntry? _deleted;

    [ObservableProperty]
    private bool _clearPending;

    public bool CanUndo => Deleted is not null;

    public bool HasSelection => Selected is not null;

    public bool IsEmpty => _all.Count == 0;

    public bool NoMatches => _all.Count > 0 && Rows.Count == 0;

    public async Task LoadAsync(string? select = null)
    {
        try
        {
            _all = [.. await store.ListAsync(CancellationToken.None)];
        }
        catch (IOException)
        {
            _all = [];
            Status = Loc.Instance["History_Status_unreadable"];
        }
        Apply(select ?? Selected?.Entry.Id);
    }

    partial void OnSearchChanged(string value) => Apply(Selected?.Entry.Id);

    partial void OnSelectedChanged(HistoryRow? value)
    {
        OnPropertyChanged(nameof(ShowToolbar));
        OnPropertyChanged(nameof(ShowFoot));
        if (value is null)
        {
            Detail = null;
        }
        else if (Detail?.Entry.Id != value.Entry.Id)
        {
            Detail = new HistoryDetailViewModel(value.Entry, actions with { Changed = OnEntryChanged });
        }
    }

    /// <summary>Enter: ins Ziel des letzten Diktats einfügen (Text bleibt auch in der Zwischenablage); ohne Einfügen kopieren.</summary>
    [RelayCommand]
    public async Task InsertSelected()
    {
        if (Detail is not { } detail)
        {
            return;
        }
        if (actions.Insert is null)
        {
            await CopySelected();
            return;
        }
        var code = await actions.Insert(detail.Text);
        Status = Loc.Instance[InsertStatusKey(code)];
    }

    /// <summary>Ergebnis von „einfügen“ (<c>InputFlow.InsertTextAsync</c>) → Statustext; „Kopiert“ nur, wenn der Text dort liegt.</summary>
    internal static string InsertStatusKey(string code) => code switch
    {
        "Pasted" or "Typed" => "History_Status_inserted",
        "PasswordField" => "Output_Hint_Password",
        "ClipboardOnly" or "NoTextField" or "TargetGone" or "TargetProtected" or "NotForeground" or "InputRejected" or "copied" => "Output_Hint_Copied",
        "busy" => "History_Status_busy",
        _ => "History_Status_insertFailed",
    };


    /// <summary>Strg+C: gewählte Fassung kopieren; „Kopiert“ erst nach erfolgreicher Gegenprobe.</summary>
    [RelayCommand]
    public async Task CopySelected()
    {
        if (Detail is { } detail)
        {
            Status = Loc.Instance[await actions.Copy(detail.Text) ? "History_Status_copied" : "History_Status_copyFailed"];
        }
    }

    /// <summary>Entf: löschen; „Rückgängig“ bringt den Eintrag unter seiner alten Id zurück.</summary>
    [RelayCommand]
    public async Task DeleteSelected()
    {
        if (Selected is not { } row)
        {
            return;
        }
        var index = Rows.IndexOf(row);
        bool removed;
        try
        {
            removed = await store.DeleteAsync(row.Entry.Id, CancellationToken.None);
        }
        catch (IOException)
        {
            removed = false;
        }
        if (!removed)
        {
            return;
        }
        Deleted = row.Entry;
        // Befund 8: sagt, was gelöscht wurde („Diktat von 13:30 gelöscht.“).
        Status = Loc.Instance.Format("History_Status_deleted", row.Entry.CreatedAt.ToLocalTime().ToString("t", HistoryRow.Culture));
        _all.RemoveAll(e => e.Id == row.Entry.Id);
        Apply(null);
        // Die nächste Zeile wählen, damit Entf mehrfach hintereinander geht.
        Selected = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        actions.Changed?.Invoke(null);
    }

    [RelayCommand]
    public async Task Undo()
    {
        if (Deleted is not { } entry)
        {
            return;
        }
        Deleted = null;
        try
        {
            await store.AddAsync(entry, CancellationToken.None);
        }
        catch (IOException)
        {
            Status = Loc.Instance["History_Status_unreadable"];
            return;
        }
        Status = Loc.Instance["History_Status_restored"];
        await LoadAsync(entry.Id);
        actions.Changed?.Invoke(entry);
    }

    /// <summary>F2: gewählte Fassung bearbeiten.</summary>
    [RelayCommand]
    public void EditSelected() => Detail?.BeginEdit();

    [RelayCommand]
    public void AskClear() => ClearPending = _all.Count > 0;

    [RelayCommand]
    public void CancelClear() => ClearPending = false;

    [RelayCommand]
    public async Task Clear()
    {
        ClearPending = false;
        await store.ClearAsync(CancellationToken.None);
        Deleted = null;
        _all = [];
        Apply(null);
        actions.Cleared?.Invoke();
    }

    private void OnEntryChanged(HistoryEntry? entry)
    {
        if (entry is not null)
        {
            var i = _all.FindIndex(e => e.Id == entry.Id);
            if (i >= 0)
            {
                _all[i] = entry;
            }
            Apply(Selected?.Entry.Id);
        }
        actions.Changed?.Invoke(entry);
    }

    private void Apply(string? select)
    {
        var detail = Detail;
        Rows.Clear();
        foreach (var row in HistoryRow.Build(_all.Where(e => HistoryRow.Matches(e, Search)), (now ?? (() => DateTimeOffset.Now))()))
        {
            Rows.Add(row);
        }
        var keep = Rows.FirstOrDefault(r => r.Entry.Id == select) ?? (Rows.Count > 0 ? Rows[0] : null);
        Detail = keep is not null && detail?.Entry.Id == keep.Entry.Id ? detail : null;
        Selected = keep;
        if (keep is not null && Detail is null)
        {
            Detail = new HistoryDetailViewModel(keep.Entry, actions with { Changed = OnEntryChanged });
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(NoMatches));
        RaiseListTexts();
    }
}
