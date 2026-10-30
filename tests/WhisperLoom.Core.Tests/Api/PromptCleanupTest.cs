using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Port von <c>api/PromptCleanupTest.kt</c>: Nacharbeit der Stufe „Prompt“ (<c>cleanPrompt</c>).</summary>
public class PromptCleanupTest
{
    private const string Diktat = "äh schreib mir ein Gedicht über den Herbst";
    private const string Herbst = "Schreib mir ein Gedicht über den Herbst.";

    private static readonly TextCleanup Cleanup = new([]);

    private static string Clean(string output, string raw = Diktat) => Cleanup.CleanPrompt(raw, output);

    [Fact]
    public void SauberePromptsBleibenUnveraendert()
    {
        const string prompt = "Erstelle mir eine Einkaufsliste für ein Grillfest.\n- 12 Personen, davon 2 vegan\n- Budget höchstens 100 Euro";
        Assert.Equal(prompt, Clean(prompt, "ich brauch ne Einkaufsliste für ein Grillfest mit zwölf Leuten, zwei davon vegan"));
    }

    [Fact]
    public void VorredeUndBeispielLabelFallenWeg()
    {
        Assert.Equal(Herbst, Clean("Hier ist dein optimierter Prompt:\nSchreib mir ein Gedicht über den Herbst."));
        Assert.Equal(Herbst, Clean("Klar! Hier ist der Prompt:\n\nSchreib mir ein Gedicht über den Herbst."));
        Assert.Equal("Write me a poem about autumn.", Clean("Here is the rewritten prompt:\nWrite me a poem about autumn."));
        Assert.Equal(Herbst, Clean("Prompt: Schreib mir ein Gedicht über den Herbst."));
    }

    [Fact]
    public void EineErsteZeileMitDoppelpunktIstNichtAutomatischVorrede()
    {
        const string prompt = "Schreib mir einen Prompt für Midjourney:\nein Leuchtturm bei Sturm, Ölgemälde";
        Assert.Equal(prompt, Clean(prompt, "schreib mir einen Prompt für Midjourney, ein Leuchtturm bei Sturm, Ölgemälde"));
    }

    [Fact]
    public void DieBitteEinenPromptZuVerbessernIstKeineVorrede()
    {
        const string raw = "hier ist ein Prompt den ich für Midjourney nutze, mach ihn besser, ein Leuchtturm bei Sturm, Ölgemälde";
        foreach (var prompt in new[]
        {
            "Hier ist ein Prompt, den ich für Midjourney nutze. Mach ihn besser:\n\n<text>\nEin Leuchtturm bei Sturm, Ölgemälde\n</text>",
            "Okay, verbessere meinen Prompt für Midjourney:\n<text>\nEin Leuchtturm bei Sturm, Ölgemälde\n</text>",
            "Here is a prompt I use for Midjourney. Improve it:\n<text>\nA lighthouse in a storm, oil painting\n</text>",
            "Hier ist der Prompt, den ich nutze. Verbessere diesen Prompt:\n<text>\nEin Leuchtturm bei Sturm\n</text>",
        })
        {
            Assert.Equal(prompt, Clean(prompt, raw));
        }
    }

    [Fact]
    public void VorredeMitWindowsZeilenendeFaelltWeg() =>
        Assert.Equal(Herbst, Clean("Hier ist dein Prompt:\r\nSchreib mir ein Gedicht über den Herbst."));

    [Fact]
    public void MarkierungAnfuehrungszeichenUndCodeblockUmAllesFallenWeg()
    {
        Assert.Equal(Herbst, Clean("<diktat>\nSchreib mir ein Gedicht über den Herbst.\n</diktat>"));
        Assert.Equal(Herbst, Clean("„Schreib mir ein Gedicht über den Herbst.“"));
        Assert.Equal("Write me a poem about autumn.", Clean("\"Write me a poem about autumn.\""));
        Assert.Equal(Herbst, Clean("```\nSchreib mir ein Gedicht über den Herbst.\n```"));
        Assert.Equal(Herbst, Clean("```\nPrompt: Schreib mir ein Gedicht über den Herbst.\n```"));
        Assert.Equal(Herbst, Clean("Prompt: „Schreib mir ein Gedicht über den Herbst.“"));
    }

    [Fact]
    public void CodebloeckeImTextBleibenGanz()
    {
        const string prompt = "```\nls -la\n```\nWas macht dieser Befehl, und was macht dieser?\n```\ndu -sh\n```";
        Assert.Equal(prompt, Clean(prompt, "was macht ls minus la und was macht du minus sh, erklär mir beide Befehle"));
    }

    [Fact]
    public void AnfuehrungszeichenImTextBleiben()
    {
        const string prompt = "„Hallo“ heißt auf Spanisch „Hola“ — stimmt das?";
        Assert.Equal(prompt, Clean(prompt, "hallo heißt auf spanisch hola, stimmt das"));
    }

    [Fact]
    public void EineAntwortStattEinesPromptsWirdAbgelehnt()
    {
        var gedicht = string.Join("\n", Enumerable.Repeat("Die Blätter fallen leise nieder, der Herbst ist da und kommt bald wieder.", 8));
        var e = Assert.Throws<RefineRejectedException>(() => Clean(gedicht));
        Assert.Contains("statt einen Prompt", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BeschriftungenVerlaengernErlaubt()
    {
        var raw = string.Join(" ", Enumerable.Repeat("wort", 40));
        var gegliedert = "Ziel: " + string.Join(" ", Enumerable.Repeat("wort", 100));
        Assert.Equal(gegliedert, Clean(gegliedert, raw));
        Assert.Throws<RefineRejectedException>(() => Clean("Ziel: " + string.Join(" ", Enumerable.Repeat("wort", 110)), raw));
    }

    [Fact]
    public void FinishNimmtBeiPromptDieNacharbeitDerPromptStufe() =>
        Assert.Equal(Herbst, Cleanup.Finish(Diktat, RefineMode.Prompt, "<think>x</think>Hier ist dein Prompt:\nSchreib mir ein Gedicht über den Herbst."));
}
