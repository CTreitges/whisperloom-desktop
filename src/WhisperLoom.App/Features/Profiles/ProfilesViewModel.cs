using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Features.Profiles;

/// <summary>Eine Zeile der Profil-Liste in den Einstellungen.</summary>
public sealed partial class ProfileRowViewModel : ObservableObject
{
    private readonly ProfilesViewModel _owner;

    internal ProfileRowViewModel(ProfilesViewModel owner, AppProfile profile)
    {
        _owner = owner;
        _name = profile.Name;
        _process = profile.Process;
        _titlePattern = profile.TitlePattern;
        _stage = Pick(owner.Stages, profile.Stage);
        _insertMode = Pick(owner.InsertModes, profile.InsertMode);
        _pressEnter = profile.PressEnter;
        _enabled = profile.Enabled;
    }

    public ProfilesViewModel Owner => _owner;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _process;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleWarning))]
    private string _titlePattern;

    [ObservableProperty]
    private Choice _stage;

    [ObservableProperty]
    private Choice _insertMode;

    [ObservableProperty]
    private bool _pressEnter;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>Hinweis bei ungültigem Regex; leer = gültig.</summary>
    public string TitleWarning => AppProfiles.IsValidPattern(TitlePattern) ? "" : "⚠ " + Loc.Instance["Profile_InvalidTitle"];

    /// <summary>Jede Änderung einer Zeile meldet die Liste (Einstellungsfenster speichert sofort).</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(TitleWarning))
        {
            _owner.Changed?.Invoke();
        }
    }

    public AppProfile ToProfile() => new()
    {
        Name = Name.Trim(),
        Process = Process.Trim(),
        TitlePattern = TitlePattern.Trim(),
        Stage = Stage.Value,
        InsertMode = InsertMode.Value,
        PressEnter = PressEnter,
        Enabled = Enabled,
    };

    [RelayCommand]
    private void Remove() => _owner.Rows.Remove(this);

    private static Choice Pick(IReadOnlyList<Choice> choices, string value) =>
        choices.FirstOrDefault(c => c.Value == value) ?? choices[0];
}

/// <summary>
/// Einstellungsseite „App-Profile“ (nur Windows, WLD-P5b): Liste mit Prozess, Titel-Regex, Stufe, Ausgabe, Enter; dazu die
/// Vorschläge Terminal → Prompt und Outlook → Verschönern. Schreibt in <see cref="DesktopSettings.Profiles"/>.
/// </summary>
public sealed class ProfilesViewModel
{
    private readonly DesktopSettings _desktop;

    public ProfilesViewModel(DesktopSettings desktop)
    {
        _desktop = desktop;
        var loc = Loc.Instance;
        Stages = [new Choice("", loc["Profile_Default"]), .. RefineModes.SettingsFor(true).Select(m => new Choice(m.Key(), loc["Stage_" + m.Key()]))];
        InsertModes = [new Choice("", loc["Profile_Default"]), .. new[] { "paste", "type", "clipboard" }.Select(m => new Choice(m, loc["Insert_" + m]))];
        Rows = [.. desktop.Profiles.Select(p => new ProfileRowViewModel(this, p))];
        AddCommand = new RelayCommand(() => Rows.Add(new ProfileRowViewModel(this, new AppProfile())));
        AddSuggestionsCommand = new RelayCommand(AddSuggestions);
        Rows.CollectionChanged += (_, _) => Changed?.Invoke();
    }

    /// <summary>Nach jeder Änderung (Einstellungsfenster speichert sofort).</summary>
    public Action? Changed { get; init; }

    public bool Available => OperatingSystem.IsWindows();

    public IReadOnlyList<Choice> Stages { get; }

    public IReadOnlyList<Choice> InsertModes { get; }

    public ObservableCollection<ProfileRowViewModel> Rows { get; }

    public IRelayCommand AddCommand { get; }

    public IRelayCommand AddSuggestionsCommand { get; }

    /// <summary>
    /// Übernimmt die Zeilen (ohne leere) in die Desktop-Einstellungen (vor dem Speichern). Solange eine Zeile einen
    /// ungültigen Fenstertitel hat, bleibt die gespeicherte Liste unverändert (Fehler steht an der Zeile).
    /// </summary>
    public void Apply()
    {
        if (Rows.Any(r => r.TitleWarning.Length > 0))
        {
            return;
        }
        _desktop.Profiles = [.. Rows.Select(r => r.ToProfile()).Where(p => p.Process.Length > 0)];
    }

    /// <summary>Fügt die Vorschläge hinzu, die es (nach Prozess) noch nicht gibt.</summary>
    private void AddSuggestions()
    {
        foreach (var suggestion in AppProfiles.Suggestions())
        {
            var process = AppProfiles.NormalizeProcess(suggestion.Process);
            if (Rows.All(r => AppProfiles.NormalizeProcess(r.Process) != process))
            {
                Rows.Add(new ProfileRowViewModel(this, suggestion));
            }
        }
    }
}
