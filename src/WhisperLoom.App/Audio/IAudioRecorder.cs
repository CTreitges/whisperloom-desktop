using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Audio;

/// <summary>
/// Eine Aufnahme, nur im Speicher: 16 kHz Mono PCM16. Pause hängt beim Weiter an dieselbe Aufnahme an.
/// Methoden laufen auf dem UI-Thread; <see cref="Level"/> darf aus jedem Thread gelesen werden.
/// </summary>
public interface IAudioRecorder
{
    /// <summary>Aktueller Pegel 0..1 (0 in der Pause).</summary>
    double Level { get; }

    /// <exception cref="AudioUnavailableException">Kein Mikrofon bzw. Audio-Bibliothek fehlt.</exception>
    void Start(string? microphone);

    void Pause();

    void Resume();

    /// <summary>Beendet die Aufnahme und gibt alles Aufgenommene zurück.</summary>
    AudioBuffer Stop();

    /// <summary>Beendet die Aufnahme und verwirft sie.</summary>
    void Cancel();
}

/// <summary>Audio nicht nutzbar (kein Gerät, native Bibliothek fehlt). Meldung ohne Audio-Inhalt.</summary>
public sealed class AudioUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Sammelt Mono-Samples mit Pause; gemeinsamer Teil der Recorder. Threadsicher.</summary>
internal sealed class SampleCollector
{
    private readonly List<short> _samples = [];
    private readonly object _gate = new();
    private bool _paused;

    public double Level { get; private set; }

    public bool Paused
    {
        get { lock (_gate) { return _paused; } }
        set { lock (_gate) { _paused = value; if (value) { Level = 0; } } }
    }

    public void Add(ReadOnlySpan<short> chunk)
    {
        lock (_gate)
        {
            if (_paused)
            {
                return;
            }
            foreach (var s in chunk)
            {
                _samples.Add(s);
            }
            Level = AudioMath.Level(chunk);
        }
    }

    public short[] Take()
    {
        lock (_gate)
        {
            var all = _samples.ToArray();
            _samples.Clear();
            _paused = false;
            Level = 0;
            return all;
        }
    }
}
