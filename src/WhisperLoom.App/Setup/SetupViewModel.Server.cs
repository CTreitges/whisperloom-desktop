using WhisperLoom.App.Localization;
using WhisperLoom.Core.Api;

namespace WhisperLoom.App.Setup;

/// <summary>
/// TP-WSETUP: Erkennung über einen eigenen Server (Anbieter ohne feste Adresse, z. B. „Eigener Server (OpenAI-kompatibel)“).
/// Der Schritt Erkennung fragt dann Server-Adresse und optional das Modell ab; „Weiter“ und „Fertig“ gehen erst mit einer
/// gültigen Adresse weiter. Eine ungültige Adresse übernimmt die Arbeitskopie nicht (Prüfung wie unter Erkennung,
/// <see cref="ViewModels.SettingsViewModel.SttBaseUrl"/>), gespeichert wird erst mit „Fertig“ bzw. „Später fortsetzen“.
/// </summary>
public sealed partial class SetupViewModel
{
    private const string UrlPath = "stt.baseUrl";

    private bool _serverTried;

    /// <summary>Online mit einem Anbieter, der eine eigene Adresse braucht: Felder für Server-Adresse und Modell zeigen.</summary>
    public bool ShowServer => ShowOnline && Settings.SttProvider is { } p && ProviderCatalog.Find(p.Value)?.NeedsUrl == true;

    /// <summary>Server-Adresse; ungültig → Fehler an der Zeile, die Arbeitskopie behält die alte Adresse.</summary>
    public string ServerUrl
    {
        get => Settings.SttBaseUrl;
        set
        {
            Settings.SttBaseUrl = value;
            RaiseServer();
        }
    }

    /// <summary>Modell-ID des Servers; leer = Standard des Servers.</summary>
    public string ServerModel
    {
        get => Settings.SttModel;
        set => Settings.SttModel = value;
    }

    /// <summary>Weg vollständig: kein eigener Server gewählt, oder Adresse gesetzt und ohne Fehler.</summary>
    public bool ServerReady => !ShowServer || (Settings.SttBaseUrl.Length > 0 && Settings.Errors[UrlPath].Length == 0);

    /// <summary>Fehler an der Adresszeile: ungültige Eingabe, sonst – nach einem Weiter-Versuch – die fehlende Adresse.</summary>
    public string ServerError
    {
        get
        {
            if (!ShowServer)
            {
                return "";
            }
            var invalid = Settings.Errors[UrlPath];
            if (invalid.Length > 0)
            {
                return invalid;
            }
            return _serverTried && Settings.SttBaseUrl.Length == 0 ? Loc.Instance["Setup_ServerUrlMissing"] : "";
        }
    }

    /// <summary>„Weiter“ im Schritt Erkennung ohne gültige Adresse: Fehler zeigen und bleiben.</summary>
    private bool BlockedByServer()
    {
        if (ServerReady)
        {
            return false;
        }
        _serverTried = true;
        RaiseServer();
        return true;
    }

    private void RaiseServer()
    {
        OnPropertyChanged(nameof(ShowServer));
        OnPropertyChanged(nameof(ServerReady));
        OnPropertyChanged(nameof(ServerError));
    }

    private void OnErrorsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseServer();
}
