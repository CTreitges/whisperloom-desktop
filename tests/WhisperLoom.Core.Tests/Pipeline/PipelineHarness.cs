using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Pipeline;

/// <summary>Keys im Speicher, Präfix-frei (nur Tests).</summary>
public sealed class InMemorySecrets : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];

    public string Kind => "Test";

    public Task<string?> GetAsync(string name, CancellationToken ct) => Task.FromResult(_values.GetValueOrDefault(name));

    public Task SetAsync(string name, string value, CancellationToken ct)
    {
        if (!SecretNames.All.Contains(name) || value.Length == 0) throw new ArgumentException("Name unbekannt oder Wert leer", nameof(name));
        _values[name] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct)
    {
        _values.Remove(name);
        return Task.CompletedTask;
    }

    public void Set(string name, string value) => _values[name] = value;
}

/// <summary>Offline-Erkennung als Fake (Kotlin: OfflineFake) — der Begleitprozess kommt in P4.</summary>
public sealed class FakeLocalStt(string text = "also ähm hallo welt") : ILocalStt
{
    public string Label => "Lokal · Small";

    public bool Ready { get; set; } = true;

    public int Calls { get; private set; }

    public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(Ready);

    public Task<TranscriptResult> TranscribeAsync(AudioBuffer audio, string language, CancellationToken ct)
    {
        Calls++;
        if (!Ready) throw new LocalNotAvailableException("Lokales Erkennungsmodell fehlt");
        return Task.FromResult(new TranscriptResult(text));
    }
}

/// <summary>Lokales Textmodell als Fake (Kotlin: FakeTextModel): Antwort einstellbar, Aufrufe gezählt, Halten und Abbruch.</summary>
public sealed class FakeLocalRefiner : ILocalRefiner
{
    public string ModelLabel => "Gemma 4 lokal";

    public bool Ready { get; set; } = true;

    public Func<string, string> Answer { get; set; } = _ => "Lokal verbessert.";

    public List<string> Calls { get; } = [];

    public int Cancels { get; private set; }

    public int ReadyChecks { get; private set; }

    /// <summary>Gesetzt: der nächste Aufruf wartet, bis abgebrochen wird.</summary>
    public bool HoldNext { get; set; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> IsReadyAsync(CancellationToken ct)
    {
        ReadyChecks++;
        return Task.FromResult(Ready);
    }

    public async Task<string> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct)
    {
        if (raw.Trim().Length == 0 || refinement.Mode == RefineMode.Off) return raw;
        lock (Calls) Calls.Add(raw);
        Started.TrySetResult();
        if (HoldNext)
        {
            HoldNext = false;
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                Cancels++;
                throw;
            }
        }
        return Answer(raw);
    }
}

/// <summary>
/// Gemeinsamer Aufbau der Pipeline-Tests (Kotlin: Prefs + Robolectric + JDK-HttpServer): Einstellungen, Keys, ein
/// lokaler Fake-Server, Netz als Schalter, lokale Erkennung und lokales Textmodell als Fakes.
/// </summary>
public sealed class PipelineHarness : IAsyncDisposable
{
    public static readonly AudioBuffer Speech = new(Enumerable.Repeat((short)9830, AudioBuffer.DefaultSampleRate).ToArray());

    public FakeServer Server { get; } = new();

    public HttpClient Http { get; } = WhisperLoom.Core.Api.Http.CreateClient();

    public WhisperLoomSettings Settings { get; } = new() { Language = "de" };

    public InMemorySecrets Secrets { get; } = new();

    public FakePromptBuilder Prompts { get; } = new();

    public RecordingPolisher Polisher { get; } = new();

    public ModelCache Cache { get; } = new();

    public FakeLocalStt LocalStt { get; } = new();

    public FakeLocalRefiner LocalRefiner { get; } = new();

    /// <summary>false = kein lokaler Textverbesserer eingerichtet (Kotlin: kein Textmodell passt ins Gerät).</summary>
    public bool HasLocalRefiner { get; set; } = true;

    public bool Net { get; set; } = true;

    public Func<string, bool, bool>? NetworkOverride { get; set; }

    public string Vocabulary { get; set; } = "";

    public List<string> Trimmed { get; } = [];

    public DictationPipeline Pipeline => new(
        () => Settings,
        Secrets,
        Http,
        Prompts,
        Polisher,
        new DictationPipelineOptions
        {
            LocalStt = LocalStt,
            LocalRefiner = HasLocalRefiner ? LocalRefiner : null,
            ModelCache = Cache,
            NetworkCheck = (url, proven) => NetworkOverride?.Invoke(url, proven) ?? Net,
            TrimSilence = a =>
            {
                Trimmed.Add("trim");
                return a;
            },
            VocabularyPrompt = _ => Vocabulary,
        });

    public string V1 => Server.Url("/v1");

    public Task<DictationResult> TranscribeAsync(RefineSkip? skip = null, Action? onStart = null, Refinement? refinement = null) =>
        Pipeline.RunAsync(
            new DictationRequest(Speech, Settings.Language, refinement ?? SettingsAccess.RefinementFor(Settings, RefineWay.Dictation)),
            skip,
            onStart,
            TestContext.Current.CancellationToken);

    /// <summary>Kotlin <c>prefs.refineMode = …</c>: die Stufe des Diktats.</summary>
    public void Stage(RefineMode mode)
    {
        Settings.Refine.Stage = mode.Key();
        if (mode == RefineMode.Prompt) Settings.Refine.PromptEnabled = true;
    }

    public void Online()
    {
        Settings.Engine = "online";
    }

    public void Offline(OfflineRefineRule rule)
    {
        Settings.Engine = "offline";
        Settings.Offline.Refine = rule.Key();
    }

    /// <summary>Erkennung über den eigenen Server (Fake), wie <c>useLocalServer</c>.</summary>
    public void UseLocalServer()
    {
        Online();
        Settings.Stt.ProviderId = "custom";
        Settings.Stt.BaseUrl = V1;
        Settings.Stt.Model = "whisper-1";
        Settings.Llm.Model = "qwen3:8b";
    }

    /// <summary>Eigener Text-Zugang (Fake-Server), wie <c>ownAccess</c>.</summary>
    public void OwnAccess()
    {
        Settings.Llm.ProviderId = "custom";
        Settings.Llm.BaseUrl = V1;
        Settings.Llm.Model = "qwen3:8b";
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await Server.DisposeAsync();
    }
}
