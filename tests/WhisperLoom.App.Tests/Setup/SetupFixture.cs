using WhisperLoom.App.Setup;
using WhisperLoom.App.ViewModels;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>
/// Autostart-Ersatz: <see cref="Target"/> ist die Datei, die der Eintrag startet (null = kein Eintrag). Nie ein echter
/// Run-Wert. <see cref="FailWrites"/> lässt <see cref="Enable"/> und <see cref="Disable"/> scheitern.
/// </summary>
internal sealed class FakeAutostart(string self = FakeAutostart.Self) : IAutostart
{
    public const string Self = @"C:\Users\Test\AppData\Local\WhisperLoom\current\WhisperLoom.exe";

    public string? Target { get; set; }

    public bool FailWrites { get; set; }

    /// <summary>Anzahl erfolgreicher <see cref="Enable"/>-Aufrufe.</summary>
    public int Writes { get; private set; }

    public bool IsEnabled
    {
        get => Target is not null;
        set => Target = value ? self : null;
    }

    public bool PointsElsewhere => Target is not null && Target != self;

    public void Enable()
    {
        if (FailWrites)
        {
            throw new IOException("Fake: nicht schreibbar");
        }
        Writes++;
        Target = self;
    }

    public void Disable()
    {
        if (FailWrites)
        {
            throw new IOException("Fake: nicht entfernbar");
        }
        Target = null;
    }
}

/// <summary>
/// Assistent mit Fakes: Controller, Key-Ablage, Zugangsprüfung, Mikrofon, Hotkey-Rekorder, Autostart
/// (<c>autostartAvailable: false</c> = OS ohne Autostart, der Assistent bekommt keinen).
/// </summary>
internal sealed class SetupFixture : IDisposable
{
    public SetupFixture(bool windows = true, PolicySettings? policy = null, WhisperLoomSettings? settings = null,
        Func<Task<string?>>? recordHotkey = null, IAutostart? autostart = null, bool autostartAvailable = true,
        Services.IAccessChecker? checker = null)
    {
        F = new ControllerFixture(settings);
        if (policy is not null)
        {
            F.Controller.Policy = policy;
        }
        State = new SetupState(F.DataDir);
        Autostart = autostart ?? new FakeAutostart();
        Settings = new SettingsViewModel(F.Controller, Secrets, checker ?? new FakeAccessChecker(), ["Headset-Mikrofon", "USB-Mikrofon"], null, recordHotkey);
        Mic = new MicLevelTest(Recorder, () => Settings.Microphone.Value);
        Vm = new SetupViewModel(Settings, State, autostartAvailable ? Autostart : null, Mic, recordHotkey, windows, "\"/opt/WhisperLoom/WhisperLoom\" toggle");
        Vm.CloseRequested += () => Closed++;
    }

    public ControllerFixture F { get; }

    public MemorySecretStore Secrets { get; } = new();

    public FakeRecorder Recorder { get; } = new(level: 0.42);

    public SetupState State { get; }

    public IAutostart Autostart { get; }

    public SettingsViewModel Settings { get; }

    public MicLevelTest Mic { get; }

    public SetupViewModel Vm { get; }

    public int Closed { get; private set; }

    public void Dispose() => F.Dispose();
}
