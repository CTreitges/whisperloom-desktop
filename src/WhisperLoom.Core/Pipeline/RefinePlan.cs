using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Pipeline;

/// <summary>Die Textverbesserung ist gescheitert; die Meldung nennt den Grund (auch von Erst- und Fallback-Zugang zusammen).</summary>
internal sealed class RefineFailedException(string message, Exception? inner) : Exception(message, inner);

/// <summary>Was ein <see cref="RefinePlan"/> zum Rechnen braucht.</summary>
internal sealed record RefineServices(HttpClient Http, IRefinePromptBuilder Prompts, TextCleanup Cleanup, Action<ApiAccess> RememberNoTemperature);

/// <summary>
/// Die Textverbesserung eines Auftrags, wie <see cref="RefineDecision"/> sie entscheidet, samt Ausführung (Port von
/// <c>RefinePlan.kt</c>): Online → Fallback-Zugang → lokal → (beim Aufrufer) Rohtext + Regeln. Für die Stücke einer
/// Sprachnachricht merkt sich der Plan Ausfälle, damit nicht jedes Stück wieder in denselben Fehler läuft.
/// </summary>
public sealed class RefinePlan
{
    /// <summary>Kein Netz: es ging keine Anfrage raus.</summary>
    public const string MsgNoNet = "Kein Netz für den Online-Zugang";

    /// <summary>Regel „lokal“, aber das Textmodell fehlt.</summary>
    public const string MsgLocalMissing = "Kein Textmodell geladen — unter „Offline-Textverbesserung“ laden oder „Überspringen“ wählen";

    /// <summary>Die lokale Rechnung ist gescheitert.</summary>
    public const string MsgLocalFailed = "Lokales Textmodell fehlgeschlagen";

    /// <summary>Kein Ausfall, nur zur Info: verbessert, nur eben lokal.</summary>
    public const string MsgOnlineFailedLocal = "Online-Textverbesserung fehlgeschlagen — lokal verbessert";

    /// <summary>Connect-Zeitlimit, wenn etwas einspringen kann: lieber früh wechseln als 15 s warten.</summary>
    public const int FallbackConnectTimeoutMs = 8_000;

    private readonly ILocalRefiner? _local;
    private readonly RefineServices _services;
    private volatile ApiAccess _access;
    private volatile ApiAccess? _fallback;
    private volatile bool _onlineFailed;
    private volatile bool _primaryFailed;

    internal RefinePlan(RefineRoute route, ApiAccess access, ApiAccess? fallback, ILocalRefiner? local, RefineServices services)
    {
        Route = route;
        _access = access;
        _fallback = fallback;
        _local = local;
        _services = services;
    }

    public RefineRoute Route { get; }

    /// <summary>Kein Ausfall, nur zur Info: der KI-Zugang ist gescheitert, der Fallback hat verbessert.</summary>
    public static string FallbackMessage(string provider) => $"KI-Zugang fehlgeschlagen — mit Fallback {provider} verbessert";

    public static string Message(RefineHint hint) => hint == RefineHint.NoNet ? MsgNoNet : MsgLocalMissing;

    internal static int ConnectTimeoutMs(RefineRoute.Online route) => route.FallbackLocal ? FallbackConnectTimeoutMs : Http.ConnectTimeoutMs;

    /// <summary>
    /// Bearbeiteter Text auf der <see cref="Route"/> (<see cref="RefineRoute.Raw"/> rechnet nie). Fehler werden geworfen; der
    /// Aufrufer fällt auf den Rohtext zurück.
    /// </summary>
    /// <param name="onNote">Hinweis, der den Text nicht betrifft (lokal verbessert, mit Fallback verbessert).</param>
    internal async Task<string> RefineAsync(string raw, string language, Refinement refinement, Action<string> onNote, CancellationToken ct)
    {
        switch (Route)
        {
            case RefineRoute.Raw:
                return raw;
            case RefineRoute.Local:
                return await LocalAsync(raw, language, refinement, ct).ConfigureAwait(false);
        }
        var route = (RefineRoute.Online)Route;
        if (_onlineFailed) return await LocalAsync(raw, language, refinement, ct).ConfigureAwait(false);
        try
        {
            return await OnlineAsync(route, raw, language, refinement, onNote, ct).ConfigureAwait(false);
        }
        catch (Exception) when (route.FallbackLocal && !ct.IsCancellationRequested)
        {
            _onlineFailed = true;
            var text = await LocalAsync(raw, language, refinement, ct).ConfigureAwait(false);
            onNote(MsgOnlineFailedLocal);
            return text;
        }
    }

    /// <summary>Womit gerechnet wurde, als Anzeige: Modell des Zugangs, des Fallbacks oder das lokale; null ohne KI.</summary>
    public string? ModelLabel()
    {
        if (Route is RefineRoute.Raw) return null;
        if (Route is RefineRoute.Local || _onlineFailed) return _local?.ModelLabel;
        var access = _primaryFailed ? _fallback : _access;
        return access is null ? null : access.ModelOption?.Label ?? access.Model;
    }

    /// <summary>Erst der Zugang, scheitert er, der Fallback (der erste wartet dann kürzer auf die Verbindung).</summary>
    private async Task<string> OnlineAsync(RefineRoute.Online route, string raw, string language, Refinement refinement, Action<string> onNote, CancellationToken ct)
    {
        var second = _fallback;
        Exception? firstError = null;
        if (!_primaryFailed)
        {
            var timeout = second is not null ? FallbackConnectTimeoutMs : ConnectTimeoutMs(route);
            try
            {
                return await Refiner(_access, timeout, a => _access = a).RefineAsync(raw, language, refinement, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (second is not null && !ct.IsCancellationRequested)
            {
                _primaryFailed = true;
                firstError = e;
            }
        }
        string text;
        try
        {
            text = await Refiner(second!, ConnectTimeoutMs(route), a => _fallback = a).RefineAsync(raw, language, refinement, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            var fallbackFailed = $"Fallback: {Reason(e)}";
            throw firstError is null
                ? new RefineFailedException(fallbackFailed, e)
                : new RefineFailedException($"{Reason(firstError)} · {fallbackFailed}", firstError);
        }
        onNote(FallbackMessage(second!.Provider.Name));
        return text;
    }

    /// <summary>Lehnt das Modell temperature ab, merkt der Plan sich den Zugang ohne — weitere Stücke fragen gleich richtig.</summary>
    private TextRefiner Refiner(ApiAccess access, int connectTimeoutMs, Action<ApiAccess> replace) =>
        new(access, _services.Http, _services.Prompts, _services.Cleanup, connectTimeoutMs, withoutTemperature =>
        {
            _services.RememberNoTemperature(withoutTemperature);
            replace(withoutTemperature);
        });

    /// <summary>Init- oder Laufzeitfehler als <see cref="MsgLocalFailed"/>; eine unplausible Antwort behält ihre Meldung.</summary>
    private async Task<string> LocalAsync(string raw, string language, Refinement refinement, CancellationToken ct)
    {
        try
        {
            if (_local is null) throw new LocalNotAvailableException("Kein lokaler Textverbesserer eingerichtet");
            return await _local.RefineAsync(raw, language, refinement, ct).ConfigureAwait(false);
        }
        catch (RefineRejectedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new RefineFailedException(MsgLocalFailed, e);
        }
    }

    private static string Reason(Exception e) => string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
}
