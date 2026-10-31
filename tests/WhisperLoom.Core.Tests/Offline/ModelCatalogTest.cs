using System.Text.RegularExpressions;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>Port von <c>whisper/ModelCatalogTest.kt</c>: Werte sind fest verdrahtet, hier wird ihre Form abgesichert.</summary>
public sealed partial class ModelCatalogTest
{
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Hex64();

    [Fact]
    public void VierModelleInAnzeigeReihenfolgeMitEindeutigenIds()
    {
        Assert.Equal(["tiny", "base", "small", "large-v3-turbo"], ModelCatalog.Models.Select(m => m.Id));
        Assert.Equal(4, ModelCatalog.Models.Select(m => m.Id).Distinct().Count());
        Assert.Equal(4, ModelCatalog.Models.Select(m => m.FileName).Distinct().Count());
        Assert.Equal(4, ModelCatalog.Models.Select(m => m.Sha256).Distinct().Count());
    }

    [Fact]
    public void PruefsummenGroessenUndUrls()
    {
        foreach (var m in ModelCatalog.Models)
        {
            Assert.Matches(Hex64(), m.Sha256);
            Assert.True(m.Bytes > 0, m.Id);
            Assert.True(m.ApproxRamBytes > m.Bytes, m.Id);
            Assert.True(m.FileName.StartsWith("ggml-", StringComparison.Ordinal) && m.FileName.EndsWith(".bin", StringComparison.Ordinal), m.Id);
            Assert.Equal("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + m.FileName, m.Url);
            Assert.False(string.IsNullOrWhiteSpace(m.Label), m.Id);
        }
    }

    [Fact]
    public void WerteAusDerHuggingFaceApi()
    {
        Assert.Equal(32_152_673L, ModelCatalog.Tiny.Bytes);
        Assert.Equal(59_707_625L, ModelCatalog.Base.Bytes);
        Assert.Equal(190_085_487L, ModelCatalog.Small.Bytes);
        Assert.Equal(574_041_195L, ModelCatalog.LargeV3Turbo.Bytes);
        Assert.Equal("ggml-small-q5_1.bin", ModelCatalog.Small.FileName);
        Assert.Equal("ggml-large-v3-turbo-q5_0.bin", ModelCatalog.LargeV3Turbo.FileName);
        Assert.Equal("ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb", ModelCatalog.Small.Sha256);
    }

    [Fact]
    public void SmallIstEmpfehlungUndDefault()
    {
        Assert.Equal([ModelCatalog.Small], ModelCatalog.Models.Where(m => m.Recommended));
        Assert.Same(ModelCatalog.Small, ModelCatalog.Default);
        Assert.Equal(new OfflineSettings().Model, ModelCatalog.Default.Id);
        Assert.Same(ModelCatalog.Small, ModelCatalog.ById("gibt-es-nicht"));
        Assert.Same(ModelCatalog.Small, ModelCatalog.ById(null));
        Assert.Same(ModelCatalog.Base, ModelCatalog.Find("base"));
        Assert.Null(ModelCatalog.Find("medium"));
    }

    /// <summary>Kotlin prüft die IDs über whisper- und Text-Katalog; Desktop hat keinen Text-Katalog (Ollama) — IDs im Katalog eindeutig.</summary>
    [Fact]
    public void IdsUndDateienEindeutigUndFindbar()
    {
        foreach (var m in ModelCatalog.Models) Assert.Same(m, ModelCatalog.Find(m.Id));
        Assert.Null(ModelCatalog.Find(null));
    }

    [Fact]
    public void WhisperModelleBrauchenKeinenZusatzplatz()
    {
        foreach (var m in ModelCatalog.Models) Assert.Equal(0L, m.ExtraDiskBytes);
    }

    [Fact]
    public void NurLargeBrauchtMindestRam()
    {
        Assert.Equal(6L * 1024 * 1024 * 1024, ModelCatalog.LargeV3Turbo.MinDeviceRamBytes);
        foreach (var m in ModelCatalog.Models.Where(m => m != ModelCatalog.LargeV3Turbo)) Assert.Equal(0L, m.MinDeviceRamBytes);
    }
}
