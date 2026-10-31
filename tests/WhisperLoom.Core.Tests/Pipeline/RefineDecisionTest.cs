using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;
using static WhisperLoom.Core.Pipeline.OfflineRefineRule;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>Port von <c>RefineDecisionTest.kt</c>: die Entscheidungstabelle der Textverbesserung, jede Zeile ausgeschrieben.</summary>
public class RefineDecisionTest
{
    private static readonly RefineRoute Online = new RefineRoute.Online(FallbackLocal: false);
    private static readonly RefineRoute OnlineThenLocal = new RefineRoute.Online(FallbackLocal: true);
    private static readonly RefineRoute LocalRoute = new RefineRoute.Local();
    private static readonly RefineRoute NoNet = new RefineRoute.Raw(RefineHint.NoNet);
    private static readonly RefineRoute Missing = new RefineRoute.Raw(RefineHint.LocalMissing);
    private static readonly RefineRoute ChosenRaw = new RefineRoute.Raw(null);
    private static readonly bool[] Bools = [true, false];
    private static readonly Engine?[] AllEngines = [Engine.Online, Engine.Offline, null];

    private sealed record Case(OfflineRefineRule Rule, bool OwnOnline, bool Network, bool LocalReady, RefineRoute Expected);

    private static readonly Case[] OfflineCases =
    [
        new(OfflineRefineRule.Local, true, true, true, LocalRoute), new(OfflineRefineRule.Local, true, true, false, Missing),
        new(OfflineRefineRule.Local, true, false, true, LocalRoute), new(OfflineRefineRule.Local, true, false, false, Missing),
        new(OfflineRefineRule.Local, false, true, true, LocalRoute), new(OfflineRefineRule.Local, false, true, false, Missing),
        new(OfflineRefineRule.Local, false, false, true, LocalRoute), new(OfflineRefineRule.Local, false, false, false, Missing),
        new(OnlineLocal, true, true, true, OnlineThenLocal), new(OnlineLocal, true, true, false, Online),
        new(OnlineLocal, true, false, true, LocalRoute), new(OnlineLocal, true, false, false, Missing),
        new(OnlineLocal, false, true, true, LocalRoute), new(OnlineLocal, false, true, false, Missing),
        new(OnlineLocal, false, false, true, LocalRoute), new(OnlineLocal, false, false, false, Missing),
        new(Skip, true, true, true, Online), new(Skip, true, true, false, Online),
        new(Skip, true, false, true, NoNet), new(Skip, true, false, false, NoNet),
        new(Skip, false, true, true, ChosenRaw), new(Skip, false, true, false, ChosenRaw),
        new(Skip, false, false, true, ChosenRaw), new(Skip, false, false, false, ChosenRaw),
    ];

    [Fact]
    public void OfflineErkennungNachDerTabelle()
    {
        Assert.Equal(24, OfflineCases.Select(c => (c.Rule, c.OwnOnline, c.Network, c.LocalReady)).Distinct().Count());
        foreach (var c in OfflineCases)
        {
            Assert.Equal(c.Expected, RefineDecision.Route(Engine.Offline, c.Rule, true, c.OwnOnline, c.Network, c.LocalReady));
        }
    }

    [Fact]
    public void OnlineErkennungWieBisherNurMitNetz()
    {
        foreach (var engine in new Engine?[] { Engine.Online, null })
        {
            foreach (var rule in Enum.GetValues<OfflineRefineRule>())
            {
                foreach (var own in Bools)
                {
                    foreach (var ready in Bools)
                    {
                        Assert.Equal(Online, RefineDecision.Route(engine, rule, true, own, network: true, localReady: ready));
                        Assert.Equal(NoNet, RefineDecision.Route(engine, rule, true, own, network: false, localReady: ready));
                    }
                }
            }
        }
    }

    [Fact]
    public void StufeAusHeisstImmerOhneKiUndOhneHinweis()
    {
        foreach (var engine in AllEngines)
        {
            foreach (var rule in Enum.GetValues<OfflineRefineRule>())
            {
                foreach (var own in Bools)
                {
                    foreach (var net in Bools)
                    {
                        foreach (var ready in Bools) Assert.Equal(ChosenRaw, RefineDecision.Route(engine, rule, false, own, net, ready));
                    }
                }
            }
        }
    }

