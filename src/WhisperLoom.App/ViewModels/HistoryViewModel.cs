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

/// <summary>Verlauf (nur Text): neueste zuerst, Kopieren, Löschen, Leeren, neu verarbeiten mit anderer Stufe (WLD-P5b).</summary>
/// <param name="reprocess">Eintrag mit Stufen-Schlüssel neu verarbeiten (<see cref="Services.DictationController.ReprocessAsync"/>); null = aus
/// (auch bei einer IT-Sperre der Stufe).</param>
/// <param name="changed">Nach Löschen bzw. Leeren (true) — Tray-Liste neu aufbauen, beim Leeren auch das letzte Ergebnis vergessen.</param>
public sealed partial class HistoryViewModel(IHistoryStore store, Func<HistoryEntry, string, Task<string>>? reprocess = null, Action<bool>? changed = null)
    : ObservableObject
{
    public ObservableCollection<HistoryItem> Items { get; } = [];

    public bool CanReprocess => reprocess is not null;

    /// <summary>Stufen für „Neu verarbeiten“ (mit Prompt; ist sie nicht freigeschaltet, lehnt der Controller ab).</summary>
    public IReadOnlyList<Choice> Stages { get; } =
        [.. RefineModes.SettingsFor(true).Where(m => m != RefineMode.Off).Select(m => new Choice(m.Key(), Loc.Instance["Stage_" + m.Key()]))];

    [ObservableProperty]
    private Choice? _reprocessStage;

    [ObservableProperty]
    private string _status = "";

    [RelayCommand]
    private async Task Reprocess(HistoryItem? item)
    {
        var stage = ReprocessStage ?? Stages[0];
        if (item is null || reprocess is null)
        {
            return;
        }
        string code;
        try
        {
            code = await reprocess(item.Entry, stage.Value);
        }
        catch (ArgumentException)
        {
            code = "stage-unavailable";
        }
        Status = Loc.Instance["History_Status_" + code];
        await LoadAsync();
    }

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
            changed?.Invoke(false);
        }
    }

    [RelayCommand]
    private async Task Clear()
    {
        await store.ClearAsync(CancellationToken.None);
        Items.Clear();
        changed?.Invoke(true);
    }
}
