namespace WhisperLoom.App.Updates;

/// <summary>
/// Brücke Einstellungen → laufender <see cref="UpdateService"/> (TP-WUPDATE): Die Seite „System“ meldet einen geänderten
/// Kanal oder Vorab-Schalter, die App hängt beim Start <see cref="UpdateService.RequestCheckAsync"/> ein. Ohne Dienst
/// (Tests, nicht installierte App) bleibt <see cref="Handler"/> null und die Meldung verpufft.
/// </summary>
public static class UpdateCheckRequests
{
    /// <summary>Gesetzt von der App beim Start der Update-Prüfung.</summary>
    public static Action? Handler { get; set; }

    public static void Request() => Handler?.Invoke();
}
