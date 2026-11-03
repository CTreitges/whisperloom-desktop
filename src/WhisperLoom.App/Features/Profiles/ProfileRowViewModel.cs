using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Features.Profiles;

/// <summary>
/// Ein Profil der Liste (K2): Kopf mit Nummer, Schalter „aktiv“, Name, Zusammenfassung der Abweichungen und Pfeilen; Körper mit
/// Name, Programm (auch aus laufenden Programmen), Stufe, Ausgabe, Enter, schwebendem Knopf und „Nur bestimmte Fenster“.
/// Stufe, Ausgabe und Enter halten den Wert; die <see cref="Choice"/> kommt aus den Listen der Liste, deren erster Eintrag
/// „Wie allgemein (zurzeit …)“ den allgemeinen Werten folgt.
/// </summary>
public sealed partial class ProfileRowViewModel : ObservableObject
{
    private readonly ProfilesViewModel _owner;
    private string _stageValue;
    private string _insertValue;

    internal ProfileRowViewModel(ProfilesViewModel owner, AppProfile profile, bool open)
    {
        _owner = owner;
        _name = profile.Name;
        _process = profile.Process;
        _titlePattern = profile.TitlePattern;
        _stageValue = owner.Stages.Any(c => c.Value == profile.Stage) ? profile.Stage : "";
        _insertValue = owner.InsertModes.Any(c => c.Value == profile.InsertMode) ? profile.InsertMode : "";
        _pressEnter = profile.PressEnter; // null = Erben (W2-C0)
        _floatingButton = profile.FloatingButton; // null = wie eingestellt (W2-SEL)
        _enabled = profile.Enabled;
        _isOpen = open;
        _advancedOpen = profile.TitlePattern.Trim().Length > 0;
    }

