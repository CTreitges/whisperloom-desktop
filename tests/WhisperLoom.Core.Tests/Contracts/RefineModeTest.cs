using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Tests.Contracts;

/// <summary>Schlüssel und Stufen-Listen von <see cref="RefineMode"/> (Android: <c>RefineMode.kt</c>, ohne eigene Testklasse).</summary>
public class RefineModeTest
{
    [Fact]
    public void SchluesselWieAndroid()
    {
        Assert.Equal(
            ["off", "polish", "beautify", "summarize", "readable", "prompt"],
            Enum.GetValues<RefineMode>().Select(m => m.Key()));
        Assert.Equal(RefineMode.Prompt, RefineModes.FromKey("prompt"));
        Assert.Equal(RefineMode.Off, RefineModes.FromKey(null));
        Assert.Equal(RefineMode.Off, RefineModes.FromKey("paragraphs"));
    }

    [Fact]
    public void LesbarRechnetMitDemGlaettenModell()
    {
        Assert.Null(RefineMode.Off.ModelStage());
        Assert.Equal(RefineMode.Polish, RefineMode.Readable.ModelStage());
        Assert.Equal(RefineMode.Prompt, RefineMode.Prompt.ModelStage());
    }

    [Fact]
    public void PromptNurWennEingeschaltet()
    {
        Assert.DoesNotContain(RefineMode.Prompt, RefineModes.SettingsFor(promptEnabled: false));
        Assert.Equal(RefineMode.Prompt, RefineModes.SettingsFor(promptEnabled: true)[^1]);
    }
}
