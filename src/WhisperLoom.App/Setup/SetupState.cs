namespace WhisperLoom.App.Setup;

/// <summary>
/// Ob die Einrichtung erledigt (abgeschlossen oder übersprungen) ist: Marker-Datei <c>setup-done</c> im Datenordner.
/// Bewusst nicht in settings.json, denn die wird exportiert und mit Android geteilt; die Einrichtung gilt je Gerät.
/// </summary>
public sealed class SetupState(string dataDir)
{
    public string FilePath { get; } = Path.Combine(dataDir, "setup-done");

    public bool Done => File.Exists(FilePath);

    /// <exception cref="IOException">Datenordner nicht beschreibbar.</exception>
    public void MarkDone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Beim Start zeigen: nur, solange nicht erledigt, und nie im Testbetrieb (<c>--test-audio</c>/<c>--output-file</c>),
    /// damit Smokes kein sichtbares Fenster öffnen. Erneut aufrufen geht immer über Tray bzw. <c>WhisperLoom setup</c>.
    /// </summary>
    internal static bool ShowOnStart(Cli.AppOptions options, SetupState state) =>
        options.TestAudio is null && options.OutputFile is null && !state.Done;
}
