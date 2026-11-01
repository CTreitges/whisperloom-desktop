using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Tests.Local;

/// <summary>
/// W2-LOCAL Regression: eine kaputte oder fehlende eigene Whisper-Datei fiel bisher still auf Small zurück
/// (<c>ModelCatalog.ById("custom:…")</c> = Small). Jetzt: nicht bereit und klare Meldung, auch wenn Small installiert ist.
/// </summary>
public sealed class CustomWhisperModelTest : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wl-custom-stt-").FullName;

    public CustomWhisperModelTest()
    {
        // Small „installiert“: Datei in Katalog-Größe (Inhalt egal, gestartet wird nichts).
        Directory.CreateDirectory(Path.Combine(_dir, "models"));
        using var small = File.Create(Path.Combine(_dir, "models", ModelCatalog.Small.FileName));
        small.SetLength(ModelCatalog.Small.Bytes);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private WhisperLoomSettings Settings(string? fileContent)
    {
        var path = Path.Combine(_dir, "eigen.bin");
        if (fileContent is not null) File.WriteAllText(path, fileContent);
        var s = new WhisperLoomSettings { Engine = "offline" };
        s.Offline.Model = "custom:eigen";
        s.Offline.CustomModels.Add(new CustomWhisperModel { Name = "eigen", Path = path });
        return s;
    }

    [Theory]
    [InlineData("kein Modell, nur Text")]
    [InlineData(null)]
    public async Task Broken_or_missing_custom_file_is_not_ready_and_never_small(string? content)
    {
        var settings = Settings(content);
        await using var stt = new LocalStt(_dir, () => settings);

        Assert.False(await stt.IsReadyAsync(CancellationToken.None));
        var e = await Assert.ThrowsAsync<LocalNotAvailableException>(() => stt.EnsureServerAsync(CancellationToken.None));
        Assert.Contains("eigen", e.Message, StringComparison.Ordinal);
        Assert.Null(stt.Server);
    }

    [Fact]
    public async Task Catalog_model_stays_ready()
    {
        var settings = new WhisperLoomSettings { Engine = "offline" };
        settings.Offline.Model = "small";
        await using var stt = new LocalStt(_dir, () => settings);

        Assert.True(await stt.IsReadyAsync(CancellationToken.None));
        Assert.Equal(Path.Combine(_dir, "models", ModelCatalog.Small.FileName), stt.Model().Path);
    }
}
