namespace WhisperLoom.Core.Contracts.Settings;

/// <summary>
/// Stufe einer Einstellung unter einer Firmen-Richtlinie (Provisioning-Datei oder Firmen-Server, gleichwertig).
/// JSON-Schlüssel über <see cref="ManagedStates"/>: <c>"free"</c>, <c>"default"</c>, <c>"fixed"</c>.
/// </summary>
public enum ManagedState
{
    /// <summary>Frei: nichts zentral.</summary>
    Free,

    /// <summary>Vorgabe: zentral vorbelegt, der Nutzer darf ändern; seine Änderung gewinnt bis „Auf Firmenvorgabe zurücksetzen“.</summary>
    Default,

    /// <summary>Fest: zentral gesetzt, in der App ausgeblendet. Nie für Tastenkürzel.</summary>
    Fixed,
}

public static class ManagedStates
{
    public static string Key(this ManagedState state) => state switch
    {
        ManagedState.Default => "default",
        ManagedState.Fixed => "fixed",
        _ => "free",
    };

    /// <summary>null bei unbekanntem Schlüssel (das Provisioning meldet dann einen Fehler).</summary>
    public static ManagedState? FromKey(string? key) => key switch
    {
        "free" => ManagedState.Free,
        "default" => ManagedState.Default,
        "fixed" => ManagedState.Fixed,
        _ => null,
    };
}
