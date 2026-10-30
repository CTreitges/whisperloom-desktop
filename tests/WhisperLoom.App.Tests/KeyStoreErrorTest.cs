using WhisperLoom.App.Services;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests;

/// <summary>Key-Ablage, die beim Lesen scheitert (Secret Service weg, Credential Manager gesperrt).</summary>
internal sealed class BrokenSecretStore : ISecretStore
{
    public string Kind => "Test";

    public Task<string?> GetAsync(string name, CancellationToken ct) => throw new IOException("Key-Ablage nicht erreichbar");

    public Task SetAsync(string name, string value, CancellationToken ct) => throw new IOException("Key-Ablage nicht erreichbar");

    public Task DeleteAsync(string name, CancellationToken ct) => throw new IOException("Key-Ablage nicht erreichbar");
}

/// <summary>Prüfbefund R1 (WLD-INT): Eine IOException der Key-Ablage während eines Diktats wird zur Meldung, nicht zum Absturz.</summary>
public sealed class KeyStoreErrorTest : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("wl-keystore-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    [Fact]
    public async Task Unreachable_key_store_ends_the_dictation_with_a_message()
    {
        var settings = new WhisperLoomSettings { Engine = "online" };
        var log = new AppLog(_dataDir);
        var sink = new CaptureSink();
        using var pipeline = new AppPipeline(() => settings, new BrokenSecretStore(), new ModelCache());
        var controller = new DictationController(new FakeRecorder(), pipeline, sink, new MemoryHistory(), log, settings, _ => { });

        controller.Start();
        Assert.Equal("error", await controller.StopAsync());

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal("Key_StoreError", controller.LastErrorKey);
        Assert.Equal("IOException", controller.LastErrorClass);
        Assert.Empty(sink.Written);
        Assert.Contains("dictation status=error errorClass=IOException", File.ReadAllText(log.FilePath));
    }

    private sealed class MemoryHistory : IHistoryStore
    {
        public Task AddAsync(HistoryEntry entry, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<HistoryEntry>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<HistoryEntry>>([]);

        public Task<HistoryEntry?> GetAsync(string id, CancellationToken ct) => Task.FromResult<HistoryEntry?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken ct) => Task.FromResult(false);

        public Task ClearAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
