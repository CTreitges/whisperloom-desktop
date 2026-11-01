using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Views.Help;

/// <summary>Zeile des Abschnittstexts: Zwischenüberschrift („## “), Aufzählung („• “) oder Absatz.</summary>
public sealed record HelpLine(string Text, bool IsHeading, bool IsBullet)
{
    public bool IsParagraph => !IsHeading && !IsBullet;
}

/// <summary>Markierung im Screenshot: Ziffer und Erklärung.</summary>
public sealed record HelpLegend(int Number, string Text);

/// <summary>Screenshot mit Beschreibung (zugleich Alternativtext) und Legende der Ziffern.</summary>
public sealed record HelpShotView(HelpShot Shot, string Caption, IReadOnlyList<HelpLegend> Legend);

/// <summary>Link mit Text der aktuellen Sprache.</summary>
public sealed record HelpLinkView(string Label, string Url);

/// <summary>
/// Ein Abschnitt im Hilfe-Fenster (bzw. die Übersicht, wenn <see cref="Topic"/> null ist). Texte kommen bei jedem Lesen
/// aus <see cref="Loc"/>; ein Sprachwechsel meldet alle Eigenschaften neu.
/// </summary>
public sealed partial class HelpSectionViewModel : ObservableObject, IDisposable
{
    public HelpSectionViewModel(HelpTopic? topic, IReadOnlyList<HelpShot> shots)
    {
        Topic = topic;
        ShotSpecs = shots;
        IconGeometry = HelpIcons.Resolve(topic?.Icon ?? HelpIcons.Help);
        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    public HelpTopic? Topic { get; }

    public IReadOnlyList<HelpShot> ShotSpecs { get; }

    public string Id => Topic?.Id ?? HelpCatalog.Overview;

    public bool IsOverview => Topic is null;

    public string Group => Topic?.Group ?? "";

    /// <summary>Überschrift der Gruppe, wenn dieser Abschnitt sie in der Seitenleiste eröffnet; sonst null.</summary>
    [ObservableProperty] private string? _groupTitle;

    /// <summary>Nur in der Übersicht: Kacheln aller Abschnitte.</summary>
    public IReadOnlyList<HelpSectionViewModel> Tiles { get; internal set; } = [];

    public Geometry? IconGeometry { get; }

    public string Tile => "Loom.Tile." + (Topic?.Tile ?? "Teal");

    public string Title => Loc.Instance[Topic?.TitleKey ?? "Help_Title"];

    public string Intro => Loc.Instance[Topic?.IntroKey ?? "Help_Overview_Intro"];

    public string ImageAlt => Topic is null ? "" : Loc.Instance[Topic.ImageAltKey];

    public string? Illustration => Topic?.Illustration;

    public IReadOnlyList<HelpLine> Lines => Topic is null ? [] : Parse(Loc.Instance[Topic.BodyKey]);

    public IReadOnlyList<HelpShotView> Shots =>
    [
        .. ShotSpecs.Select(s => new HelpShotView(s, Loc.Instance[s.CaptionKey],
            [.. s.Markers.Select((_, i) => new HelpLegend(i + 1, Loc.Instance[s.LegendKey(i + 1)]))])),
    ];

    public bool HasShots => ShotSpecs.Count > 0;

    public IReadOnlyList<HelpLinkView> Links => Topic is null ? [] : [.. Topic.Links.Select(l => new HelpLinkView(Loc.Instance[l.LabelKey], l.Url))];

    public bool HasLinks => Topic?.Links.Length > 0;

    /// <summary>Texte für die Suche: Titel, Einleitung, Zeilen, Bild- und Screenshot-Texte.</summary>
    public IReadOnlyList<string> SearchLines()
    {
        var lines = new List<string> { Title, Intro };
        lines.AddRange(Lines.Select(l => l.Text));
        lines.Add(ImageAlt);
        foreach (var shot in Shots)
        {
            lines.Add(shot.Caption);
            lines.AddRange(shot.Legend.Select(l => l.Text));
        }
        return lines;
    }

    public static IReadOnlyList<HelpLine> Parse(string body) =>
    [
        .. body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line =>
            line.StartsWith("## ", StringComparison.Ordinal) ? new HelpLine(line[3..], true, false)
            : line.StartsWith("• ", StringComparison.Ordinal) ? new HelpLine(line[2..], false, true)
            : new HelpLine(line, false, false)),
    ];

    public void Dispose() => Loc.Instance.PropertyChanged -= OnLanguageChanged;

    public override string ToString() => Title;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(string.Empty);
}
