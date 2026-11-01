using WhisperLoom.App.Setup;
using WhisperLoom.App.ViewModels;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>Autostart-Ersatz für UI-Tests, die nur den Schalter prüfen.</summary>
internal sealed class FakeAutostart : IAutostart
{
    public bool IsEnabled { get; set; }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;
}

/// <summary>Assistent mit Fakes: Controller, Key-Ablage, Zugangsprüfung, Mikrofon, Hotkey-Rekorder, Autostart.</summary>
internal sealed class SetupFixture : IDisposable
{
    public SetupFixture(bool windows = true, PolicySettings? policy = null, WhisperLoomSettings? settings = null,
        Func<Task<string?>>? recordHotkey = null, IAutostart? autostart = null)
    {
        F = new ControllerFixture(settings);
        if (policy is not null)
        {
            F.Controller.Policy = policy;
        }
        State = new SetupState(F.DataDir);
        Autostart = autostart ?? new FakeAutostart();
        Settings = new SettingsViewModel(F.Controller, Secrets, new FakeAccessChecker(), ["Headset-Mikrofon", "USB-Mikrofon"], null, recordHotkey);
        Mic = new MicLevelTest(Recorder, () => Settings.Microphone.Value);
        Vm = new SetupViewModel(Settings, State, Autostart, Mic, recordHotkey, windows, "\"/opt/WhisperLoom/WhisperLoom\" toggle");
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