    [Fact]
    public void ModellFehltNurBeiOfflineMitStufeUndLokalerRegel()
    {
        RefineMode[] modes = [RefineMode.Off, RefineMode.Polish];
        var expectedTrue = new HashSet<(RefineMode, RefineMode, OfflineRefineRule)>
        {
            (RefineMode.Polish, RefineMode.Off, OfflineRefineRule.Local), (RefineMode.Off, RefineMode.Polish, OfflineRefineRule.Local), (RefineMode.Polish, RefineMode.Polish, OfflineRefineRule.Local),
            (RefineMode.Polish, RefineMode.Off, OnlineLocal), (RefineMode.Off, RefineMode.Polish, OnlineLocal), (RefineMode.Polish, RefineMode.Polish, OnlineLocal),
        };
        var count = 0;
        foreach (var engine in AllEngines)
        {
            foreach (var dictation in modes)
            {
                foreach (var share in modes)
                {
                    foreach (var rule in Enum.GetValues<OfflineRefineRule>())
                    {
                        foreach (var ready in Bools)
                        {
                            var missing = RefineDecision.LocalModelMissing(engine, dictation, share, rule, ready);
                            Assert.Equal(engine == Engine.Offline && !ready && expectedTrue.Contains((dictation, share, rule)), missing);
                            if (missing) count++;
                        }
                    }
                }
            }
        }
        Assert.Equal(6, count);
    }

    [Fact]
    public void JedeKiStufeZaehltFuerDiePflichtkarte()
    {
        foreach (var mode in Enum.GetValues<RefineMode>().Where(m => m != RefineMode.Off))
        {
            Assert.True(RefineDecision.LocalModelMissing(Engine.Offline, mode, RefineMode.Off, OfflineRefineRule.Local, false));
        }
    }

    [Fact]
    public void KiStufenBereitWennDieRouteNichtOhneKiMitGrundWaere()
    {
        Assert.True(RefineDecision.StagesReady(Engine.Online, OfflineRefineRule.Local, onlineReady: true, localReady: false));
        Assert.False(RefineDecision.StagesReady(Engine.Online, OfflineRefineRule.Local, onlineReady: false, localReady: true));
        Assert.False(RefineDecision.StagesReady(null, Skip, onlineReady: false, localReady: false));
        Assert.True(RefineDecision.StagesReady(Engine.Offline, OfflineRefineRule.Local, onlineReady: false, localReady: true));
        Assert.False(RefineDecision.StagesReady(Engine.Offline, OfflineRefineRule.Local, onlineReady: true, localReady: false));
        Assert.True(RefineDecision.StagesReady(Engine.Offline, OnlineLocal, onlineReady: true, localReady: false));
        Assert.True(RefineDecision.StagesReady(Engine.Offline, OnlineLocal, onlineReady: false, localReady: true));
        Assert.False(RefineDecision.StagesReady(Engine.Offline, OnlineLocal, onlineReady: false, localReady: false));
        Assert.True(RefineDecision.StagesReady(Engine.Offline, Skip, onlineReady: true, localReady: false));
        Assert.False(RefineDecision.StagesReady(Engine.Offline, Skip, onlineReady: false, localReady: true));
    }

    [Fact]
    public void RegelSchluesselUndDefault()
    {
        Assert.Equal(["local", "online_local", "skip"], Enum.GetValues<OfflineRefineRule>().Select(r => r.Key()));
        foreach (var r in Enum.GetValues<OfflineRefineRule>()) Assert.Equal(r, Engines.RuleFromKey(r.Key()));
        Assert.Equal(OfflineRefineRule.Local, Engines.RuleFromKey(null));
        Assert.Equal(OfflineRefineRule.Local, Engines.RuleFromKey("no_net"));
        Assert.Equal(OfflineRefineRule.Local, Engines.RuleFromKey(new WhisperLoom.Core.Contracts.Settings.OfflineSettings().Refine));
    }

    /// <summary>
    /// Kotlin prüft zusätzlich <c>TextModelCatalog.anyFits</c> (RAM des Android-Geräts). Auf dem Desktop „passt“ ein
    /// Textmodell, wenn ein lokaler Textverbesserer (Ollama, P4) eingerichtet ist — das prüft RefinePipelineTest.
    /// </summary>
    [Fact]
    public void PasstKeinTextmodellInsGeraetWirktJedeRegelWieUeberspringen()
    {
        foreach (var r in Enum.GetValues<OfflineRefineRule>()) Assert.Equal(r, r.Effective(textModelFits: true));
        foreach (var r in Enum.GetValues<OfflineRefineRule>()) Assert.Equal(Skip, r.Effective(textModelFits: false));
    }

    /// <summary>Desktop: Engine-Schlüssel der Einstellungen ("online" | "offline" | null).</summary>
    [Fact]
    public void EngineSchluessel()
    {
        Assert.Equal(Engine.Online, Engines.FromKey("online"));
        Assert.Equal(Engine.Offline, Engines.FromKey("offline"));
        Assert.Null(Engines.FromKey(null));
        Assert.Null(Engines.FromKey("quatsch"));
    }
}
