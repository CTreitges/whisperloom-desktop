using System.Collections.ObjectModel;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// W2-LOCAL: Anbieter-Auswahl aus Katalog, eigenen Anbietern (<c>custom:&lt;id&gt;</c>) und lokalen Servern
/// (<c>local:&lt;name&gt;</c>); ändern sich die Einträge (Seiten „Anbieter“, „Lokale Modelle“), werden die Listen neu
/// gebaut. Modelllisten vom Server mit Cache (<see cref="ModelCache"/>) für die Auswahl.
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>Geladene Modelllisten aller Einstellungsfenster dieser Laufzeit (nur Anbieter, Adresse, Modelle — nie Keys).</summary>
    internal static ModelCache ServerModels { get; } = new();

    private bool _refreshingProviders;

    /// <summary>Key-Ablage für die Key-Felder der eigenen Anbieter.</summary>
    internal ISecretStore SecretStore => _secrets;

    /// <summary>„Zugang prüfen“ für Key-Felder und Server-Zeilen.</summary>
    internal IAccessChecker Checker { get; }

    /// <summary>Katalog und Einträge der Arbeitskopie (ohne Keys).</summary>
    public ProviderDirectory Providers => ProviderDirectory.For(_s);

    /// <summary>Der Zugang nutzt einen Eintrag: sein Key liegt beim Eintrag (Seite „Anbieter“), das Key-Feld des Zugangs entfällt.</summary>
    public bool SttUsesEntry => IsEntry(SttProvider.Value);
    public bool SttFallbackUsesEntry => IsEntry(SttFallbackProvider.Value);
    public bool LlmUsesEntry => LlmProvider.Value == LlmSettings.Same ? SttUsesEntry : IsEntry(LlmProvider.Value);
    public bool LlmFallbackUsesEntry => IsEntry(LlmFallbackProvider.Value);

    private static bool IsEntry(string providerId) => providerId.Contains(':', StringComparison.Ordinal);

    /// <summary>
    /// Hinweis unter einem Zugang auf einen Eintrag (W2-ENR): Firmen-Proxy → „läuft über die Firma“, sonst „Key beim Anbieter“.
    /// </summary>
    public string SttKeyHint => KeyHint(SttProvider.Value);
    public string SttFallbackKeyHint => KeyHint(SttFallbackProvider.Value);
    public string LlmKeyHint => LlmProvider.Value == LlmSettings.Same ? SttKeyHint : KeyHint(LlmProvider.Value);
    public string LlmFallbackKeyHint => KeyHint(LlmFallbackProvider.Value);

    private static string KeyHint(string providerId) =>
        providerId.StartsWith(CompanyProvider.Prefix, StringComparison.Ordinal) ? "Key_AtCompany" : "Key_AtEntry";

    /// <summary>Anzeige eines Anbieters: Katalogname bzw. „Name · eigener Anbieter / lokaler Server“.</summary>
    internal static string ProviderLabel(ProviderInfo p) =>
        p.Id.StartsWith(ProvidersSettings.CustomPrefix, StringComparison.Ordinal) ? Loc.Instance.Format("Provider_CustomEntry", p.Name)
        : p.Id.StartsWith(LocalServerSource.LocalPrefix, StringComparison.Ordinal) ? Loc.Instance.Format("Provider_LocalEntry", p.Name)
        : p.Name;

    /// <summary>Von IT nicht freigegebene Anbieter erscheinen nicht; der aktuelle bleibt sichtbar, sonst wechselte er still.</summary>
    private void FillProviderChoices()
    {
        var used = new[] { _s.Stt.ProviderId, _s.SttFallback.ProviderId, _s.Llm.ProviderId, _s.LlmFallback.ProviderId };
        var offered = Providers.All.Where(p => _policy.IsProviderAllowed(p.Id) || used.Contains(p.Id)).ToList();
        Replace(SttProviders, offered.Where(p => p.HasStt).Select(p => new Choice(p.Id, ProviderLabel(p))));
        Replace(LlmProviders, [new Choice(LlmSettings.Same, Loc.Instance["Settings_LlmSame"]), .. offered.Where(p => p.HasLlm).Select(p => new Choice(p.Id, ProviderLabel(p)))]);
        Replace(LlmFallbackProviders, offered.Where(p => p.HasLlm).Select(p => new Choice(p.Id, ProviderLabel(p))));
    }

    private static void Replace(ObservableCollection<Choice> target, IEnumerable<Choice> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    /// <summary>
    /// Einträge geändert: Listen neu bauen und die Auswahl erhalten. Ein Zugang, dessen Eintrag fehlt, behält seine ID
    /// (unvollständig, kein stiller Wechsel zu einem Cloud-Anbieter).
    /// </summary>
    internal void RefreshProviders()
    {
        BuildResult(); // Auswahl in die Kopie, bevor die Listen kurz leer sind
        _refreshingProviders = true;
        try
        {
            FillProviderChoices();
            EnsureChoice(SttProviders, _s.Stt.ProviderId);
            EnsureChoice(SttProviders, _s.SttFallback.ProviderId);
            EnsureChoice(LlmProviders, _s.Llm.ProviderId);
            EnsureChoice(LlmFallbackProviders, _s.LlmFallback.ProviderId);
            SttProvider = Pick(SttProviders, _s.Stt.ProviderId);
            SttFallbackProvider = Pick(SttProviders, _s.SttFallback.ProviderId);
            LlmProvider = Pick(LlmProviders, _s.Llm.ProviderId);
            LlmFallbackProvider = Pick(LlmFallbackProviders, _s.LlmFallback.ProviderId);
        }
        finally
        {
            _refreshingProviders = false;
        }
        OnPropertyChanged(nameof(Providers));
        NotifyEntryUse();
    }

    /// <summary>Gelöschter Eintrag in Gebrauch: als „(fehlt)“ sichtbar lassen.</summary>
    private static void EnsureChoice(ObservableCollection<Choice> choices, string id)
    {
        if (IsEntry(id) && choices.All(c => c.Value != id))
        {
            choices.Add(new Choice(id, Loc.Instance.Format("Provider_Missing", id)));
        }
    }

    private void NotifyEntryUse()
    {
        OnPropertyChanged(nameof(SttUsesEntry));
        OnPropertyChanged(nameof(SttFallbackUsesEntry));
        OnPropertyChanged(nameof(LlmUsesEntry));
        OnPropertyChanged(nameof(LlmFallbackUsesEntry));
        OnPropertyChanged(nameof(SttKeyHint));
        OnPropertyChanged(nameof(SttFallbackKeyHint));
        OnPropertyChanged(nameof(LlmKeyHint));
        OnPropertyChanged(nameof(LlmFallbackKeyHint));
    }

    /// <summary>
    /// „Modelle vom Server laden“ für einen Zugang (<c>stt</c>, <c>sttFallback</c>, <c>llm</c>, <c>llmFallback</c>):
    /// OpenAI <c>/models</c>, Ollama <c>/api/tags</c>, mit Key des Zugangs bzw. Eintrags. null = geladen, sonst Fehlertext.
    /// </summary>
    internal async Task<string?> LoadServerModelsAsync(string slot, ModelKind kind, CancellationToken ct = default)
    {
        BuildResult();
        AccessKeys keys;
        try
        {
            keys = await SettingsAccess.KeysAsync(_secrets, ct, _s);
        }
        catch (IOException)
        {
            return Loc.Instance["Key_StoreError"];
        }
        var s = SettingsJson.Deserialize(SettingsJson.Serialize(_s));
        s.SttFallback.Enabled = s.LlmFallback.Enabled = true;
        var access = slot switch
        {
            "sttFallback" => SettingsAccess.SttFallback(s, keys)!,
            "llm" => SettingsAccess.Llm(s, keys),
            "llmFallback" => SettingsAccess.LlmFallback(s, keys)!,
            _ => SettingsAccess.Stt(s, keys),
        };
        if (access.SameAsOffline)
        {
            return Loc.Instance["Models_NoAccess"];
        }
        if (!_policy.AllowsTarget(access.Provider.Id, access.BaseUrl))
        {
            return Loc.Instance["Access_Policy"];
        }
        try
        {
            using var http = HttpClients.Create(_desktop);
            await ServerModels.RefreshAsync(http, access, kind, ct);
            return null;
        }
        catch (ApiNotConfiguredException)
        {
            return Loc.Instance["Access_NoUrl"];
        }
        catch (ApiHttpException e)
        {
            return Loc.Instance.Format("Models_Http", e.StatusCode);
        }
        catch (ApiNetworkException)
        {
            return Loc.Instance["Access_Network"];
        }
        catch (ModelListException)
        {
            return Loc.Instance["Models_NoList"];
        }
    }

    /// <summary>Zuletzt geladene Liste des Zugangs (Anbieter + Adresse wie bei der Auflösung); leer = keine.</summary>
    internal IReadOnlyList<RemoteModel> CachedServerModels(string slot, ModelKind kind)
    {
        var (providerId, baseUrl) = SlotTarget(slot);
        return ServerModels.Get(providerId, kind, baseUrl)?.Models ?? [];
    }

    /// <summary>Anbieter und Adresse eines Zugangs ohne Keys (für den Cache-Schlüssel).</summary>
    private (string ProviderId, string BaseUrl) SlotTarget(string slot)
    {
        var s = SettingsJson.Deserialize(SettingsJson.Serialize(_s));
        s.Stt.ProviderId = SttProvider?.Value ?? s.Stt.ProviderId;
        s.SttFallback.ProviderId = SttFallbackProvider?.Value ?? s.SttFallback.ProviderId;
        s.Llm.ProviderId = LlmProvider?.Value ?? s.Llm.ProviderId;
        s.LlmFallback.ProviderId = LlmFallbackProvider?.Value ?? s.LlmFallback.ProviderId;
        s.SttFallback.Enabled = s.LlmFallback.Enabled = true;
        var none = new AccessKeys();
        var access = slot switch
        {
            "sttFallback" => SettingsAccess.SttFallback(s, none)!,
            "llm" => SettingsAccess.Llm(s, none),
            "llmFallback" => SettingsAccess.LlmFallback(s, none)!,
            _ => SettingsAccess.Stt(s, none),
        };
        return (access.Provider.Id, access.BaseUrl);
    }
}
