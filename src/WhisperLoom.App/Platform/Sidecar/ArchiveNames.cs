namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Dateinamen aus heruntergeladenen Archiven (W2-FIX N-9, Zip-Slip): Bei Zips „made by Unix“ meldet .NET unter Windows
/// <c>..\x</c>, absolute oder laufwerksrelative Namen (<c>C:x</c>) als <c>Name</c>; <c>Path.Combine</c> führte damit aus dem
/// Zielordner heraus. Erlaubt ist nur ein einfacher Dateiname, und der fertige Pfad muss im Zielordner liegen.
/// </summary>
// Doku: learn.microsoft.com/dotnet/api/system.io.compression.ziparchiveentry.name (Trennzeichen je „made by“) und
// learn.microsoft.com/dotnet/api/system.io.path.getfullpath (abgerufen 2026-11-01).
internal static class ArchiveNames
{
    /// <summary>Pfad im Ordner <paramref name="dir"/> für den Eintrag <paramref name="entryName"/>; null = verwerfen.</summary>
    public static string? SafePath(string dir, string entryName)
    {
        var name = entryName.Split('/', '\\')[^1];
        if (name is "" or "." or ".." || name.Contains(':') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.IsPathRooted(name)
            || IsDeviceName(name))
        {
            return null;
        }
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, name));
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && Path.GetDirectoryName(full) + Path.DirectorySeparatorChar == root ? full : null;
    }

    /// <summary>Reservierte Windows-Gerätenamen (CON, NUL, COM1, aux.dll …), auch mit Endung oder Leerzeichen.</summary>
    // Doku: learn.microsoft.com/windows/win32/fileio/naming-a-file#naming-conventions (abgerufen 2026-11-01).
    private static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && (char.IsAsciiDigit(stem[3]) || stem[3] is '¹' or '²' or '³'));
    }
}
