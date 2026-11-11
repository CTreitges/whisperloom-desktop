using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.ViewModels.Settings;

/// <summary>Ein Treffer der Suche: Einstellung, Titel und Ort („Seite › Gruppe“).</summary>
public sealed record SearchResult(SettingEntry Entry, string Title, string Location)
{
    public override string ToString() => Title;
}

/// <summary>
/// Hülle des Einstellungsfensters: Seiten der Seitenleiste, gewählte Seite, Suche über alle Einstellungen und der
/// Fest/Vorgabe-Kontext. Speichert nichts selbst – die Seiten schreiben über die gemeinsame Arbeitskopie (AutoSave).
/// </summary>
public sealed partial class SettingsShellViewModel : ObservableObject, IDisposable
{
    /// <summary>Zeilen, die es unter Linux nicht gibt (globale Tastenkürzel, Markierung, Einfügen, App-Profile).</summary>
    private static readonly HashSet<string> WindowsOnly = new(StringComparer.Ordinal)
    {
        "desktop.hotkeysEnabled", "desktop.hotkeyHold", "desktop.hotkeyToggle", "desktop.hotkeyStage", "desktop.hotkeyReinsert",
        "selection.hotkeyCombo", "selection.floatingButton", "selection.ctrlRightClick",
        "desktop.insertMode", "output.alwaysCopy", "output.pressEnter", "desktop.profiles",
    };

    private readonly Func<SettingsViewModel, IManagedSource> _sourceOf;
    private readonly string _toggleCommand;

    /// <param name="source">Stufen je Pfad; null = aus der Richtlinie (<see cref="PolicyManagedSource"/>).</param>
    /// <param name="toggleCommand">Befehl fürs Desktop-Kürzel unter Linux.</param>
    public SettingsShellViewModel(SettingsViewModel session, Func<SettingsViewModel, IManagedSource>? source = null, string toggleCommand = "whisperloom toggle")
    {
        _sourceOf = source ?? (s => new PolicyManagedSource(s.Policy, () => s.Current));
        _toggleCommand = toggleCommand;
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Load(session, LastPage);
    }

    /// <summary>Zuletzt gezeigte Seite (HIG settings.md: „Restore the most recently viewed pane“), für die Laufzeit der App.</summary>
    internal static string LastPage { get; set; } = "dictation";

    public SettingsViewModel Session { get; private set; } = null!;

    public ObservableCollection<SettingsPageViewModel> Pages { get; } = [];

    public ManagedContext Managed { get; private set; } = ManagedContext.None;

    [ObservableProperty] private SettingsPageViewModel? _selectedPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _searchText = "";

    public bool IsSearching => SearchText.Trim().Length > 0;

    /// <summary>
    /// Rückmeldung nach dem Sofort-Speichern (TP-WSETTINGS): kurz „Gespeichert“, danach leer; ein Fehler bleibt stehen, bis
    /// wieder gespeichert wurde. Leer = nichts zeigen (die Ansicht sagt den Text als Live-Region an, sobald er erscheint).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    private string _saveNotice = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    private bool _saveFailed;

    /// <summary>„Gespeichert“ steht gerade da (Häkchen sichtbar).</summary>
    public bool IsSaved => !SaveFailed && SaveNotice.Length > 0;

    /// <summary>So lange steht „Gespeichert“ da (Tests kürzen es).</summary>
    internal TimeSpan SavedVisibleFor { get; set; } = TimeSpan.FromSeconds(2.5);

    /// <summary>Zeitquelle für <see cref="SavedVisibleFor"/>; Tests setzen eine steuerbare Uhr (TS-WFLAKY).</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    private int _saveVersion;

    public ObservableCollection<SearchResult> Results { get; } = [];

    /// <summary>Statuszeile über den Treffern: Anzahl bzw. nächster Schritt, wenn nichts passt (B33).</summary>
    public string ResultSummary => Results.Count > 0
        ? Loc.Instance.Format("Search_Count", Results.Count, SearchText.Trim())
        : Loc.Instance.Format("Search_NoneFor", SearchText.Trim());

    /// <summary>Nach einem Sprung aus der Suche: die Ansicht holt die Zeile mit diesem Pfad ins Bild und fokussiert sie.</summary>
    public event Action<string>? RevealRequested;

    public SettingsPageViewModel Page(string id) => Pages.First(p => p.Id == id);

    /// <summary>Wechselt zur Seite des Treffers, beendet die Suche (die Treffer stehen im Inhaltsbereich) und zeigt die Zeile.</summary>
    [RelayCommand]
    public void Open(SearchResult result)
    {
        SelectedPage = Page(result.Entry.Page);
        SearchText = "";
        RevealRequested?.Invoke(result.Entry.Path);
    }

    /// <summary>Firmenwert setzen, dann alles vom neuen Stand neu aufbauen (die Arbeitskopie ist danach veraltet).</summary>
    public async Task ResetToDefaultAsync(string path)
    {
        await Session.ResetToDefaultAsync(path, Managed.Source);
        Reload();
    }

    /// <summary>
    /// Neue Arbeitskopie vom aktuellen Stand der App (nach Zurücksetzen bzw. Import), gleiche Seite. Die Hülle hängt sich
    /// dabei selbst an den Import der neuen Session – ein Abo von außen hinge sonst an der alten (Review W2-UI1 R2).
    /// </summary>
    private void Reload()
    {
        Load(Session.Reopen(), SelectedPage?.Id ?? "dictation");
        _ = Session.LoadKeysAsync();
        _ = Session.Local.RefreshOllamaCommand.ExecuteAsync(null);
    }

    private void OnImported() => Avalonia.Threading.Dispatcher.UIThread.Post(Reload);

