namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Schutz für Low-Level-Hook-Callbacks (W2-FIX N-8): Eine Ausnahme im Callback beendet sonst den Prozess (Rückruf aus
/// nativem Code), und das Ereignis bekäme kein <c>CallNextHookEx</c>. Bei einem Fehler geht das Ereignis unverändert weiter
/// (nie geschluckt). Gezählt wird nur die Fehlerklasse, für Status und Log.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/winmsg/lowlevelkeyboardproc („should call CallNextHookEx“) und
// learn.microsoft.com/dotnet/standard/native-interop/exceptions-interoperability (Ausnahmen über native Rahmen) (abgerufen 2026-11-01).
public static class HookGuard
{
    private static int _failures;

    /// <summary>Anzahl abgefangener Fehler seit dem Start.</summary>
    public static int Failures => _failures;

    /// <summary>Typname des letzten Fehlers; null = keiner.</summary>
    public static string? LastError { get; private set; }

    public static IntPtr Invoke(Func<IntPtr> handle, Func<IntPtr> next)
    {
        try
        {
            return handle();
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref _failures);
            LastError = e.GetType().Name;
            return next();
        }
    }
}
