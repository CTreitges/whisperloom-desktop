using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Features.Profiles;

/// <summary>Allgemeine Werte, die ein Profil erbt (für „Wie allgemein (zurzeit …)“ und die Zusammenfassung).</summary>
public sealed record ProfileDefaults(string Stage, string InsertMode, bool PressEnter, bool FloatingButton);

/// <summary>Wohin die Ansicht nach einer Aktion den Fokus setzt (K2: Fokus bleibt am Profil bzw. springt zur Meldung).</summary>
public enum ProfileFocus
{
    Header,
    Name,
    MoveUp,
    MoveDown,
    Status,
}

/// <summary>Vorschlag für ein Programm ohne Profil („Terminal-Profil anlegen“).</summary>
public sealed class ProfileSuggestion(AppProfile profile, string text, string actionText, IRelayCommand add)
{
    public string Process => profile.Process;

    public string Text => text;

    public string ActionText => actionText;

    public IRelayCommand AddCommand => add;
}

/// <summary>
/// App-Profile auf der Seite „Ausgabe“ (nur Windows, WLD-P5b; Aufbau nach K2): nummerierte Liste mit Reihenfolge per Pfeil,
/// Löschen mit Rückgängig in der Statuszeile, Vorschläge nur für fehlende Programme, ein Hauptknopf „Profil hinzufügen“.
/// Schreibt in <see cref="DesktopSettings.Profiles"/>; die Reihenfolge ist der Vorrang unter gleich genauen Profilen.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly DesktopSettings _desktop;
    private readonly Func<ProfileDefaults> _defaults;
    private readonly Func<IReadOnlyList<string>> _running;
    private (ProfileRowViewModel Row, int Index)? _undo;

    public ProfilesViewModel(DesktopSettings desktop, Func<ProfileDefaults>? defaults = null, Func<IReadOnlyList<string>>? running = null)
    {
        _desktop = desktop;
        _defaults = defaults ?? (() => new ProfileDefaults("", desktop.InsertMode, false, false));
        _running = running ?? RunningPrograms.List;
        BuildChoices();
        Rows = [.. desktop.Profiles.Select(p => new ProfileRowViewModel(this, p, open: false))];
        Rows.CollectionChanged += (_, _) =>
        {
            Renumber();
            RefreshSuggestions();
            OnPropertyChanged(nameof(IsEmpty));
            Changed?.Invoke();
        };
        Renumber();
        RefreshSuggestions();
    }

    /// <summary>Nach jeder Änderung (Einstellungsfenster speichert sofort).</summary>
    public Action? Changed { get; init; }

    /// <summary>Die Ansicht setzt den Fokus (Zeile null = Statuszeile).</summary>
    public event Action<ProfileRowViewModel?, ProfileFocus>? FocusRequested;

    public bool Available => OperatingSystem.IsWindows();

    /// <summary>Stufen: „Wie allgemein (zurzeit …)“, dann alle Stufen.</summary>
    [ObservableProperty]
    private IReadOnlyList<Choice> _stages = [];

    /// <summary>Ausgabe: „Wie allgemein (zurzeit …)“, Einfügen, Direkt tippen, Nur Zwischenablage.</summary>
    [ObservableProperty]
    private IReadOnlyList<Choice> _insertModes = [];

    /// <summary>Enter nach dem Einfügen: Wie allgemein (erbt output.pressEnter), Enter drücken, Kein Enter.</summary>
    [ObservableProperty]
    private IReadOnlyList<Choice> _enterChoices = [];

    /// <summary>Schwebender Knopf allgemein an: nur dann zeigt ein Profil seinen Schalter.</summary>
    [ObservableProperty]
    private bool _floatingButtonOn;

    /// <summary>Statuszeile unter der Liste („Profil ‚X‘ gelöscht.“ mit Rückgängig, „… angelegt.“).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    [ObservableProperty]
    private bool _canUndo;

    public bool HasStatus => Status.Length > 0;

    public ObservableCollection<ProfileRowViewModel> Rows { get; }

    public ObservableCollection<ProfileSuggestion> Suggestions { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// Übernimmt die Zeilen (ohne leere) in die Desktop-Einstellungen (vor dem Speichern). Solange eine Zeile einen
    /// unlesbaren Fenstertitel hat, bleibt die gespeicherte Liste unverändert (Meldung steht am Feld).
    /// </summary>
    public void Apply()
    {
        if (Rows.Any(r => r.TitleWarning.Length > 0))
        {
            return;
        }
        _desktop.Profiles = [.. Rows.Select(r => r.ToProfile()).Where(p => p.Process.Length > 0)];
    }

    /// <summary>Allgemeine Stufe, Ausgabe, Enter oder schwebender Knopf geändert: Listen und Zeilen neu.</summary>
    public void RefreshDefaults()
    {
        BuildChoices();
        foreach (var row in Rows)
        {
            row.RefreshDefaults();
        }
    }

    [RelayCommand]
    private void Add()
    {
        var row = new ProfileRowViewModel(this, new AppProfile(), open: true);
        Status = "";
        _undo = null;
        CanUndo = false;
        Rows.Add(row);
        FocusRequested?.Invoke(row, ProfileFocus.Name);
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo is not { } undo)
        {
            return;
        }
        _undo = null;
        CanUndo = false;
        Rows.Insert(Math.Min(undo.Index, Rows.Count), undo.Row);
        Status = Loc.Instance.Format("Profile_Restored", undo.Row.DisplayName);
        FocusRequested?.Invoke(undo.Row, ProfileFocus.Header);
    }

    internal void Move(ProfileRowViewModel row, int delta)
    {
        var from = Rows.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Rows.Count)
        {
            return;
        }
        Rows.Move(from, to);
        // Fokus bleibt am Profil: auf demselben Pfeil, sonst (am Rand) auf dem anderen.
        var same = delta < 0 ? row.CanMoveUp : row.CanMoveDown;
        FocusRequested?.Invoke(row, (delta < 0) == same ? ProfileFocus.MoveUp : ProfileFocus.MoveDown);
    }

    internal void Delete(ProfileRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }
        Rows.RemoveAt(index);
        _undo = (row, index);
        CanUndo = true;
        Status = Loc.Instance.Format("Profile_Deleted", row.DisplayName);
        FocusRequested?.Invoke(null, ProfileFocus.Status);
    }

    internal IReadOnlyList<string> ReadRunning()
    {
        try
        {
            return _running();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Eine Zeile hat sich geändert: speichern; ein neues Programm ändert die Vorschläge.</summary>
    internal void RowChanged(ProfileRowViewModel row, string? property)
    {
        if (property == nameof(ProfileRowViewModel.Process))
        {
            RefreshSuggestions();
        }
        Changed?.Invoke();
    }

    private void AddSuggestion(AppProfile suggestion)
    {
        var row = new ProfileRowViewModel(this, suggestion, open: false);
        Rows.Add(row);
        Status = Loc.Instance.Format("Profile_Created", row.DisplayName);
        CanUndo = false;
        _undo = null;
        FocusRequested?.Invoke(row, ProfileFocus.Header);
    }

    private void Renumber()
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].Number = i + 1;
            Rows[i].CanMoveUp = i > 0;
            Rows[i].CanMoveDown = i < Rows.Count - 1;
        }
    }

    private void RefreshSuggestions()
    {
        var loc = Loc.Instance;
        var have = Rows.Select(r => AppProfiles.NormalizeProcess(r.Process)).ToHashSet();
        Suggestions.Clear();
        foreach (var s in AppProfiles.Suggestions().Where(s => !have.Contains(AppProfiles.NormalizeProcess(s.Process))))
        {
            Suggestions.Add(new ProfileSuggestion(s,
                loc.Format("Profile_Suggestion", s.Name, s.Process, loc["Stage_" + s.Stage]),
                loc.Format("Profile_SuggestionAdd", s.Name),
                new RelayCommand(() => AddSuggestion(s))));
        }
    }

    private void BuildChoices()
    {
        var loc = Loc.Instance;
        var d = _defaults();
        FloatingButtonOn = d.FloatingButton;
        string Inherit(string current) => loc.Format("Profile_Inherit", current);
        var generalStage = RefineModes.SettingsFor(true).Select(m => m.Key()).Contains(d.Stage) ? loc["Stage_" + d.Stage] : loc["Stage_off"];
        Stages = [new Choice("", Inherit(generalStage)), .. RefineModes.SettingsFor(true).Select(m => new Choice(m.Key(), loc["Stage_" + m.Key()]))];
        var modes = new[] { "paste", "type", "clipboard" };
        InsertModes = [new Choice("", Inherit(loc["Insert_" + (modes.Contains(d.InsertMode) ? d.InsertMode : "paste")])), .. modes.Select(m => new Choice(m, loc["Insert_" + m]))];
        EnterChoices = [new Choice("", Inherit(loc[d.PressEnter ? "Profile_EnterOn" : "Profile_Sum_EnterOff"])), new Choice("on", loc["Profile_EnterOn"]), new Choice("off", loc["Profile_EnterOff"])];
    }
}
