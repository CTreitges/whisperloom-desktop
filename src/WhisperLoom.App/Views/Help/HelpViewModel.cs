using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Views.Help;

/// <summary>Ein Treffer der Suche: Abschnitt und die erste passende Zeile als Auszug.</summary>
public sealed record HelpSearchResult(HelpSectionViewModel Section, string Title, string Snippet)
{
    public override string ToString() => Title;
}

/// <summary>
/// Hilfe-Fenster: Abschnitte der Seitenleiste (erst die Übersicht, dann <see cref="HelpCatalog.Topics"/>), gewählter
/// Abschnitt und Suche über Titel, Einleitung, Text, Bild- und Screenshot-Beschreibungen der aktuellen Sprache.
/// </summary>
public sealed partial class HelpViewModel : ObservableObject, IDisposable
{
    public HelpViewModel(IReadOnlyList<HelpShot>? shots = null)
    {
        var all = shots ?? HelpShots.Load();
        Sections.Add(new HelpSectionViewModel(null, []));
        foreach (var topic in HelpCatalog.Topics)
        {
            Sections.Add(new HelpSectionViewModel(topic, [.. all.Where(s => s.Section == topic.Id)]));
        }
        Overview = Sections[0];
        Overview.Tiles = [.. Sections.Skip(1)];
        UpdateGroupTitles();
        _selectedSection = Overview;
        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    public ObservableCollection<HelpSectionViewModel> Sections { get; } = [];

    public HelpSectionViewModel Overview { get; }

    [ObservableProperty] private HelpSectionViewModel? _selectedSection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _searchText = "";

    public bool IsSearching => SearchText.Trim().Length > 0;

    public ObservableCollection<HelpSearchResult> Results { get; } = [];

    public HelpSectionViewModel Section(string id) => Sections.FirstOrDefault(s => s.Id == id) ?? Overview;

    /// <summary>Zeigt den Abschnitt (unbekannt → Übersicht) und beendet eine laufende Suche.</summary>
    public void Open(string? id)
    {
        SearchText = "";
        SelectedSection = Section(id ?? HelpCatalog.Overview);
    }

    public void Dispose()
    {
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
        foreach (var section in Sections)
        {
            section.Dispose();
        }
    }

    partial void OnSelectedSectionChanged(HelpSectionViewModel? value)
    {
        // Die Seitenleiste darf nie „nichts gewählt“ zeigen.
        if (value is null)
        {
            SelectedSection = Overview;
        }
    }

    partial void OnSearchTextChanged(string value) => RunSearch();

    /// <summary>Alle Wörter müssen im Abschnitt vorkommen (Groß/klein egal); Auszug = erste Zeile mit dem ersten Wort.</summary>
    private void RunSearch()
    {
        Results.Clear();
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return;
        }
        foreach (var section in Sections.Skip(1))
        {
            var lines = section.SearchLines();
            var haystack = string.Join('\n', lines);
            if (!words.All(w => haystack.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }
            var snippet = lines.Skip(1).FirstOrDefault(l => l.Contains(words[0], StringComparison.CurrentCultureIgnoreCase)) ?? section.Intro;
            Results.Add(new HelpSearchResult(section, section.Title, Shorten(snippet)));
        }
    }

    private static string Shorten(string text)
    {
        var plain = text.TrimStart('#', '•', ' ');
        return plain.Length <= 90 ? plain : plain[..87].TrimEnd() + " …";
    }

    private void UpdateGroupTitles()
    {
        string? group = null;
        foreach (var section in Sections.Skip(1))
        {
            section.GroupTitle = section.Group == group ? null : Loc.Instance["HelpGroup_" + section.Group];
            group = section.Group;
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateGroupTitles();
        RunSearch();
    }
}
