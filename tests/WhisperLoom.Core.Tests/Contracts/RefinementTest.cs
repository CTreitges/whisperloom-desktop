using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Contracts;

/// <summary>Port von <c>RefinementTest.kt</c>: die wirksame Verarbeitung je Stufe und Weg.</summary>
public class RefinementTest
{
    private static Refinement Of(
        RefineMode stage,
        RefineWay way = RefineWay.Dictation,
        PolishCleanup cleanup = PolishCleanup.Plain,
        bool paragraphs = true,
        SummarizeForm form = SummarizeForm.Auto) =>
        Refinement.Of(way, stage, cleanup, paragraphs, form);

    [Fact]
    public void DieDreiBereinigungenSindDieDreiGlaettenVarianten()
    {
        Assert.Equal(new Refinement(RefineMode.Polish, SmartFillers: false), Of(RefineMode.Polish, cleanup: PolishCleanup.Plain));
        Assert.Equal(new Refinement(RefineMode.Polish, SmartFillers: true), Of(RefineMode.Polish, cleanup: PolishCleanup.Clean));
        Assert.Equal(new Refinement(RefineMode.Readable), Of(RefineMode.Polish, cleanup: PolishCleanup.Readable));
    }

    [Fact]
    public void DieBereinigungZaehltNurBeimGlaetten()
    {
        foreach (var stage in new[] { RefineMode.Beautify, RefineMode.Summarize, RefineMode.Prompt })
        {
            foreach (var cleanup in Enum.GetValues<PolishCleanup>())
            {
                var r = Of(stage, cleanup: cleanup);
                Assert.Equal(stage, r.Mode);
                Assert.False(r.SmartFillers, $"Verschönern ohne Füllwort-Zusatz: {stage}");
            }
        }
        foreach (var way in Enum.GetValues<RefineWay>())
        {
            foreach (var cleanup in Enum.GetValues<PolishCleanup>())
            {
                Assert.Equal(Refinement.Off, Of(RefineMode.Off, way, cleanup));
            }
        }
    }

    [Fact]
    public void AbsaetzeJeStufeBeimDiktat()
    {
        Assert.False(Of(RefineMode.Polish, paragraphs: false).Paragraphs);
        Assert.False(Of(RefineMode.Polish, cleanup: PolishCleanup.Readable, paragraphs: false).Paragraphs);
        Assert.False(Of(RefineMode.Beautify, paragraphs: false).Paragraphs);
        Assert.True(Of(RefineMode.Beautify, paragraphs: true).Paragraphs);
        // „Prompt“ gliedert immer, Zusammenfassen nach der Form — der Schalter zählt dort nicht.
        Assert.Equal(new Refinement(RefineMode.Prompt), Of(RefineMode.Prompt, paragraphs: false));
        Assert.True(Of(RefineMode.Summarize, paragraphs: false).Paragraphs);
    }

    [Fact]
    public void SprachnachrichtenSindImmerGegliedert()
    {
        foreach (var stage in new[] { RefineMode.Polish, RefineMode.Beautify })
        {
            Assert.True(Of(stage, RefineWay.Share, paragraphs: false).Paragraphs, stage.ToString());
        }
        Assert.Equal(new Refinement(RefineMode.Readable), Of(RefineMode.Polish, RefineWay.Share, PolishCleanup.Readable, paragraphs: false));
    }

    [Fact]
    public void DieFormBestimmtDenZweigBeimZusammenfassen()
    {
        foreach (var way in Enum.GetValues<RefineWay>())
        {
            Assert.Equal(new Refinement(RefineMode.Summarize, Paragraphs: true), Of(RefineMode.Summarize, way, form: SummarizeForm.Auto));
            Assert.Equal(new Refinement(RefineMode.Summarize, Paragraphs: false), Of(RefineMode.Summarize, way, form: SummarizeForm.Prose));
        }
        // Die Form zählt nur dort.
        Assert.Equal(new Refinement(RefineMode.Beautify), Of(RefineMode.Beautify, form: SummarizeForm.Prose));
    }

    [Fact]
    public void EinGespeichertesLesbarGiltAlsGlaetten()
    {
        // READABLE ist nie wählbar; stünde es doch im Speicher, entscheidet die Bereinigung.
        Assert.Equal(new Refinement(RefineMode.Polish), Of(RefineMode.Readable, cleanup: PolishCleanup.Plain));
    }

    [Fact]
    public void SchluesselSindFest()
    {
        Assert.Equal(["plain", "clean", "readable"], Enum.GetValues<PolishCleanup>().Select(c => c.Key()));
        Assert.Equal(["auto", "prose"], Enum.GetValues<SummarizeForm>().Select(f => f.Key()));
        Assert.Equal(PolishCleanup.Plain, RefineKeys.CleanupFromKey(null));
        Assert.Equal(PolishCleanup.Clean, RefineKeys.CleanupFromKey("clean"));
        Assert.Equal(SummarizeForm.Auto, RefineKeys.FormFromKey("quatsch"));
        Assert.Equal(SummarizeForm.Prose, RefineKeys.FormFromKey("prose"));
    }
}
