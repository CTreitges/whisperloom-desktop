namespace WhisperLoom.Core.Contracts.Settings;

/// <summary>
/// Einstellungen, Schema v1 (JSON, <see cref="SchemaVersion"/>). Felder und Standardwerte aus <c>Prefs.kt</c>
/// (PREFS_VERSION 7, Android 3.9.5). Stufen, Bereinigung und Form stehen als Schlüssel (<c>"polish"</c>),
/// gelesen über <see cref="RefineModes.FromKey"/> bzw. <see cref="RefineKeys"/> — Unbekanntes gilt wie auf
/// Android als Standard. Keys stehen nie hier, sondern im <see cref="ISecretStore"/> (<see cref="SecretNames"/>).
/// Spätere Phasen ergänzen nur additiv (eigene Abschnitte bzw. neue Felder mit Standardwert).
/// </summary>
public sealed class WhisperLoomSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Erkennungssprache: "auto" oder ISO-Code. Android-Standard: Deutsch.</summary>
    public string Language { get; set; } = "de";

    /// <summary>"online" | "offline"; null = noch nicht gewählt (Einrichtung zeigen).</summary>
    public string? Engine { get; set; }

    public SttSettings Stt { get; set; } = new();

    public SttFallbackSettings SttFallback { get; set; } = new();

    public LlmSettings Llm { get; set; } = new();

    public LlmFallbackSettings LlmFallback { get; set; } = new();

    /// <summary>Stufe und Stufen-Einstellungen des Diktats (Weg <see cref="RefineWay.Dictation"/>).</summary>
    public RefineSettings Refine { get; set; } = new();

    /// <summary>Weg <see cref="RefineWay.Share"/>: auf Desktop ohne Funktion, für den Sync mit Android erhalten.</summary>
    public ShareSettings Share { get; set; } = new();

    public PolishSettings Polish { get; set; } = new();

    public OfflineSettings Offline { get; set; } = new();

    public HistorySettings History { get; set; } = new();

    /// <summary>Modellwahl mit der aktuellen Liste des Anbieters statt der Katalog-Empfehlungen.</summary>
    public bool ServerModelsEnabled { get; set; }
}

public sealed class SttSettings
{
    /// <summary>Anbieter-ID aus dem Katalog.</summary>
    public string ProviderId { get; set; } = "openai";

    /// <summary>"" = Basis-URL des Anbieters.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>"" = Empfehlung des Anbieters.</summary>
    public string Model { get; set; } = "";

    /// <summary>Lese-Zeitlimit in Sekunden; 0 = Anbieter-Standard (90, eigener Server 600), sonst 30–1800.</summary>
    public int ReadTimeoutSec { get; set; }

    /// <summary>Kontext-Wörter (Vokabular), ein Eintrag pro Zeile; geht als <c>prompt</c> an die Erkennung.</summary>
    public string Vocabulary { get; set; } = "";

    /// <summary>Pfad einer verknüpften .md/.txt-Datei mit Kontext-Wörtern; "" = keine.</summary>
    public string VocabularyFile { get; set; } = "";
}

public sealed class SttFallbackSettings
{
    public bool Enabled { get; set; }

    public string ProviderId { get; set; } = "openai";

    public string BaseUrl { get; set; } = "";

    public string Model { get; set; } = "";
}

public sealed class LlmSettings
{
    /// <summary>Wert für „wie Erkennung“: der Erkennungs-Zugang wird wiederverwendet.</summary>
    public const string Same = "same";

    /// <summary><see cref="Same"/> oder Anbieter-ID.</summary>
    public string ProviderId { get; set; } = Same;

    public string BaseUrl { get; set; } = "";

    /// <summary>Modell des Zugangs = Standard für Stufen ohne eigenes; "" = Empfehlung je Stufe.</summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// Eigenes Modell je Modell-Stufe (<see cref="RefineModes.ModelStages"/>), Schlüssel = Stufen-Schlüssel
    /// ("polish", "beautify", "summarize", "prompt"); fehlend oder "" = Standard.
    /// </summary>
    public Dictionary<string, string> StageModels { get; set; } = [];
}

public sealed class LlmFallbackSettings
{
    public bool Enabled { get; set; }

    public string ProviderId { get; set; } = "openai";

    public string BaseUrl { get; set; } = "";

    /// <summary>Ein Modell für alle Stufen; "" = Empfehlung des Fallback-Anbieters je Stufe.</summary>
    public string Model { get; set; } = "";
}

public sealed class RefineSettings
{
    /// <summary>Gespeicherte Stufe (Schlüssel). "prompt" gilt nur mit <see cref="PromptEnabled"/>, sonst als "polish".</summary>
    public string Stage { get; set; } = "off";

    /// <summary>Stufe „Prompt“ anbieten (auf Android eine Pro-Funktion).</summary>
    public bool PromptEnabled { get; set; }

    /// <summary>Bereinigung beim Glätten: "plain" | "clean" | "readable".</summary>
    public string PolishCleanup { get; set; } = "plain";

    /// <summary>Form beim Zusammenfassen: "auto" | "prose".</summary>
    public string SummarizeForm { get; set; } = "auto";

    public bool ParagraphsPolish { get; set; } = true;

    public bool ParagraphsBeautify { get; set; } = true;
}

public sealed class ShareSettings
{
    /// <summary>Nur Stufen aus <see cref="RefineModes.Settings"/>; Unbekanntes gilt als "off".</summary>
    public string Stage { get; set; } = "off";

    public string PolishCleanup { get; set; } = "plain";

    public string SummarizeForm { get; set; } = "auto";

    public bool HideFillers { get; set; } = true;
}

public sealed class PolishSettings
{
    public bool RemoveFillers { get; set; } = true;

    public bool AutoCapitalize { get; set; } = true;

    /// <summary>Nach jedem Diktat ein Leerzeichen anhängen.</summary>
    public bool TrailingSpace { get; set; } = true;

    /// <summary>Eigene Füllwörter, klein geschrieben, ohne Duplikate.</summary>
    public List<string> CustomFillers { get; set; } = [];

    /// <summary>Wörter der eingebauten Liste, die nicht gefiltert werden.</summary>
    public List<string> DisabledFillers { get; set; } = [];
}

public sealed class OfflineSettings
{
    /// <summary>Modell-ID der lokalen Erkennung (tiny/base/small/large-v3-turbo).</summary>
    public string Model { get; set; } = "small";

    /// <summary>Beam-Search (genauer, langsamer) statt Greedy.</summary>
    public bool Accurate { get; set; } = true;

    /// <summary>Verbesserung bei lokaler Erkennung: "local" | "online_local" | "skip".</summary>
    public string Refine { get; set; } = "local";

    /// <summary>Lokales Textmodell. Android: LiteRT-ID (gemma4_e2b); Desktop nutzt Ollama (Phase 4).</summary>
    public string LocalLlmModel { get; set; } = "gemma4_e2b";
}

public sealed class HistorySettings
{
    public static readonly IReadOnlyList<int> Sizes = [10, 25, 50, 100, 250, 500];

    public const int DefaultSize = 50;

    public bool Enabled { get; set; } = true;

    /// <summary>Höchstzahl der Einträge, eine aus <see cref="Sizes"/>; Unbekanntes gilt als <see cref="DefaultSize"/>.</summary>
    public int Size { get; set; } = DefaultSize;
}
