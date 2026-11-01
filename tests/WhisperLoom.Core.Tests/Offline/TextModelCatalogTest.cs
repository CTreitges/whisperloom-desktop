using WhisperLoom.Core.Local;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>W2-LOCAL: Katalog der mitgelieferten Textmodelle — freie Lizenz, fester Commit, RAM-Mindestwerte.</summary>
public sealed class TextModelCatalogTest
{
    [Fact]
    public void Every_model_is_pinned_and_freely_licensed()
    {
        Assert.All(TextModelCatalog.Models, m =>
        {
            Assert.Contains(m.License, new[] { "Apache-2.0", "MIT" });
            Assert.Matches("^https://huggingface\\.co/[^/]+/[^/]+/resolve/[0-9a-f]{40}/", m.File.Url);
            Assert.Matches("^[0-9a-f]{64}$", m.File.Sha256);
            Assert.EndsWith(".gguf", m.File.FileName, StringComparison.Ordinal);
            Assert.True(m.File.MinDeviceRamBytes > 0);
        });
        Assert.Single(TextModelCatalog.Models, m => m.File.Recommended);
    }

    [Theory]
    [InlineData("", "qwen2.5-1.5b")]
    [InlineData("gibts-nicht", "qwen2.5-1.5b")]
    [InlineData("qwen3-4b-2507", "qwen3-4b-2507")]
    public void Empty_or_unknown_id_means_the_recommendation(string id, string expected)
    {
        Assert.Equal(expected, TextModelCatalog.ById(id).Id);
    }

    [Theory]
    [InlineData(8L << 30, true, false)]
    [InlineData(16L << 30, true, true)]
    [InlineData(4L << 30, false, false)]
    [InlineData(0L, true, true)] // unbekannt: kein Verbot
    public void Ram_minimum_decides(long ram, bool small, bool big)
    {
        Assert.Equal(small, TextModelCatalog.Fits(TextModelCatalog.Qwen25_1_5B, ram));
        Assert.Equal(big, TextModelCatalog.Fits(TextModelCatalog.Qwen3_4B, ram));
    }
}
