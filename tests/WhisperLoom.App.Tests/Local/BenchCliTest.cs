using WhisperLoom.App.Cli;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests.Local;

/// <summary>Verb <c>bench</c> in der Befehlszeile, das Testsignal und der Abschnitt <c>local</c> der Einstellungen.</summary>
public sealed class BenchCliTest
{
    [Fact]
    public void BenchMitSchalternLaeuftLokal()
    {
        var r = CommandLine.Parse(["bench", "--model", "small", "--seconds", "10", "--data-dir", "x"]);
        Assert.Equal("bench", r.Verb);
        Assert.Equal(["--model", "small", "--seconds", "10"], r.Args);
        Assert.True(r.IsLocal);
        Assert.False(r.NeedsRunningInstance);
        Assert.Equal(Path.GetFullPath("x"), r.Options.DataDir);
    }

    [Theory]
    [InlineData("bench", "--seconds", "0")]
    [InlineData("bench", "--seconds", "abc")]
    [InlineData("bench", "--backend", "cuda")]
    [InlineData("status", "--model", "small")]
    [InlineData("bench", "--model")]
    public void FalscheBenchArgumente(params string[] args) => Assert.Throws<UsageException>(() => CommandLine.Parse(args));

    [Fact]
    public void TestsignalHatDieVerlangteLaenge()
    {
        var audio = Bench.Audio(null, 2.5);
        Assert.Equal(2_500, audio.DurationMs);
        Assert.Contains(audio.Samples.ToArray(), s => Math.Abs((int)s) > 1_000);
    }

    [Fact]
    public void LocalDirLiegtImDatenordner() =>
        Assert.Equal(Path.Combine(Path.GetFullPath("d"), "local"), Bench.LocalDir(new AppOptions(Path.GetFullPath("d"))));

    [Fact]
    public void LokaleEinstellungenStandardUndRundreise()
    {
        var s = new WhisperLoomSettings();
        var local = LocalSettings.Of(s);
        Assert.Equal(("", "auto", 10), (local.OllamaModel, local.Backend, local.IdleMinutes));
        LocalSettings.Set(s, new LocalSettings { OllamaModel = "qwen3:4b", Backend = "cpu", IdleMinutes = 3 });
        var back = LocalSettings.Of(SettingsJson.Deserialize(SettingsJson.Serialize(s)));
        Assert.Equal(("qwen3:4b", "cpu", 3), (back.OllamaModel, back.Backend, back.IdleMinutes));
        // Der Vertrag bleibt unberührt: das Android-Feld behält seine LiteRT-ID.
        Assert.Equal("gemma4_e2b", s.Offline.LocalLlmModel);
    }

    [Fact]
    public void KaputterAbschnittGiltAlsStandard()
    {
        var s = SettingsJson.Deserialize("""{"local":{"backend":5}}""");
        Assert.Equal("auto", LocalSettings.Of(s).Backend);
    }
}
