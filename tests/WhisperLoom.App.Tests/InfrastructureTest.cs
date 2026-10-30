using System.Buffers.Binary;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Cli;
using WhisperLoom.App.Instance;
using WhisperLoom.App.Services;

namespace WhisperLoom.App.Tests;

public class CommandLineTest
{
    [Fact]
    public void Parses_verbs_and_test_switches()
    {
        var r = CommandLine.Parse(["stage", "next", "--data-dir", "d", "--output-file", "o.txt", "--test-audio", "a.wav"]);
        Assert.Equal("stage", r.Verb);
        Assert.Equal(["next"], r.Args);
        Assert.Equal(Path.GetFullPath("d"), r.Options.DataDir);
        Assert.Equal(Path.GetFullPath("o.txt"), r.Options.OutputFile);
        Assert.Equal(Path.GetFullPath("a.wav"), r.Options.TestAudio);
        Assert.True(r.NeedsRunningInstance);
    }

    [Fact]
    public void Status_json_and_no_verb()
    {
        Assert.True(CommandLine.Parse(["status", "--json"]).Json);
        var none = CommandLine.Parse([]);
        Assert.Null(none.Verb);
        Assert.Equal(AppOptions.DefaultSecretPrefix, none.Options.SecretPrefix);
        Assert.False(CommandLine.Parse(["toggle"]).NeedsRunningInstance);
        Assert.True(CommandLine.Parse(["key", "set", "stt"]).IsLocal);
    }

    [Theory]
    [InlineData("jump")]
    [InlineData("stage")]
    [InlineData("key", "get", "stt")]
    [InlineData("--data-dir")]
    [InlineData("--secret-prefix", "Other:")]
    [InlineData("--reveal")]
    public void Rejects_bad_input(params string[] args)
    {
        Assert.Throws<UsageException>(() => CommandLine.Parse(args));
    }
}

public class InstanceTest
{
    [Fact]
    public async Task Second_lock_fails_and_ipc_round_trip_works()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-ipc-" + Guid.NewGuid().ToString("N"));
        var endpoint = new InstanceEndpoint(dir);
        using (var first = endpoint.TryAcquireLock())
        {
            Assert.NotNull(first);
            Assert.Null(endpoint.TryAcquireLock());

            await using var server = new IpcServer(endpoint, (req, _) =>
                Task.FromResult(new IpcResponse(true, $"{req.Verb}:{string.Join(',', req.Args)}")));
            server.Start();
            await using var stream = await endpoint.TryConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(stream);
            await IpcProtocol.WriteAsync(stream, new IpcRequest("stage", ["next"]), CancellationToken.None);
            var response = await IpcProtocol.ReadAsync<IpcResponse>(stream, CancellationToken.None);
            Assert.Equal("stage:next", response?.Message);
        }
        using (var again = endpoint.TryAcquireLock())
        {
            Assert.NotNull(again);
        }
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task No_instance_means_no_connection()
    {
        var endpoint = new InstanceEndpoint(Path.Combine(Path.GetTempPath(), "wl-none-" + Guid.NewGuid().ToString("N")));
        Assert.Null(await endpoint.TryConnectAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None));
    }

    [Fact]
    public void Endpoint_name_depends_on_data_dir()
    {
        Assert.NotEqual(new InstanceEndpoint("/a").PipeName, new InstanceEndpoint("/b").PipeName);
    }
}

public class AudioTest
{
    [Fact]
    public void Resample_48k_to_16k_keeps_duration()
    {
        var input = Enumerable.Range(0, 48_000).Select(i => (short)(i % 100)).ToArray();
        Assert.Equal(16_000, AudioMath.Resample(input, 48_000, 16_000).Length);
        Assert.Empty(AudioMath.Resample([], 44_100, 16_000));
    }

    [Fact]
    public void Level_is_zero_for_silence_and_high_for_loud()
    {
        Assert.Equal(0, AudioMath.Level(new short[100]));
        Assert.True(AudioMath.Level(Enumerable.Repeat((short)20_000, 100).ToArray()) > 0.8);
    }

    [Fact]
    public void Reads_stereo_48k_wav_as_16k_mono()
    {
        var wav = Wav(48_000, channels: 2, frames: 4_800, value: 1000);
        var samples = WavFileRecorder.Read(wav);
        Assert.Equal(1_600, samples.Length);
        Assert.All(samples.Skip(10), s => Assert.Equal(1000, s));
    }

    [Fact]
    public void Rejects_non_wav()
    {
        Assert.Throws<InvalidDataException>(() => WavFileRecorder.Read("not a wav file"u8));
    }

    internal static byte[] Wav(int rate, int channels, int frames, short value)
    {
        var data = frames * channels * 2;
        var b = new byte[44 + data];
        "RIFF"u8.CopyTo(b);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 36 + data);
        "WAVEfmt "u8.CopyTo(b.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(28), rate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(32), (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(34), 16);
        "data"u8.CopyTo(b.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(40), data);
        for (var i = 0; i < frames * channels; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(44 + (i * 2)), value);
        }
        return b;
    }
}

public class AppLogTest
{
    [Theory]
    [InlineData("polish", "polish")]
    [InlineData(1500L, "1500")]
    [InlineData(null, "-")]
    [InlineData("zwei Wörter", "(verworfen)")]
    [InlineData("zeile\nzwei", "(verworfen)")]
    public void Values_are_short_tokens_only(object? value, string expected)
    {
        Assert.Equal(expected, AppLog.Format(value));
    }

    [Fact]
    public void Long_values_are_dropped()
    {
        Assert.Equal("(verworfen)", AppLog.Format(new string('x', 65)));
    }
}
