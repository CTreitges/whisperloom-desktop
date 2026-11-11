using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>
/// TS-WSTUFEN: eine einzige Stufen-Auflösung im Core (<see cref="SettingsAccess.DictationStage"/>) mit dem sichtbaren
/// Verhalten der App. Die Erwartung stammt aus dem alten App-Code (<c>DictationController.StageOf/RefinementOf</c>,
/// Golden-Datei <c>tests/fixtures/dictation-stage-golden.txt</c>, 432 Kombinationen). Geändert gegenüber dem alten Core:
/// gespeichertes „readable“ ergibt jetzt Glätten statt „Lesbar“ (das wirksame <see cref="Refinement"/> war schon gleich).
/// </summary>
public sealed class DictationStageTableTest
{
    private static readonly string Golden = Path.Combine(AppContext.BaseDirectory, "fixtures", "dictation-stage-golden.txt");

    public static TheoryData<string> Rows()
    {
        var data = new TheoryData<string>();
        foreach (var line in File.ReadAllLines(Golden).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            data.Add(line);
        }
        return data;
    }

    [Fact]
    public void Golden_table_covers_every_stage_and_mode()
    {
        var rows = File.ReadAllLines(Golden).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        Assert.Equal(432, rows.Count);
        Assert.Equal(
            ["", "beautify", "bogus", "off", "paragraphs", "polish", "prompt", "readable", "summarize"],
            rows.Select(r => r.Split('|')[0]).Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Stage_and_refinement_match_the_app(string row)
    {
        var c = row.Split('|');
        var refine = new RefineSettings
        {
            Stage = c[0],
            PromptEnabled = bool.Parse(c[1]),
            PolishCleanup = c[2],
            ParagraphsPolish = bool.Parse(c[3]),
            ParagraphsBeautify = bool.Parse(c[4]),
            SummarizeForm = c[5],
        };
        var settings = new WhisperLoomSettings { Refine = refine };

        Assert.Equal(c[6], SettingsAccess.DictationStage(refine).Key());
        Assert.Equal(c[7], SettingsAccess.DictationRefinement(refine).ToString());
        Assert.Equal(c[7], SettingsAccess.RefinementFor(settings, RefineWay.Dictation).ToString());
    }

    [Theory]
    [InlineData("readable", false, RefineMode.Polish)] // der geänderte Core-Fall (vorher Readable)
    [InlineData("readable", true, RefineMode.Polish)]
    [InlineData("paragraphs", false, RefineMode.Polish)]
    [InlineData("prompt", false, RefineMode.Polish)]
    [InlineData("prompt", true, RefineMode.Prompt)]
    [InlineData("unbekannt", false, RefineMode.Off)]
    public void Stored_stage_resolves_like_the_app(string stored, bool promptEnabled, RefineMode expected)
    {
        Assert.Equal(expected, SettingsAccess.DictationStage(new RefineSettings { Stage = stored, PromptEnabled = promptEnabled }));
    }
}
