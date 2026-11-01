using WhisperLoom.App.Platform.Windows.Input;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>W2-FIX N-8: Ausnahme im Hook-Callback → Ereignis geht per CallNextHookEx weiter, kein Absturz, nie geschluckt.</summary>
public sealed class HookGuardTest
{
    [Fact]
    public void Exception_in_the_callback_passes_the_event_on()
    {
        var before = HookGuard.Failures;
        var nextCalls = 0;

        var result = HookGuard.Invoke(() => throw new InvalidOperationException("kaputt"), () =>
        {
            nextCalls++;
            return 7;
        });

        Assert.Equal((IntPtr)7, result);
        Assert.Equal(1, nextCalls);
        Assert.True(HookGuard.Failures > before);
        Assert.Equal(nameof(InvalidOperationException), HookGuard.LastError);
    }

    [Fact]
    public void Without_exception_the_callback_decides()
    {
        var nextCalls = 0;

        var result = HookGuard.Invoke(() => 1, () =>
        {
            nextCalls++;
            return 0;
        });

        Assert.Equal((IntPtr)1, result); // geschluckt wie entschieden
        Assert.Equal(0, nextCalls);
    }
}
