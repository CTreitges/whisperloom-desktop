using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Pipeline;

/// <summary>Womit erkannt wird (Einstellung <c>engine</c>: "online" | "offline"; null = noch nicht gewählt).</summary>
public enum Engine
{
    Online,
    Offline,
}

/// <summary>Regel „Textverbesserung bei lokaler Erkennung“ (Port von <c>OfflineRefineRule</c>). Online erkannt gilt sie nicht.</summary>
public enum OfflineRefineRule
{
    /// <summary>Standard: lokal erkannter Text wird lokal verbessert, nie online.</summary>
    Local,

    /// <summary>Mit eigenem Online-Zugang und Netz online; sonst oder bei Online-Fehler lokal.</summary>
    OnlineLocal,

    /// <summary>Mit eigenem Online-Zugang und Netz online; sonst ohne KI.</summary>
    Skip,
}

/// <summary>Schlüssel von <see cref="Engine"/> und <see cref="OfflineRefineRule"/>.</summary>
public static class Engines
{
    public static Engine? FromKey(string? key) => key switch
    {
        "online" => Engine.Online,
        "offline" => Engine.Offline,
        _ => null,
    };

    public static string Key(this OfflineRefineRule rule) => rule switch
    {
        OfflineRefineRule.Local => "local",
        OfflineRefineRule.OnlineLocal => "online_local",
        OfflineRefineRule.Skip => "skip",
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
    };

    /// <summary>Ungespeichert und Unbekanntes gilt als <see cref="OfflineRefineRule.Local"/>.</summary>
    public static OfflineRefineRule RuleFromKey(string? key) =>
        Enum.GetValues<OfflineRefineRule>().FirstOrDefault(r => r.Key() == key, OfflineRefineRule.Local);

    /// <summary>
    /// Die Regel, die wirkt: gibt es kein lokales Textmodell (Android: passt nicht in den RAM; Desktop: kein lokaler
    /// Textverbesserer eingerichtet), gilt jede Regel als <see cref="OfflineRefineRule.Skip"/>.
    /// </summary>
    public static OfflineRefineRule Effective(this OfflineRefineRule rule, bool textModelFits) =>
        textModelFits ? rule : OfflineRefineRule.Skip;
}

/// <summary>Warum ein Diktat ohne KI kommt, obwohl eine Stufe an ist.</summary>
public enum RefineHint
{
    /// <summary>Kein Netz für die Online-Textverbesserung — es ging keine Anfrage raus.</summary>
    NoNet,

    /// <summary>Das lokale Textmodell fehlt.</summary>
    LocalMissing,
}

/// <summary>Wohin die Textverbesserung eines Diktats geht (<see cref="RefineDecision.Route"/>).</summary>
public abstract record RefineRoute
{
    /// <summary>Online. <paramref name="FallbackLocal"/>: scheitert die Anfrage, rechnet das (bereite) lokale Modell.</summary>
    public sealed record Online(bool FallbackLocal) : RefineRoute;

    /// <summary>Lokales Textmodell.</summary>
    public sealed record Local : RefineRoute;

    /// <summary>Ohne KI. <paramref name="Hint"/> = Hinweis; null = so gewollt (Stufe aus oder Überspringen).</summary>
    public sealed record Raw(RefineHint? Hint) : RefineRoute;
}

/// <summary>Reine Entscheidungstabelle der Textverbesserung (Port von <c>RefineDecision.kt</c>).</summary>
public static class RefineDecision
{
    /// <param name="stageActive">Die Stufe des Auftrags ist nicht „Aus“.</param>
    /// <param name="ownOnlineReady">Eigener, vollständiger Text-Zugang (nicht „wie Erkennung“ bei lokaler Erkennung).</param>
    /// <param name="network">Netz für den Text-Zugang da.</param>
    /// <param name="localReady">Lokales Textmodell bereit.</param>
    public static RefineRoute Route(Engine? engine, OfflineRefineRule rule, bool stageActive, bool ownOnlineReady, bool network, bool localReady)
    {
        if (!stageActive) return new RefineRoute.Raw(null);
        // Online-Erkennung: wie bisher online; fällt das Netz nach der Erkennung weg, sofort ohne KI.
        if (engine != Engine.Offline) return network ? new RefineRoute.Online(false) : new RefineRoute.Raw(RefineHint.NoNet);
        var online = ownOnlineReady && network;
        return rule switch
        {
            OfflineRefineRule.Local => LocalRoute(localReady),
            OfflineRefineRule.OnlineLocal => online ? new RefineRoute.Online(localReady) : LocalRoute(localReady),
            _ when online => new RefineRoute.Online(false),
            _ when ownOnlineReady => new RefineRoute.Raw(RefineHint.NoNet),
            _ => new RefineRoute.Raw(null),
        };
    }

    private static RefineRoute LocalRoute(bool ready) => ready ? new RefineRoute.Local() : new RefineRoute.Raw(RefineHint.LocalMissing);

    /// <summary>Können die KI-Stufen gerade etwas ausrichten? Online erkannt entscheidet der Zugang, lokal die Regel.</summary>
    public static bool StagesReady(Engine? engine, OfflineRefineRule rule, bool onlineReady, bool localReady)
    {
        if (engine != Engine.Offline) return onlineReady;
        return rule switch
        {
            OfflineRefineRule.Local => localReady,
            OfflineRefineRule.OnlineLocal => onlineReady || localReady,
            _ => onlineReady,
        };
    }

    /// <summary>„Lokales Textmodell fehlt“: lokal erkannt, eine KI-Stufe an, die Regel will lokal rechnen, Modell nicht bereit.</summary>
    public static bool LocalModelMissing(Engine? engine, RefineMode dictationMode, RefineMode shareMode, OfflineRefineRule rule, bool localReady) =>
        engine == Engine.Offline &&
        (dictationMode != RefineMode.Off || shareMode != RefineMode.Off) &&
        rule != OfflineRefineRule.Skip &&
        !localReady;
}
