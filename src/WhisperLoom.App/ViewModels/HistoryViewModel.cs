using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.ViewModels;

/// <summary>Ein Verlaufseintrag für die Liste.</summary>
public sealed record HistoryItem(HistoryEntry Entry)
{
    public string Header =>
        $"{Entry.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} · {Loc.Instance["Stage_" + Entry.Refinement.Mode.Key()]} · {Entry.SttLabel}";

    public string Raw => Entry.Raw;

    public string Text => Entry.Text;
}

/// <summary>Verlauf (nur Text): neueste zuerst, Kopieren, Löschen, Leeren.</summary>
public sealed partial class HistoryViewModel(IHistoryStore store) : ObservableObject
{
    public ObservableCollection<HistoryItem> Items { get; } = [];

    [ObservableProperty]
    private HistoryItem? _selected;

    public async Task LoadAsync()
    {
        Items.Clear();
        foreach (var entry in await store.ListAsync(CancellationToken.None))
        {
            Items.Add(new HistoryItem(entry));
        }
    }

    [RelayCommand]
    private async Task Delete(HistoryItem? item)
    {
        if (item is not null && await store.DeleteAsync(item.Entry.Id, CancellationToken.None))
        {
            Items.Remove(item);
        }
    }

    [RelayCommand]
    private async Task Clear()
    {
        await store.ClearAsync(CancellationToken.None);
        Items.Clear();
    }
}
