using WhisperLoom.App.Audio;
using WhisperLoom.App.Output;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests;

/// <summary>Mikrofon-Ersatz: liefert eine feste Anzahl Samples und einen festen Pegel.</summary>
internal sealed class FakeRecorder(int samples = 16_000, double level = 0.6) : IAudioRecorder
{
    public bool Running { get; private set; }

    public bool Unavailable { get; set; }

    public double Level => Running ? level : 0;

    public void Start(string? microphone)
    {
        if (Unavailable)
        {
            throw new AudioUnavailableException("kein Gerät");
        }
        Running = true;
    }

    public void Pause() => Running = false;

    public void Resume() => Running = true;

    public AudioBuffer Stop()
    {
        Running = false;
        return new AudioBuffer(new short[samples]);
    }

    public void Cancel() => Running = false;
}

internal sealed class CaptureSink : IOutputSink
{
    public List<string> Written { get; } = [];

    public string Kind => "capture";

    public Task WriteAsync(string text, CancellationToken ct)
    {
        Written.Add(text);
        return Task.CompletedTask;
    }
}

internal sealed class MemorySecretStore : ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];

    public string Kind => "Speicher";

    public Task<string?> GetAsync(string name, CancellationToken ct) => Task.FromResult(Values.GetValueOrDefault(name));

    public Task SetAsync(string name, string value, CancellationToken ct)
    {
        Values[name] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct)
    {
        Values.Remove(name);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAccessChecker : IAccessChecker
{
    public Task<AccessResult> CheckAsync(string providerId, string baseUrl, string? key, CancellationToken ct) =>
        Task.FromResult(new AccessResult(key is not null, key is null ? "Access_NoKey" : "Access_Ok", 200));
}

/// <summary>Ein Controller mit Fakes und Temp-Datenordner; räumt beim Dispose auf.</summary>
internal sealed class ControllerFixture : IDisposable
{
    public ControllerFixture(WhisperLoomSettings? settings = null)
    {
        DataDir = Path.Combine(Path.GetTempPath(), "wl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDir);
        Log = new AppLog(DataDir);
        Store = new SettingsStore(DataDir);
        var s = settings ?? new WhisperLoomSettings();
        History = new InMemoryHistoryStore(() => s.History);
        Controller = new DictationController(Recorder, Pipeline, Sink, History, Log, s, Store.Save, () => Now);
    }

    public string DataDir { get; }

    public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    public FakeRecorder Recorder { get; } = new();

    public FakeDictationPipeline Pipeline { get; } = new() { RawText = "Hallo Welt geheimes Diktat" };

    public CaptureSink Sink { get; } = new();

    public AppLog Log { get; }

    public SettingsStore Store { get; }

    public InMemoryHistoryStore History { get; }

    public DictationController Controller { get; }

    public string LogText => File.Exists(Log.FilePath) ? File.ReadAllText(Log.FilePath) : "";

    public void Dispose() => Directory.Delete(DataDir, recursive: true);
}
