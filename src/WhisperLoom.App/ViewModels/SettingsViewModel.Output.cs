using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// Ausgabe und Aufbewahrung (W2-OUT): „Immer auch in die Zwischenablage kopieren“, „Enter nach dem Einfügen“ (global; App-Profile
/// erben oder überschreiben) und die Verlaufsgröße mit Rückfrage, wenn Verkleinern ältere Diktate löschen würde (Android
/// <c>History.excess</c>).
/// </summary>
public sealed partial class SettingsViewModel
{
    private int? _pendingHistorySize;
    private int _historyExcess;

    public bool AlwaysCopy { get => _s.Output.AlwaysCopy; set => SetPlain(() => _s.Output.AlwaysCopy = value); }

    public bool PressEnter { get => _s.Output.PressEnter; set => SetPlain(() => _s.Output.PressEnter = value); }

    /// <summary>Schwebender Knopf an einer Maus-Markierung (W2-SEL, <c>selection.floatingButton</c>).</summary>
    public bool FloatingButton { get => _s.Selection.FloatingButton; set => SetPlain(() => _s.Selection.FloatingButton = value); }

    /// <summary>Strg+Rechtsklick auf eine Markierung (W2-SEL, <c>selection.ctrlRightClick</c>).</summary>
    public bool CtrlRightClick { get => _s.Selection.CtrlRightClick; set => SetPlain(() => _s.Selection.CtrlRightClick = value); }

    /// <summary>Auswahl der Größe im Fenster; ein kleinerer Wert mit zu vielen Einträgen fragt erst nach.</summary>
    public int HistorySizeChoice
    {
        get => _pendingHistorySize ?? HistorySize;
        set => _ = ChooseHistorySizeAsync(value);
    }

    /// <summary>Größen als Optionsgruppe (Nachher-Audit UX-6 N7: sechs feste Werte als Optionsfelder statt Auswahlliste).</summary>
    public IReadOnlyList<Choice> HistorySizeOptions =>
        _historySizeOptions ??= [.. HistorySizes.Select(n => new Choice(n.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.CurrentCulture)))];

    private IReadOnlyList<Choice>? _historySizeOptions;

    /// <summary>Gewählte Größe der Optionsgruppe; schreibt über <see cref="HistorySizeChoice"/> (mit Rückfrage beim Verkleinern).</summary>
    public Choice? HistorySizeOption
    {
        get => HistorySizeOptions.FirstOrDefault(c => c.Value == HistorySizeChoice.ToString(CultureInfo.InvariantCulture));
        set
        {
            if (value is not null && int.TryParse(value.Value, CultureInfo.InvariantCulture, out var size) && size != HistorySizeChoice)
            {
                HistorySizeChoice = size;
            }
        }
    }

    /// <summary>Rückfrage „N ältere Diktate löschen?“ sichtbar.</summary>
    public bool HistoryShrinkPending => _pendingHistorySize is not null;

    public string HistoryShrinkText => string.Format(System.Globalization.CultureInfo.CurrentCulture, Loc.Instance["History_ShrinkConfirm_Desc"], _historyExcess);

    /// <summary>Wartet auf die laufende Größenwahl (Tests).</summary>
    public Task HistorySizePending { get; private set; } = Task.CompletedTask;

    private Task ChooseHistorySizeAsync(int size) => HistorySizePending = ChooseCoreAsync(size);

    private async Task ChooseCoreAsync(int size)
    {
        if (!AutoSave || size >= HistorySize)
        {
            SetPendingSize(null, 0);
            HistorySize = size;
            return;
        }
        int count;
        try
        {
            count = await _controller.HistoryCountAsync();
        }
        catch (IOException)
        {
            count = 0;
        }
        if (count - size <= 0)
        {
            SetPendingSize(null, 0);
            HistorySize = size;
            return;
        }
        SetPendingSize(size, count - size);
    }

    [RelayCommand]
    private async Task ConfirmHistoryShrink()
    {
        if (_pendingHistorySize is not { } size)
        {
            return;
        }
        SetPendingSize(null, 0);
        HistorySize = size;
        await Pending;
        await _controller.TrimHistoryAsync();
    }

    [RelayCommand]
    private void CancelHistoryShrink() => SetPendingSize(null, 0);

    private void SetPendingSize(int? size, int excess)
    {
        _pendingHistorySize = size;
        _historyExcess = excess;
        OnPropertyChanged(nameof(HistorySizeChoice));
        OnPropertyChanged(nameof(HistorySizeOption));
        OnPropertyChanged(nameof(HistoryShrinkPending));
        OnPropertyChanged(nameof(HistoryShrinkText));
    }
}
