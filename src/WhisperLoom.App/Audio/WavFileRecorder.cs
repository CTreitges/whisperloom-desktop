using System.Buffers.Binary;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Audio;

/// <summary>
/// Test-Schalter <c>--test-audio &lt;wav&gt;</c>: „nimmt“ eine WAV-Datei statt des Mikrofons auf. Die Datei wird nur
/// gelesen; <see cref="Stop"/> liefert sie ganz (unabhängig von der Aufnahmedauer), damit Smokes reproduzierbar sind.
/// Der Pegel folgt der Position in der Datei.
/// </summary>
internal sealed class WavFileRecorder(string path) : IAudioRecorder
{
    private short[]? _samples;
    private DateTimeOffset _started;
    private bool _paused;

    public double Level
    {
        get
        {
            if (_samples is null || _paused || _samples.Length == 0)
            {
                return 0;
            }
            var pos = (int)((DateTimeOffset.UtcNow - _started).TotalSeconds * AudioBuffer.DefaultSampleRate) % _samples.Length;
            var len = Math.Min(1600, _samples.Length - pos);
            return AudioMath.Level(_samples.AsSpan(pos, len));
        }
    }

    public void Start(string? microphone)
    {
        try
        {
            _samples = Read(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new AudioUnavailableException("Test-Audio nicht lesbar.", e);
        }
        _started = DateTimeOffset.UtcNow;
        _paused = false;
    }

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    public AudioBuffer Stop()
    {
        var samples = _samples ?? [];
        _samples = null;
        return new AudioBuffer(samples);
    }

    public void Cancel() => _samples = null;

    /// <summary>PCM16-WAV (RIFF) → 16 kHz Mono. Andere Formate: <see cref="InvalidDataException"/>.</summary>
    internal static short[] Read(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Keine WAV-Datei.");
        }
        int channels = 0, rate = 0, bits = 0;
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = wav.Slice(pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(pos + 4, 4));
            var body = pos + 8;
            if (size < 0 || body + size > wav.Length)
            {
                size = wav.Length - body;
            }
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                var format = BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body, 2));
                channels = BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body + 2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(body + 4, 4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body + 14, 2));
                if (format != 1 || bits != 16 || channels < 1 || rate <= 0)
                {
                    throw new InvalidDataException("Nur PCM16-WAV wird unterstützt.");
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels == 0)
                {
                    throw new InvalidDataException("WAV ohne fmt-Block.");
                }
                var frames = size / 2 / channels;
                var mono = new short[frames];
                for (var i = 0; i < frames; i++)
                {
                    var sum = 0;
                    for (var c = 0; c < channels; c++)
                    {
                        sum += BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body + (((i * channels) + c) * 2), 2));
                    }
                    mono[i] = (short)(sum / channels);
                }
                return AudioMath.Resample(mono, rate, AudioBuffer.DefaultSampleRate);
            }
            pos = body + size + (size & 1);
        }
        throw new InvalidDataException("WAV ohne data-Block.");
    }
}
