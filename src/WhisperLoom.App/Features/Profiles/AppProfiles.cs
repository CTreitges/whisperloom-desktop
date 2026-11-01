using System.Text.RegularExpressions;

namespace WhisperLoom.App.Features.Profiles;

/// <summary>
/// Ein App-Profil (Einstellung <c>desktop.profiles</c>): passt das Zielfenster eines Diktats per Tastenkürzel zu
/// <see cref="Process"/> (und optional <see cref="TitlePattern"/>), gelten Stufe, Ausgabemodus und „Enter danach“ des Profils.
/// </summary>
public sealed class AppProfile
{
    /// <summary>Anzeigename im Hinweis „Profil · Stufe“; "" = Prozessname.</summary>
    public string Name { get; set; } = "";

    /// <summary>Prozessname mit oder ohne „.exe“, Groß-/Kleinschreibung egal, z. B. "OUTLOOK.EXE".</summary>
    public string Process { get; set; } = "";

    /// <summary>Regex auf den Fenstertitel (ohne Groß-/Kleinschreibung); "" = jeder Titel.</summary>
    public string TitlePattern { get; set; } = "";

    /// <summary>Stufen-Schlüssel; "" = Standard-Stufe.</summary>
    public string Stage { get; set; } = "";

    /// <summary>"paste", "type", "clipboard"; "" = Standard-Ausgabemodus.</summary>
    public string InsertMode { get; set; } = "";

    /// <summary>
    /// Nach dem Einfügen Enter senden: null = Erben (<c>output.pressEnter</c>), true = An, false = Aus. Ältere Dateien
    /// mit <c>false</c> bleiben „Aus“ — gleiches Verhalten wie bisher, weil <c>output.pressEnter</c> standardmäßig aus ist.
    /// </summary>
    public bool? PressEnter { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>Das passende Profil und seine Stelle in der Liste (zum Speichern einer geänderten Stufe).</summary>
public sealed record ProfileMatch(int Index, AppProfile Profile);

/// <summary>Prozess und Titel eines Fensters (Win32: <c>GetWindowThreadProcessId</c>, <c>GetWindowTextW</c>).</summary>
public sealed record WindowInfo(string Process, string Title);

/// <summary>
/// Auflösung der App-Profile. Vorrang: ein Profil mit passendem Titel-Regex vor einem Profil nur mit Prozess; unter
/// gleich genauen gilt das erste der Liste. Ein ungültiger Regex macht nur sein Profil unwirksam.
/// </summary>
public static class AppProfiles
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Vorschläge in den Einstellungen: Terminal → Prompt, Outlook → Verschönern.</summary>
    public static IReadOnlyList<AppProfile> Suggestions() =>
    [
        new AppProfile { Name = "Terminal", Process = "WindowsTerminal.exe", Stage = "prompt" },
        new AppProfile { Name = "Outlook", Process = "OUTLOOK.EXE", Stage = "beautify" },
    ];

    /// <summary>
    /// Profile aus fremden Dateien (Import, Provisioning, von Hand): null-Einträge fallen weg, null-Felder werden "".
    /// </summary>
    public static List<AppProfile> Clean(IEnumerable<AppProfile?>? profiles) =>
    [
        .. (profiles ?? []).OfType<AppProfile>().Select(p => new AppProfile
        {
            Name = p.Name ?? "",
            Process = p.Process ?? "",
            TitlePattern = p.TitlePattern ?? "",
            Stage = p.Stage ?? "",
            InsertMode = p.InsertMode ?? "",
            PressEnter = p.PressEnter,
            Enabled = p.Enabled,
        }),
    ];

    public static ProfileMatch? Resolve(IReadOnlyList<AppProfile> profiles, WindowInfo window)
    {
        ProfileMatch? processOnly = null;
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            if (!Matches(profile, window))
            {
                continue;
            }
            if (profile.TitlePattern.Length > 0)
            {
                return new ProfileMatch(i, profile);
            }
            processOnly ??= new ProfileMatch(i, profile);
        }
        return processOnly;
    }

    /// <summary>Passt das Profil zum Fenster? Deaktiviert, ohne Prozess oder mit ungültigem Regex: nein.</summary>
    public static bool Matches(AppProfile profile, WindowInfo window)
    {
        var process = NormalizeProcess(profile.Process);
        if (!profile.Enabled || process.Length == 0 || process != NormalizeProcess(window.Process))
        {
            return false;
        }
        if (profile.TitlePattern.Length == 0)
        {
            return true;
        }
        try
        {
            return Regex.IsMatch(window.Title, profile.TitlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>Ist der Titel-Regex gültig (Einstellungsfenster)? Leer zählt als gültig.</summary>
    public static bool IsValidPattern(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>"OUTLOOK.EXE", "outlook" → "outlook".</summary>
    public static string NormalizeProcess(string process)
    {
        var name = process.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }

    public static string DisplayName(AppProfile profile) =>
        profile.Name.Trim().Length > 0 ? profile.Name.Trim() : NormalizeProcess(profile.Process);
}
