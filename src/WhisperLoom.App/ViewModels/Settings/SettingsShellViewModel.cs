using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;

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
    private static readonly HashSet<string> WindowsOnly = new(StringComparer.Ordinal) { "desktop.insertMode", "desktop.profiles" };

    private readonly Func<SettingsViewModel, IManagedSource> _sourceOf;
    private readonly string _toggleCommand;

    /// <param name="source">Stufen je Pfad; null = aus der Richtlinie (<see cref="PolicyManagedSource"/>).</param>
    /// <param name="toggleCommand">Befehl fürs Desktop-Kürzel unter Linux.</param>
    public SettingsShellViewModel(SettingsViewModel session, Func<SettingsViewModel, IManagedSource>? source = null, string toggleCommand = "whisperloom toggle")
    {
        _sourceOf = source ?? (s => new PolicyManagedSource(s.Policy));
        _toggleCommand = toggleCommand;
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Load(session, "general");
    }

    public SettingsViewModel Session { get; private set; } = null!;

    public ObservableCollection<SettingsPageViewModel> Pages { get; } = [];

    public ManagedContext Managed { get; private set; } = ManagedContext.None;

    [ObservableProperty] private SettingsPageViewModel? _selectedPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _searchText = "";

    public bool IsSearching => SearchText.Trim().Length > 0;

    public ObservableCollection<SearchResult> Results { get; } = [];

    /// <summary>Nach einem Sprung aus der Suche: die Ansicht holt die Zeile mit diesem Pfad ins Bild und fokussiert sie.</summary>
    public event Action<string>? RevealRequested;

    public SettingsPageViewModel Page(string id) => Pages.First(p => p.Id == id);

    /// <summary>Wechselt zur Seite des Treffers und zeigt die Zeile.</summary>
    public void Open(SearchResult result)
    {
        SelectedPage = Page(result.Entry.Page);
        RevealRequested?.Invoke(result.Entry.Path);
    }

    /// <summary>Firmenwert setzen, dann alles vom neuen Stand neu aufbauen (die Arbeitskopie ist danach veraltet).</summary>
    public async Task ResetToDefaultAsync(string path)
    {
        await Session.ResetToDefaultAsync(path, Managed.Source.DefaultOf(path));
        Load(Session.Reopen(), SelectedPage?.Id ?? "general");
        _ = Session.LoadKeysAsync();
    }

    public void Dispose()
    {
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        foreach (var page in Pages)
        {
            page.Dispose();
        }
    }

    partial void OnSearchTextChanged(string value) => RunSearch();

    private void Load(SettingsViewModel session, string pageId)
    {
        foreach (var page in Pages)
        {
            page.Dispose();
        }
        session.AutoSave = true;
        Session = session;
        Managed = new ManagedContext(_sourceOf(session), ResetToDefaultAsync);
        Pages.Clear();
        foreach (var page in CreatePages(session))
        {
            Pages.Add(page);
        }
        string? section = null;
        foreach (var page in Pages)
        {
            var current = SettingsCatalog.SectionOf(page.Id);
            page.SectionTitle = current == section ? null : Loc.Instance["Section_" + current];
            section = current;
        }
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(Managed));
        SelectedPage = Pages.FirstOrDefault(p => p.Id == pageId) ?? Pages[0];
        RunSearch();
    }

    private IEnumerable<SettingsPageViewModel> CreatePages(SettingsViewModel s) =>
    [
        new GeneralPageViewModel(s), new RecognitionPageViewModel(s), new TextPageViewModel(s), new CleanupPageViewModel(s),
        new OutputPageViewModel(s), new HotkeysPageViewModel(s, _toggleCommand), new PrivacyPageViewModel(s), new NetworkPageViewModel(s),
    ];

    /// <summary>
    /// Sucht in Titel, Beschreibung, Gruppe und Seite (aktuelle Sprache) sowie im Pfad; alle Wörter müssen vorkommen.
    /// Feste und auf diesem System nicht verfügbare Einstellungen erscheinen nicht.
    /// </summary>
    private void RunSearch()
    {
        Results.Clear();
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return;
        }
        var loc = Loc.Instance;
        foreach (var entry in SettingsCatalog.Entries)
        {
            if (Managed.Source.LevelOf(entry.Path) == ManagedLevel.Fixed || !Available(entry))
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
    }

    private static bool Available(SettingEntry entry) =>
        OperatingSystem.IsWindows() || (entry.Page != "hotkeys" && !WindowsOnly.Contains(entry.Path));

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RunSearch();
}
