using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Services;

/// <summary>
/// Desktop-eigene Einstellungen. Sie stehen in settings.json im Abschnitt <c>desktop</c> der obersten Ebene
/// (<see cref="WhisperLoomSettings.Extra"/>), damit der Core-Vertrag unverändert bleibt.
/// </summary>
public sealed class DesktopSettings
{
    public const string SectionName = "desktop";

    public const double DefaultStageFontSize = 32;

    /// <summary>"" = nach OS-Sprache, sonst "de" oder "en".</summary>
    public string UiLanguage { get; set; } = "";

    /// <summary>"clipboard" (Standard). Einfügen und Tippen kommen in Phase 3.</summary>
    public string Output { get; set; } = "clipboard";

    /// <summary>Name des Mikrofons; "" = Standardgerät.</summary>
    public string Microphone { get; set; } = "";

    public double StageFontSize { get; set; } = DefaultStageFontSize;

    /// <summary>"system" | "off" | "manual".</summary>
    public string ProxyMode { get; set; } = "system";

    /// <summary>Nur bei <see cref="ProxyMode"/> "manual", z. B. http://proxy:3128.</summary>
    public string ProxyUrl { get; set; } = "";

    /// <summary>Windows: globale Tastenkürzel an.</summary>
    public bool HotkeysEnabled { get; set; } = true;

    /// <summary>Windows: Halten-Kombi (Aufnahme, solange gedrückt), z. B. "Ctrl+Win".</summary>
    public string HotkeyHold { get; set; } = "Ctrl+Win";

    /// <summary>Windows: Umschalt-Kombi (Overlay; zweiter Druck = Senden).</summary>
    public string HotkeyToggle { get; set; } = "Ctrl+Space";

    /// <summary>Windows: Kombi für die nächste Stufe.</summary>
    public string HotkeyStage { get; set; } = "Ctrl+Alt+Space";

    /// <summary>Windows, Diktat per Tastenkürzel: "paste" (einfügen), "type" (direkt tippen) oder "clipboard" (nur Zwischenablage).</summary>
    public string InsertMode { get; set; } = "paste";

    /// <summary>Windows: letztes Diktat erneut ins Vordergrundfenster einfügen (WLD-P5b).</summary>
    public string HotkeyReinsert { get; set; } = "Ctrl+Alt+Shift+V";

    /// <summary>App-Profile (WLD-P5b): Stufe, Ausgabemodus und Enter je Zielprogramm; Reihenfolge = Vorrang unter gleich genauen.</summary>
    public List<Features.Profiles.AppProfile> Profiles { get; set; } = [];

    /// <summary>Unbekannte Felder (neuere Version, Sync von Android) bleiben beim Speichern erhalten.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Liest und schreibt settings.json im Datenordner. Fehlt die Datei, gelten die Standardwerte.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions DesktopJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SettingsStore(string dataDir)
    {
        FilePath = Path.Combine(dataDir, "settings.json");
    }

    public string FilePath { get; }

    /// <summary>
    /// Ohne gewählte Erkennung gilt „online“: der Desktop hat keinen Einrichtungs-Bildschirm wie Android, und die lokale
    /// Erkennung kommt erst mit Phase 4. Sonst bliebe jedes Diktat bei „nicht eingerichtet“ stehen.
    /// </summary>
    /// <exception cref="JsonException">Datei ist kein gültiges JSON.</exception>
    public WhisperLoomSettings Load()
    {
        var settings = File.Exists(FilePath) ? Read(File.ReadAllText(FilePath)) : new WhisperLoomSettings();
        settings.Engine ??= "online";
        // W2-SEL: „Markierung umschreiben“ geht in „Markierung verarbeiten“ auf (desktop.hotkeyRewrite → selection.hotkeyCombo).
        Features.Selection.SelectionMigration.Apply(settings, combo => Platform.Windows.Input.Hotkey.Parse(combo) is not null);
        return settings;
    }

    /// <summary>TD-A4: abgeschaltete OpenAI-Modelle gehen beim Laden auf den Nachfolger (idempotent, <see cref="RetiredModels"/>).</summary>
    internal static WhisperLoomSettings Read(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) return SettingsJson.Deserialize(json);
        RetiredModels.ApplyFull(root);
        return SettingsJson.Deserialize(root.ToJsonString());
    }

    public void Save(WhisperLoomSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, SettingsJson.Serialize(settings));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static DesktopSettings Desktop(WhisperLoomSettings settings)
    {
        if (settings.Extra is not null && settings.Extra.TryGetValue(DesktopSettings.SectionName, out var element))
        {
            try
            {
                var desktop = element.Deserialize<DesktopSettings>(DesktopJson) ?? new DesktopSettings();
                desktop.Profiles = Features.Profiles.AppProfiles.Clean(desktop.Profiles);
                return desktop;
            }
            catch (JsonException)
            {
                return new DesktopSettings();
            }
        }
        return new DesktopSettings();
    }

    public static void SetDesktop(WhisperLoomSettings settings, DesktopSettings desktop)
    {
        settings.Extra ??= [];
        settings.Extra[DesktopSettings.SectionName] = JsonSerializer.SerializeToElement(desktop, DesktopJson);
    }
}
