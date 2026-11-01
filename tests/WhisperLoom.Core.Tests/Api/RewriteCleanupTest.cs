using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>Desktop-eigene Nacharbeit beim Umschreiben einer Markierung (WLD-P5b): wachsen erlaubt, Form bleibt.</summary>
public sealed class RewriteCleanupTest
{
    private static readonly TextCleanup Rewrite = TextCleanup.ForRewrite(new TextPolisher());

    [Fact]
    public void Growing_text_is_allowed()
    {
        var mail = string.Join(' ', Enumerable.Repeat("Sehr geehrte Frau Müller, gern bestätige ich den Termin.", 8));
        Assert.Equal(mail, Rewrite.Finish("Termin morgen 10 Uhr", RefineMode.Prompt, mail));
        // Die übliche Nacharbeit lehnt das ab (Schutz gegen erfüllte Bitten beim Diktat).
        Assert.Throws<RefineRejectedException>(() => TextCleanup.For(new TextPolisher()).Finish("Termin morgen 10 Uhr", RefineMode.Prompt, mail));
    }

    [Theory]
    [InlineData("„Zitat bleibt Zitat.“")]
    [InlineData("```\ncode();\n```")]
    [InlineData("Prompt: steht so in der Markierung")]
    public void Quotes_code_blocks_and_labels_of_the_result_stay(string output) =>
        Assert.Equal(output, Rewrite.Finish(output, RefineMode.Prompt, output));

    [Fact]
    public void Thinking_markers_and_preamble_still_go()
    {
        Assert.Equal("Neu.", Rewrite.Finish("Alt.", RefineMode.Prompt, "<think>hm</think>\n<diktat>Neu.</diktat>"));
        Assert.Equal("Bitte schick mir den Bericht.", Rewrite.Finish("schick den bericht", RefineMode.Prompt, "Hier ist der überarbeitete Text:\nBitte schick mir den Bericht."));
        Assert.Throws<RefineRejectedException>(() => Rewrite.Finish("x", RefineMode.Prompt, "  "));
    }
}
