using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>
/// Modellwahl eines Zugangs (W2-LOCAL, Android „Modelle vom Server“): eine Auswahl aus Katalog bzw. Modellen des
/// Eintrags, der zuletzt geladenen Serverliste und „Eigenes Modell …“, dazu „Vom Server laden“ mit Fehlertext.
/// </summary>
public sealed partial class ModelPickerViewModel : ObservableObject
{
    private readonly Func<IEnumerable<Choice>> _options;
    private readonly Func<CancellationToken, Task<string?>> _load;
    private readonly Action? _loaded;

    /// <param name="options">Aktuelle Optionen ohne „Eigenes …“ (wird nach Anbieterwechsel und Laden neu gefragt).</param>
    /// <param name="load">Lädt die Serverliste; Rückgabe = Fehlertext oder null.</param>
    /// <param name="loaded">Nach erfolgreichem Laden (z. B. Stufen-Modelle neu füllen).</param>
    public ModelPickerViewModel(Func<IEnumerable<Choice>> options, Func<string> read, Action<string> write,
        Func<CancellationToken, Task<string?>> load, Action? loaded = null)
    {
        _options = options;
        _load = load;
        _loaded = loaded;
        Choice = new CustomChoiceViewModel(options(), read, v =>
        {
            write(v);
            return null;
        }, "Model_Custom");
    }

    public CustomChoiceViewModel Choice { get; }

    [ObservableProperty] private string _status = "";

    [ObservableProperty] private bool _statusIsError;

    /// <summary>Optionen neu bauen (Anbieterwechsel, neue Serverliste).</summary>
    public void Refresh() => Choice.SetOptions(_options());

    [RelayCommand]
    private async Task Load()
    {
        Status = Loc.Instance["Models_Loading"];
        StatusIsError = false;
        var error = await _load(CancellationToken.None);
        if (error is not null)
        {
            Status = error;
            StatusIsError = true;
            return;
        }
        Refresh();
        _loaded?.Invoke();
        var count = Choice.Items.Count - 2; // ohne Standard und „Eigenes …“
        Status = Loc.Instance.Format("Models_Loaded", Math.Max(0, count));
    }

    /// <summary>
    /// Optionen: Standard-Eintrag, Modelle des Katalogs bzw. Eintrags, dann die der Serverliste, die dort noch fehlen.
    /// </summary>
    public static IEnumerable<Choice> Combine(string defaultLabel, IReadOnlyList<ModelOption> catalog, IReadOnlyList<RemoteModel> server)
    {
        var known = catalog.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return
        [
            new Choice("", defaultLabel),
            .. catalog.Select(m => new Choice(m.Id, m.Label)),
            .. server.Where(m => !known.Contains(m.Id)).Select(m => new Choice(m.Id, Loc.Instance.Format("Model_FromServer", m.Label ?? m.Id))),
        ];
    }
}
