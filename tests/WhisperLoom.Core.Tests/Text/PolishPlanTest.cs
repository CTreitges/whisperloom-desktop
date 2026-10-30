using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Tests.Text;

/// <summary>Port von <c>PolishPlanTest.kt</c>: welche Nachbearbeitungs-Stufe greift.</summary>
public class PolishPlanTest
{
    private static readonly TextPolisher Polisher = new();

    private static PolishOptions Plan(
        bool removeFillers = true,
        RefineMode refineMode = RefineMode.Off,
        IReadOnlyCollection<string>? customFillers = null,
        IReadOnlySet<string>? disabledFillers = null,
        bool paragraphs = true) =>
        PolishPlan.Options(
            removeFillers: removeFillers,
            autoCapitalize: true,
            language: "de",
            refineMode: refineMode,
            customFillers: customFillers,
            disabledFillers: disabledFillers,
            paragraphs: paragraphs);

    [Fact]
    public void OhneAutomatischeAbsaetzeWirdKiTextZuEinemAbsatz()
    {
        Assert.True(Plan(refineMode: RefineMode.Polish, paragraphs: true).KeepLineBreaks);
        Assert.False(Plan(refineMode: RefineMode.Polish, paragraphs: false).KeepLineBreaks);
        Assert.False(Plan(refineMode: RefineMode.Off, paragraphs: true).KeepLineBreaks);
        var flat = Polisher.Polish("Erster Absatz.\n\nZweiter Absatz.", Plan(refineMode: RefineMode.Beautify, paragraphs: false));
        Assert.Equal("Erster Absatz. Zweiter Absatz.", flat);
    }

    [Fact]
    public void PromptBehaeltSeineGliederungAuchOhneAutomatischeAbsaetze()
    {
        var options = Plan(refineMode: RefineMode.Prompt, paragraphs: false);
        Assert.True(options.KeepLineBreaks);
        const string prompt = "Erstelle mir eine Einkaufsliste.\n- 12 Personen, davon 2 vegan\n- Budget höchstens 100 Euro";
        Assert.Equal(prompt, Polisher.Polish(prompt, options));
    }

    /// <summary>Review-Befund: aus <c>&lt;/text&gt;</c> nach einem Punkt wurde <c>&lt;/Text&gt;</c>, Füllwörter im Material verschwanden.</summary>
    [Fact]
    public void PromptMaterialBleibtUnangetastet()
    {
        var options = Plan(refineMode: RefineMode.Prompt, customFillers: ["halt"]);
        Assert.False(options.AutoCapitalize, "Groß-Schreibung erledigt das Modell");
        Assert.False(options.RemoveFillers, "Füllwörter erledigt das Modell");
        const string prompt = "Fasse den Text zusammen.\n\n<text>\nDas Meeting war halt gut. wir machen weiter.\n</text>";
        Assert.Equal(prompt, Polisher.Polish(prompt, options));
    }

    [Fact]
    public void OhneKiGreiftDieWortliste()
    {
        Assert.True(Plan(removeFillers: true).RemoveFillers);
    }

    [Fact]
    public void AusGeschaltetBleibtAus()
    {
        Assert.False(Plan(removeFillers: false).RemoveFillers);
    }

    /// <summary>
    /// 3.9.0 (Plan §4): die Liste ist das Sicherheitsnetz nach jeder KI-Stufe außer "Prompt" — auch
    /// wenn die KI selbst über Füllwörter entscheidet.
    /// </summary>
    [Fact]
    public void DieWortlisteLaeuftNachJederKiStufeAusserPrompt()
    {
        foreach (var mode in new[] { RefineMode.Polish, RefineMode.Readable, RefineMode.Beautify, RefineMode.Summarize })
        {
            var o = Plan(removeFillers: true, refineMode: mode);
            Assert.True(o.RemoveFillers, mode.ToString());
            Assert.Equal("Also, ich komme morgen.", Polisher.Polish("Also, ähm, ich komme morgen.", o));
        }
        Assert.False(Plan(removeFillers: true, refineMode: RefineMode.Prompt).RemoveFillers);
    }

    /// <summary>E6: eigene Wörter trägt der Nutzer bewusst ein — sie laufen nach jeder KI-Stufe mit.</summary>
    [Fact]
    public void EigeneWoerterLaufenNachDerKiMit()
    {
        foreach (var mode in new[] { RefineMode.Polish, RefineMode.Beautify, RefineMode.Summarize })
        {
            var o = Plan(refineMode: mode, customFillers: ["halt"]);
            Assert.Equal("Das ist gut.", Polisher.Polish("Das ist halt gut.", o));
        }
    }

    [Fact]
    public void LesbarerGlaettenBehaeltDieWortlisteAlsNetz()
    {
        // Review: gemma3:4b ließ bei "Lesbar" ein "ähm" stehen — die Liste fängt es (wie beim Glätten, samt eigener Wörter).
        var o = Plan(removeFillers: true, refineMode: RefineMode.Readable);
        Assert.True(o.RemoveFillers);
        Assert.Equal("Also, ich komme morgen.", Polisher.Polish("Also, ähm, ich komme morgen.", o));
        Assert.True(o.AutoCapitalize, "Groß-Schreibung wie beim Glätten");
        Assert.True(o.KeepLineBreaks);
        Assert.False(Plan(refineMode: RefineMode.Readable, paragraphs: false).KeepLineBreaks);
    }

    [Fact]
    public void AusgeschalteteListeBleibtAuchNachDerKiAus()
    {
        foreach (var mode in Enum.GetValues<RefineMode>())
        {
            Assert.False(Plan(removeFillers: false, refineMode: mode).RemoveFillers, mode.ToString());
        }
    }

    [Fact]
    public void KiTextBehaeltSeineAbsaetze()
    {
        Assert.False(Plan().KeepLineBreaks);
        Assert.True(Plan(refineMode: RefineMode.Polish).KeepLineBreaks);
        Assert.True(Plan(refineMode: RefineMode.Summarize).KeepLineBreaks);
    }

    [Fact]
    public void UebrigeOptionenWerdenDurchgereicht()
    {
        var o = Plan(customFillers: ["halt"], disabledFillers: new HashSet<string> { "hmm" });
        Assert.True(o.AutoCapitalize);
        Assert.Equal("de", o.Language);
        Assert.Equal(["halt"], o.CustomFillers!.ToList());
        Assert.Equal(new HashSet<string> { "hmm" }, o.DisabledFillers);
    }

    [Fact]
    public void VerbatimUndCleaned()
    {
        var v = PolishPlan.Verbatim("de");
        Assert.False(v.RemoveFillers);
        Assert.False(v.AutoCapitalize);
        var c = PolishPlan.Cleaned("de", ["halt"], new HashSet<string> { "hmm" });
        Assert.True(c.RemoveFillers);
        Assert.False(c.AutoCapitalize);
        Assert.Equal(["halt"], c.CustomFillers!.ToList());
        Assert.Equal(new HashSet<string> { "hmm" }, c.DisabledFillers);
    }

    [Fact]
    public void FuellwoerterWerdenNormalisiert()
    {
        Assert.Equal(new HashSet<string> { "ähm", "halt", "sozusagen" }, PolishPlan.ParseFillers("Ähm, halt,sozusagen ; HALT\n"));
        Assert.Empty(PolishPlan.ParseFillers("  , ,\n"));
        Assert.Equal(new HashSet<string> { "halt" }, PolishPlan.NormalizeFillers([" Halt ", "halt", ""]));
    }
}
