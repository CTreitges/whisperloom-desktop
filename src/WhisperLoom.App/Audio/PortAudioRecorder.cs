using System.Runtime.InteropServices;
using PortAudioSharp;
using WhisperLoom.Core.Contracts;
using PaStream = PortAudioSharp.Stream;

namespace WhisperLoom.App.Audio;

/// <summary>
/// Mikrofon über PortAudio (PortAudioSharp2, natives PortAudio für win-x64 und linux-x64 im Paket). Nimmt mono in der
/// Standard-Rate des Geräts auf und rechnet beim Stopp auf 16 kHz um — viele Geräte können 16 kHz nicht direkt.
/// Samples liegen nur im Speicher. PortAudio wird erst beim ersten Gebrauch geladen, damit ein fehlendes
/// libasound (z. B. WSL) die App nicht am Start hindert.
/// </summary>
// Doku: nuget.org/packages/PortAudioSharp2 und github.com/k2-fsa/PortAudioSharp2 (abgerufen 2026-10-30);
// PortAudio-API: portaudio.com/docs/v19-doxydocs/portaudio_8h.html (abgerufen 2026-10-30).
internal sealed class PortAudioRecorder : IAudioRecorder, IDisposable
{
    private static readonly object InitGate = new();
    private static bool _initialized;

    private readonly SampleCollector _collector = new();
    private PaStream? _stream;
    private PaStream.Callback? _callback;
    private int _rate = AudioBuffer.DefaultSampleRate;

    public double Level => _collector.Level;

    /// <summary>Namen der Eingabegeräte für die Mikrofonwahl.</summary>
    /// <exception cref="AudioUnavailableException">PortAudio nicht ladbar.</exception>
    public static IReadOnlyList<string> InputDevices()
    {
        EnsureInitialized();
        var names = new List<string>();
        for (var i = 0; i < PortAudio.DeviceCount; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.maxInputChannels > 0)
            {
                names.Add(info.name);
            }
        }
        return names;
    }

    public void Start(string? microphone)
    {
        EnsureInitialized();
        var device = FindDevice(microphone);
        if (device == PortAudio.NoDevice)
        {
            throw new AudioUnavailableException("Kein Mikrofon gefunden.");
        }
        var info = PortAudio.GetDeviceInfo(device);
        _rate = (int)info.defaultSampleRate;
        var parameters = new StreamParameters
        {
            device = device,
            channelCount = 1,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = info.defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };
        _collector.Take();
        _callback = OnAudio;
        try
        {
            _stream = new PaStream(parameters, null, _rate, PortAudio.FramesPerBufferUnspecified, StreamFlags.ClipOff, _callback, null);
            _stream.Start();
        }
        catch (PortAudioException e)
        {
            DisposeStream();
            throw new AudioUnavailableException($"Mikrofon lässt sich nicht öffnen ({e.ErrorCode}).", e);
        }
    }

    public void Pause() => _collector.Paused = true;

    public void Resume() => _collector.Paused = false;

    public AudioBuffer Stop()
    {
        DisposeStream();
        var samples = _collector.Take();
        return new AudioBuffer(AudioMath.Resample(samples, _rate, AudioBuffer.DefaultSampleRate));
    }

    public void Cancel()
    {
        DisposeStream();
        _collector.Take();
    }

    public void Dispose() => DisposeStream();

    private StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (input == IntPtr.Zero || frameCount == 0)
        {
            return StreamCallbackResult.Continue;
        }
        var floats = new float[frameCount];
        Marshal.Copy(input, floats, 0, (int)frameCount);
        var pcm = new short[frameCount];
        for (var i = 0; i < pcm.Length; i++)
        {
            pcm[i] = AudioMath.ToPcm16(floats[i]);
        }
        _collector.Add(pcm);
        return StreamCallbackResult.Continue;
    }

    private void DisposeStream()
    {
        if (_stream is null)
        {
            return;
        }
        try
        {
            _stream.Stop();
        }
        catch (PortAudioException)
        {
            // Gerät weg: trotzdem schließen.
        }
        _stream.Dispose();
        _stream = null;
    }

    private static int FindDevice(string? name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels > 0 && info.name == name)
                {
                    return i;
                }
            }
        }
        return PortAudio.DefaultInputDevice;
    }

    private static void EnsureInitialized()
    {
        lock (InitGate)
        {
            if (_initialized)
            {
                return;
            }
            try
            {
                PortAudio.LoadNativeLibrary();
                PortAudio.Initialize();
                _initialized = true;
            }
            catch (Exception e) when (e is DllNotFoundException or PortAudioException or EntryPointNotFoundException or BadImageFormatException)
            {
                throw new AudioUnavailableException("Audio-Bibliothek (PortAudio) nicht verfügbar.", e);
            }
        }
    }
}
