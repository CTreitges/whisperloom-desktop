using System.Globalization;
using System.Reflection;

namespace WhisperLoom.App.Updates;

/// <summary>Version der App für <c>--version</c>: die beim Publish gesetzte <c>-p:Version</c>.</summary>
internal static class AppVersion
{
    public static string Current =>
        Format(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Schneidet die Commit-Kennung (<c>+&lt;sha&gt;</c>) ab, die das SDK anhängt.</summary>
    internal static string Format(string? informational) =>
        string.IsNullOrEmpty(informational) ? "0.0.0" : informational.Split('+')[0];

    /// <summary>Vorabversion (SemVer mit <c>-…</c>, z. B. <c>1.1.0-beta.1</c>).</summary>
    internal static bool IsPrerelease(string version) => Format(version).Contains('-', StringComparison.Ordinal);

    /// <summary>
    /// Rangfolge nach SemVer 2.0 (Abschnitt 11): Kern numerisch, Vorabversion kleiner als die Version ohne, Kennungen
    /// numerisch vor alphanumerisch, Build-Metadaten (<c>+…</c>) zählen nicht.
    /// </summary>
    // Doku: semver.org/#spec-item-11 (abgerufen 2026-11-02).
    internal static int Compare(string a, string b)
    {
        var (coreA, preA) = Split(a);
        var (coreB, preB) = Split(b);
        for (var i = 0; i < 3; i++)
        {
            var c = coreA[i].CompareTo(coreB[i]);
            if (c != 0) return c;
        }
        if (preA.Length == 0 && preB.Length == 0) return 0;
        if (preA.Length == 0) return 1;
        if (preB.Length == 0) return -1;
        for (var i = 0; i < Math.Min(preA.Length, preB.Length); i++)
        {
            var c = CompareIdentifier(preA[i], preB[i]);
            if (c != 0) return c;
        }
        return preA.Length.CompareTo(preB.Length);
    }

    private static (long[] Core, string[] Pre) Split(string version)
    {
        var plain = Format(version);
        var dash = plain.IndexOf('-', StringComparison.Ordinal);
        var core = (dash < 0 ? plain : plain[..dash]).Split('.');
        var numbers = new long[3];
        for (var i = 0; i < 3 && i < core.Length; i++)
        {
            long.TryParse(core[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]);
        }
        return (numbers, dash < 0 ? [] : plain[(dash + 1)..].Split('.'));
    }

    private static int CompareIdentifier(string a, string b)
    {
        var numA = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var na);
        var numB = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var nb);
        return (numA, numB) switch
        {
            (true, true) => na.CompareTo(nb),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(a, b) switch { 0 => 0, var x => x > 0 ? 1 : -1 },
        };
    }
}