    /// <summary>
    /// Jedes Sofort-Speichern setzt <see cref="SettingsViewModel.SaveStatus"/>: „Gespeichert“ zeigen und später ausblenden, sonst
    /// den Fehler. Danach leert die Hülle den Status wieder, damit das nächste „Gespeichert“ erneut gemeldet wird (gleicher Wert
    /// löst sonst kein PropertyChanged aus).
    /// </summary>
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.SaveStatus) || sender is not SettingsViewModel session || session.SaveStatus.Length == 0)
        {
            return;
        }
        var status = session.SaveStatus;
        session.SaveStatus = "";
        var version = ++_saveVersion;
        var saved = status == Loc.Instance["Settings_Saved"];
        SaveFailed = !saved;
        SaveNotice = saved ? status : FailureText(session, status);
        if (saved)
        {
            _ = HideSavedAsync(version);
        }
    }

    /// <summary>
    /// Die Arbeitskopie meldet bei jedem Schreibfehler „Gespeichert, aber der Verlauf ließ sich nicht löschen“. Das stimmt nur, wenn
    /// der Verlauf tatsächlich ausgeschaltet wurde (dann gilt der neue Stand). Ist er beim aktuellen Stand der App noch an, wurde
    /// gar nichts gespeichert, und die Hülle sagt das (TP-WSETTINGS).
    /// </summary>
    private static string FailureText(SettingsViewModel session, string status) =>
        status == Loc.Instance["Settings_HistoryClearError"] && session.Current.History.Enabled ? Loc.Instance["Settings.SaveFailed"] : status;

    private async Task HideSavedAsync(int version)
    {
        // Doku: learn.microsoft.com/dotnet/api/system.threading.tasks.task.delay (Overload TimeSpan, TimeProvider; abgerufen 2026-11-11)
        await Task.Delay(SavedVisibleFor, Time);
        if (version == _saveVersion)
        {
            SaveNotice = "";
        }
    }

    public void Dispose()
    {
        Session.PropertyChanged -= OnSessionChanged;
        Session.Sync.Applied -= OnImported;
        if (Session.Company is { } company)
        {
            company.Changed -= OnManagementChanged;
        }
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        foreach (var page in Pages)
        {
            page.Dispose();
        }
    }

    partial void OnSearchTextChanged(string value) => RunSearch();

    partial void OnSelectedPageChanged(SettingsPageViewModel? value)
    {
        if (value is not null)
        {
            LastPage = value.Id;
        }
    }

    private void Load(SettingsViewModel session, string pageId)
    {
        foreach (var page in Pages)
        {
            page.Dispose();
        }
        if (Session is not null)
        {
            Session.Sync.Applied -= OnImported;
            Session.PropertyChanged -= OnSessionChanged;
            if (Session.Company is { } previous)
            {
                previous.Changed -= OnManagementChanged;
            }
        }
        session.AutoSave = true;
        session.Sync.Applied += OnImported;
        session.PropertyChanged += OnSessionChanged;
        if (session.Company is { } company)
        {
            company.Changed += OnManagementChanged;
        }
        Session = session;
        Managed = new ManagedContext(_sourceOf(session), ResetToDefaultAsync);
        Pages.Clear();
        foreach (var page in CreatePages(session))
        {
            page.Navigate = id => SelectedPage = Page(id);
            Pages.Add(page);
        }
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(Managed));
        SelectedPage = Pages.FirstOrDefault(p => p.Id == pageId) ?? Pages[0];
        RunSearch();
    }

    /// <summary>Reihenfolge wie <see cref="SettingsCatalog.Pages"/>.</summary>
    private IEnumerable<SettingsPageViewModel> CreatePages(SettingsViewModel s) =>
    [
        new DictationPageViewModel(s, _toggleCommand), new RecognitionPageViewModel(s), new TextPageViewModel(s), new OutputPageViewModel(s),
        new ConnectionsPageViewModel(s), new PrivacyPageViewModel(s), new SystemPageViewModel(s), new CompanyPageViewModel(s),
    ];

    /// <summary>
    /// Abgleich bzw. Verlassen der Firma hat die Richtlinie getauscht (W2-ENR): neu aufbauen, damit Fest sofort verschwindet
    /// bzw. wieder erscheint. Ein Abgleich ohne Änderung (304) lässt die Seite in Ruhe.
    /// </summary>
    private void OnManagementChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (!ReferenceEquals(Session.Policy, Session.LivePolicy))
        {
            Reload();
        }
    });

    /// <summary>
    /// Sucht in Titel, Beschreibung, Gruppe und Seite (aktuelle Sprache) sowie im Pfad; alle Wörter müssen vorkommen.
    /// Feste und auf diesem System nicht verfügbare Einstellungen erscheinen nicht.
    /// </summary>
    private void RunSearch()
    {
        Results.Clear();
        OnPropertyChanged(nameof(ResultSummary));
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return;
        }
        var loc = Loc.Instance;
        foreach (var entry in SettingsCatalog.Entries)
        {
            if (Managed.Source.StateOf(entry.Path) == ManagedState.Fixed || !Available(entry))
            {
                continue;
            }
            var title = loc[entry.TitleKey];
            var location = $"{loc["Page_" + entry.Page]} › {loc[entry.GroupKey]}";
            var haystack = string.Join(' ', title, entry.DescriptionKey is { } d ? loc[d] : "", location, entry.Path);
            if (words.All(w => haystack.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            {
                Results.Add(new SearchResult(entry, title, location));
            }
        }
        OnPropertyChanged(nameof(ResultSummary));
    }

    private static bool Available(SettingEntry entry) => OperatingSystem.IsWindows() || !WindowsOnly.Contains(entry.Path);

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RunSearch();
}
