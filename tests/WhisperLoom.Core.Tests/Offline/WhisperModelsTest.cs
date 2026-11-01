using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.Core.Tests.Offline;

/// <summary>W2-LOCAL: eigene Whisper-Datei (<c>offline.model = "custom:&lt;name&gt;"</c>), Prüfung auf ggml/gguf-Magic.</summary>
public sealed class WhisperModelsTest : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wl-models-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, byte[] head, long length = WhisperModels.MinBytes)
    {
        var path = Path.Combine(_dir, name);
        using var f = File.Create(path);
        f.Write(head);
        f.SetLength(length);
        return path;
    }

    private static OfflineSettings Custom(string name, string path) => new()
    {
        Model = OfflineSettings.CustomModelPrefix + name,
        CustomModels = [new CustomWhisperModel { Name = name, Path = path }],
    };

    [Theory]
    [InlineData("lmgg")]
    [InlineData("GGUF")]
    public void Ggml_and_gguf_files_are_models(string magic)
    {
        var path = Write("m.bin", System.Text.Encoding.ASCII.GetBytes(magic));

        var choice = WhisperModels.Resolve(Custom("mein", path), new ModelStore(_dir));

        Assert.Null(choice.Error);
        Assert.Equal(path, choice.Path);
        Assert.Null(choice.Catalog);
        Assert.Equal("mein", choice.Label);
    }

    [Fact]
    public void Other_file_is_a_clear_error()
    {
        var path = Write("m.bin", "PK\u0003\u0004"u8.ToArray());

        var choice = WhisperModels.Resolve(Custom("zip", path), new ModelStore(_dir));

        Assert.Equal(WhisperModels.MsgNoModel("zip"), choice.Error);
    }

    [Fact]
    public void Tiny_file_with_magic_is_no_model()
    {
        Assert.Equal(WhisperModels.FileCheck.NotAModel, WhisperModels.CheckFile(Write("t.bin", "GGUF"u8.ToArray(), 100)));
    }

    [Fact]
    public void Missing_file_and_missing_entry_are_errors_not_small()
    {
        var missing = WhisperModels.Resolve(Custom("weg", Path.Combine(_dir, "nix.bin")), new ModelStore(_dir));
        var unlisted = WhisperModels.Resolve(new OfflineSettings { Model = "custom:unbekannt" }, new ModelStore(_dir));

        Assert.Equal(WhisperModels.MsgFileMissing("weg"), missing.Error);
        Assert.Equal(WhisperModels.MsgNotListed("unbekannt"), unlisted.Error);
        Assert.Null(unlisted.Catalog);
    }

    [Fact]
    public void Unknown_catalog_id_still_means_small()
    {
        var choice = WhisperModels.Resolve(new OfflineSettings { Model = "medium" }, new ModelStore(_dir));

        Assert.Same(ModelCatalog.Small, choice.Catalog);
        Assert.Equal(WhisperModels.MsgModelMissing, choice.Error);
    }

    [Fact]
    public void Suggested_name_is_unique()
    {
        var existing = new[] { new CustomWhisperModel { Name = "ggml-medium" } };

        Assert.Equal("ggml-medium (2)", WhisperModels.SuggestName("D:/m/ggml-medium.bin", existing));
        Assert.Equal("distil", WhisperModels.SuggestName("D:/m/distil.gguf", existing));
    }
}
