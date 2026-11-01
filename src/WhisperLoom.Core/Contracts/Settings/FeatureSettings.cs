namespace WhisperLoom.Core.Contracts.Settings;

/// <summary>Ausgabe des Diktats (Abschnitt <c>output</c>, W2). Plattformneutral, Android darf ihn mitlesen.</summary>
public sealed class OutputSettings
{
    /// <summary>Text zusätzlich immer in die Zwischenablage legen, auch nach erfolgreichem Einfügen.</summary>
    public bool AlwaysCopy { get; set; }

    /// <summary>
    /// Nach dem Einfügen Enter senden (ersetzt die frühere Strg+Enter-Taste im Overlay). App-Profile mit
    /// <c>pressEnter: null</c> erben diesen Wert. Ein Import übernimmt <c>true</c> nur, wenn es schon <c>true</c> war.
    /// </summary>
    public bool PressEnter { get; set; }
}

/// <summary>Markierten Text verarbeiten (Abschnitt <c>selection</c>, W2): Tastenkombi und zwei Mauswege, einzeln schaltbar.</summary>
public sealed class SelectionSettings
{
    public const string DefaultHotkey = "Ctrl+Alt+Shift+S";

    /// <summary>
    /// Kombi wie <c>desktop.hotkey*</c>; "" = aus. Tastenkürzel sind nie Fest. Heißt <c>hotkeyCombo</c> statt <c>hotkey</c>, weil kein
    /// Feld des Schemas auf <c>key</c> enden darf (Test <c>KeinKeyFeldImSchema</c>).
    /// </summary>
    public string HotkeyCombo { get; set; } = DefaultHotkey;

    /// <summary>Schwebender Knopf neben einer Markierung.</summary>
    public bool FloatingButton { get; set; }

    /// <summary>Strg+Rechtsklick auf eine Markierung.</summary>
    public bool CtrlRightClick { get; set; }
}

/// <summary>Eigene Anbieter-Einträge (Abschnitt <c>providers</c>, W2) zusätzlich zum Katalog.</summary>
public sealed class ProvidersSettings
{
    /// <summary>Präfix, mit dem <c>*.providerId</c> einen eigenen Eintrag nennt: <c>"custom:&lt;id&gt;"</c>.</summary>
    public const string CustomPrefix = "custom:";

    public List<CustomProvider> Custom { get; set; } = [];
}

/// <summary>Ein eigener Anbieter. Keys liegen wie immer im <see cref="ISecretStore"/>, nie hier.</summary>
public sealed class CustomProvider
{
    public static readonly IReadOnlyList<string> Protocols = ["openai", "ollama", "elevenlabs", "anthropic"];

    /// <summary><c>[a-z0-9][a-z0-9_-]{0,31}</c>, eindeutig; Verweis als <c>custom:&lt;id&gt;</c>.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Eins aus <see cref="Protocols"/>. openai: Erkennung + Text; ollama, anthropic: Text; elevenlabs: Erkennung.</summary>
    public string Protocol { get; set; } = "openai";

    /// <summary>http(s)-Adresse; http nur zu privaten Adressen.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Angebotene Modell-IDs; leer = Modellliste vom Server abfragen.</summary>
    public List<string> Models { get; set; } = [];

    /// <summary>Kein Key nötig (z. B. Server im LAN).</summary>
    public bool KeyOptional { get; set; }

    /// <summary>Kann der Eintrag erkennen (<c>stt</c>) bzw. Text verbessern?</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasStt => Protocol is "openai" or "elevenlabs";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasLlm => Protocol is "openai" or "ollama" or "anthropic";
}

/// <summary>Eigene Whisper-Datei für die lokale Erkennung (<c>offline.customModels</c>); Auswahl als <c>offline.model = "custom:&lt;name&gt;"</c>.</summary>
public sealed class CustomWhisperModel
{
    public string Name { get; set; } = "";

    /// <summary>Pfad der ggml-Datei auf diesem Gerät (gerätebezogen: Import und Export lassen ihn weg).</summary>
    public string Path { get; set; } = "";
}

/// <summary>Lokaler OpenAI-kompatibler Server (<c>local.servers</c>, Desktop-Abschnitt <c>local</c>).</summary>
public sealed class LocalServer
{
    public static readonly IReadOnlyList<string> Kinds = ["stt", "text"];

    public string Name { get; set; } = "";

    /// <summary>"stt" (Erkennung, <c>/audio/transcriptions</c>) oder "text" (<c>/chat/completions</c>).</summary>
    public string Kind { get; set; } = "text";

    public string BaseUrl { get; set; } = "";

    /// <summary>"" = erstes Modell der Liste des Servers.</summary>
    public string Model { get; set; } = "";
}
