using System.Text.Json;
using WhisperLoom.Core.Contracts.Settings;

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
        var settings = File.Exists(FilePath) ? SettingsJson.Deserialize(File.ReadAllText(FilePath)) : new WhisperLoomSettings();
        settings.Engine ??= "online";
        return settings;
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
                return element.Deserialize<DesktopSettings>(DesktopJson) ?? new DesktopSettings();
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