    public ProfilesViewModel Owner => _owner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(ActiveLabel), nameof(MoveUpLabel), nameof(MoveDownLabel), nameof(DeleteLabel))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(DisplayName), nameof(ActiveLabel), nameof(MoveUpLabel), nameof(MoveDownLabel), nameof(DeleteLabel))]
    private string _process;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleWarning), nameof(HasPriority), nameof(Summary))]
    private string _titlePattern;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Enter), nameof(Summary))]
    private bool? _pressEnter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFloatingButton), nameof(Summary))]
    private bool? _floatingButton;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _enabled;

    /// <summary>Körper aufgeklappt (nicht gespeichert).</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>„Nur bestimmte Fenster“ aufgeklappt; ein Profil mit Titel öffnet aufgeklappt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdvanced))]
    private bool _advancedOpen;

    /// <summary>Liste „Laufende Programme“ offen.</summary>
    [ObservableProperty]
    private bool _pickOpen;

    [ObservableProperty]
    private IReadOnlyList<string> _runningPrograms = [];

    /// <summary>Stelle in der Liste ab 1 (Kopf); setzt die Liste.</summary>
    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private bool _canMoveUp;

    [ObservableProperty]
    private bool _canMoveDown;

    public Choice Stage
    {
        get => Pick(_owner.Stages, _stageValue);
        set => SetValue(ref _stageValue, value?.Value ?? "");
    }

    public Choice InsertMode
    {
        get => Pick(_owner.InsertModes, _insertValue);
        set => SetValue(ref _insertValue, value?.Value ?? "");
    }

    /// <summary>„Enter nach dem Einfügen“: Wie allgemein (null, wird nicht geschrieben) / Enter drücken / Kein Enter.</summary>
    public Choice Enter
    {
        get => _owner.EnterChoices[PressEnter switch { null => 0, true => 1, false => 2 }];
        set => PressEnter = value?.Value switch { "on" => true, "off" => false, _ => null };
    }

    /// <summary>Schalter „Schwebenden Knopf zeigen“: aus = in diesem Programm nie (false), an = wie allgemein (null).</summary>
    public bool ShowFloatingButton
    {
        get => FloatingButton != false;
        set => FloatingButton = value ? null : false;
    }

    public string DisplayName => AppProfiles.DisplayName(new AppProfile { Name = Name, Process = Process }) is { Length: > 0 } n ? n : Loc.Instance["Profile_New"];

    public string ActiveLabel => Loc.Instance.Format("Profile_ActiveName", DisplayName);

    public string MoveUpLabel => Loc.Instance.Format("Profile_Up", DisplayName);

    public string MoveDownLabel => Loc.Instance.Format("Profile_Down", DisplayName);

    public string DeleteLabel => Loc.Instance.Format("Profile_Delete", DisplayName);

    /// <summary>Meldung am Feld „Fenstertitel enthält“, wenn der Ausdruck nicht lesbar ist; leer = in Ordnung.</summary>
    public string TitleWarning => AppProfiles.IsValidPattern(TitlePattern) ? "" : Loc.Instance["Profile_InvalidTitle"];

    /// <summary>Kennzeichen „Vorrang“: ein lesbarer Fenstertitel geht vor Profilen nur mit Programm.</summary>
    public bool HasPriority => TitlePattern.Trim().Length > 0 && TitleWarning.Length == 0;

    public bool ShowAdvanced => AdvancedOpen;

    /// <summary>Zusammenfassung im Kopf: nur, was vom Allgemeinen abweicht (K2).</summary>
    public string Summary
    {
        get
        {
            var loc = Loc.Instance;
            if (Process.Trim().Length == 0)
            {
                return loc["Profile_Sum_NoProgram"];
            }
            var parts = new List<string>();
            if (!Enabled)
            {
                parts.Add(loc["Profile_Sum_Inactive"]);
            }
            parts.Add(Process.Trim());
            var own = parts.Count;
            if (_stageValue.Length > 0)
            {
                parts.Add(loc.Format("Profile_Sum_Stage", Stage.Label));
            }
            if (_insertValue.Length > 0)
            {
                parts.Add(loc.Format("Profile_Sum_Insert", InsertMode.Label));
            }
            if (PressEnter is { } enter)
            {
                parts.Add(loc[enter ? "Profile_EnterOn" : "Profile_Sum_EnterOff"]);
            }
            if (FloatingButton == false && _owner.FloatingButtonOn)
            {
                parts.Add(loc["Profile_Sum_NoButton"]);
            }
            var title = TitlePattern.Trim();
            if (title.Length > 0)
            {
                parts.Add(TitleWarning.Length == 0 ? loc.Format("Profile_Sum_Title", title) : loc["Profile_Sum_BadTitle"]);
            }
            else if (parts.Count == own)
            {
                parts.Add(loc["Profile_Sum_Inherits"]);
            }
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Jede gespeicherte Änderung meldet die Liste (Einstellungsfenster speichert sofort).</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(Process) or nameof(TitlePattern) or nameof(Stage) or nameof(InsertMode)
            or nameof(PressEnter) or nameof(FloatingButton) or nameof(Enabled))
        {
            _owner.RowChanged(this, e.PropertyName);
        }
    }

    /// <summary>Allgemeine Werte geändert: Auswahl aus den neuen Listen holen, Zusammenfassung neu.</summary>
    internal void RefreshDefaults()
    {
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Stage)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(InsertMode)));
        OnPropertyChanged(nameof(Enter));
        OnPropertyChanged(nameof(Summary));
    }

    public AppProfile ToProfile() => new()
    {
        Name = Name.Trim(),
        Process = Process.Trim(),
        TitlePattern = TitlePattern.Trim(),
        Stage = _stageValue,
        InsertMode = _insertValue,
        PressEnter = PressEnter,
        FloatingButton = FloatingButton,
        Enabled = Enabled,
    };

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void MoveUp() => _owner.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.Move(this, 1);

    [RelayCommand]
    private void Delete() => _owner.Delete(this);

    /// <summary>„Aus laufenden Programmen wählen …“: Liste auf/zu; beim Öffnen frisch gelesen.</summary>
    [RelayCommand]
    private void Pick()
    {
        if (!PickOpen)
        {
            RunningPrograms = _owner.ReadRunning();
        }
        PickOpen = !PickOpen;
    }

    [RelayCommand]
    private void PickOne(string? process)
    {
        if (!string.IsNullOrEmpty(process))
        {
            Process = process;
        }
        PickOpen = false;
    }

    private void SetValue(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (field == value)
        {
            return;
        }
        field = value;
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(Summary));
    }

    private static Choice Pick(IReadOnlyList<Choice> choices, string value) =>
        choices.FirstOrDefault(c => c.Value == value) ?? choices[0];
}
