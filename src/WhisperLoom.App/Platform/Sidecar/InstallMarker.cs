namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Vollständigkeit einer entpackten Begleitprozess-Variante (<see cref="LlamaRelease"/>, <see cref="SidecarRelease"/>;
/// Kompass 01a2145a-bb06): Nach dem Entpacken steht im Ordner eine Liste der Archiv-Dateien. Installiert ist die Variante
/// nur, wenn die Exe und jede gelistete Datei da sind; fehlt etwas (Bibliotheken von außen gelöscht), lädt
/// <c>EnsureAsync</c> neu statt eine Exe zu liefern, die nicht startet.
/// </summary>
/// <remarks>
/// Ältere Installationen ohne Liste: so muss niemand nach dem Update neu laden (offline ginge das nicht).
/// <c>llama-server</c> ist nur ein Starter, der seine Bibliotheken braucht: ohne Liste vollständig, wenn neben der Exe noch
/// Dateien liegen; die Exe allein wird repariert. <c>whisper-server</c> kommt unter Windows als eine Datei: ohne Liste
/// genügt die Exe wie bisher.
/// </remarks>
internal static class InstallMarker
{
    public const string FileName = ".installed";

    /// <param name="legacyNeedsLibraries">Ohne Liste: neben der Exe muss noch eine Datei liegen (llama-server).</param>
    public static bool IsComplete(string exe, bool legacyNeedsLibraries)
    {
        var dir = Path.GetDirectoryName(exe)!;
        try
        {
            if (!File.Exists(exe)) return false;
            var marker = Path.Combine(dir, FileName);
            if (File.Exists(marker))
            {
                return File.ReadAllLines(marker).Where(n => n.Length > 0).All(n => File.Exists(Path.Combine(dir, n)));
            }
            return !legacyNeedsLibraries || Directory.EnumerateFiles(dir).Any(f => !string.Equals(Path.GetFileName(f), Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Schreibt die Liste der entpackten Dateien in den noch temporären Ordner (vor dem Umbenennen).</summary>
    public static void Write(string dir) =>
        File.WriteAllLines(Path.Combine(dir, FileName), Directory.EnumerateFiles(dir).Select(Path.GetFileName).Where(n => n != FileName).Order(StringComparer.Ordinal)!);
}
