using WhisperLoom.App.Setup;

namespace WhisperLoom.App.Views.Help;

/// <summary>Link im Abschnitt (Doku im Repository); Text über <see cref="LabelKey"/>.</summary>
public sealed record HelpLink(string LabelKey, string Url);

/// <summary>
/// Ein Abschnitt der Hilfe. Texte (DE/EN in <c>Localization/*.json</c>): <c>Help_&lt;Id&gt;_Title</c>, <c>_Intro</c> (unter der
/// Illustration), <c>_Body</c> (Zeilen; „## “ = Zwischenüberschrift, „• “ = Aufzählung) und die Bildbeschreibung
/// <c>HelpImg_&lt;Id&gt;</c>. Die Illustration heißt wie der Abschnitt (<c>tools/gen_help_illustrations.py</c>).
/// </summary>
public sealed record HelpTopic(string Id, string Group, string Icon, string Tile, params HelpLink[] Links)
{
    public string TitleKey => $"Help_{Id}_Title";

    public string IntroKey => $"Help_{Id}_Intro";

    public string BodyKey => $"Help_{Id}_Body";

    public string ImageAltKey => $"HelpImg_{Id}";

    public string Illustration => $"avares://WhisperLoom/Assets/Help/illustrations/{Id}.svg";
}

/// <summary>
/// Inhalt der Hilfe (Vorbild Android <c>ui/settings/HelpScreen.kt</c>): Abschnitte in Reihenfolge der Seitenleiste, Gruppen,
/// und welcher Abschnitt zu welcher Einstellungsseite bzw. welchem Schritt des Assistenten gehört („?“ und F1).
/// <c>HelpCatalogTest</c> prüft Texte, Bilder und dass jede Seite aus <see cref="ViewModels.Settings.SettingsCatalog.Pages"/> hier steht.
/// </summary>
public static class HelpCatalog
{
    /// <summary>Startseite des Fensters: Kacheln aller Abschnitte.</summary>
    public const string Overview = "overview";

    private const string Repo = "https://github.com/CTreitges/whisperloom-desktop/blob/main/";

    public static readonly HelpLink LinuxShortcuts = new("HelpLink_LinuxShortcuts", Repo + "docs/linux-shortcuts.md");
    public static readonly HelpLink LocalModels = new("HelpLink_LocalModels", Repo + "docs/local-models.md");
    public static readonly HelpLink ServerReadme = new("HelpLink_ServerReadme", Repo + "server/README.md");
    public static readonly HelpLink Provisioning = new("HelpLink_Provisioning", Repo + "docs/provisioning.md");
    public static readonly HelpLink Proxy = new("HelpLink_Proxy", Repo + "docs/proxy.md");

    public static readonly IReadOnlyList<HelpTopic> Topics =
    [
        new("start", "basics", "Loom.Icon.Info", "Teal"),
        new("dictation", "basics", "Loom.Icon.Recognition", "Teal", LinuxShortcuts),
        new("stages", "basics", "Loom.Icon.Text", "Violet"),
        new("hotkeys", "basics", "Loom.Icon.Hotkeys", "Gray", LinuxShortcuts),
        new("selection", "features", HelpIcons.Selection, "Violet", LinuxShortcuts),
        new("history", "features", HelpIcons.History, "Blue"),
        new("output", "features", "Loom.Icon.Output", "Blue"),
        new("recognition", "sources", "Loom.Icon.Recognition", "Teal"),
        new("providers", "sources", "Loom.Icon.Providers", "Green", LocalModels),
        new("company", "sources", HelpIcons.Company, "Blue"),
        new("network", "sources", "Loom.Icon.Network", "Gray", Proxy),
        new("privacy", "more", "Loom.Icon.Privacy", "Green"),
        new("trouble", "more", HelpIcons.Trouble, "Amber"),
        new("it", "more", "Loom.Icon.Providers", "Gray", ServerReadme, Provisioning, Proxy),
    ];

    /// <summary>Einstellungsseite (Id aus dem SettingsCatalog) → Abschnitt.</summary>
    public static readonly IReadOnlyDictionary<string, string> PageTopics = new Dictionary<string, string>
    {
        ["general"] = "start",
        ["recognition"] = "recognition",
        ["text"] = "stages",
        ["cleanup"] = "stages",
        ["providers"] = "providers",
        ["localmodels"] = "providers",
        ["output"] = "output",
        ["hotkeys"] = "hotkeys",
        ["privacy"] = "privacy",
        ["network"] = "network",
        ["company"] = "company",
    };

    public static HelpTopic? Find(string id) => Topics.FirstOrDefault(t => t.Id == id);

    /// <summary>Abschnitt zur Einstellungsseite; unbekannte Seite → Übersicht.</summary>
    public static string TopicForPage(string? page) =>
        page is not null && PageTopics.TryGetValue(page, out var topic) ? topic : Overview;

    /// <summary>Abschnitt zum Schritt des Einrichtungs-Assistenten.</summary>
    public static string TopicForSetup(SetupStep step) => step switch
    {
        SetupStep.Company => "company",
        SetupStep.Recognition => "recognition",
        SetupStep.Stage => "stages",
        SetupStep.Hotkey => "hotkeys",
        _ => "start",
    };
}
