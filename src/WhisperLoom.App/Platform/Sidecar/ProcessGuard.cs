using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Der Begleitprozess endet mit der App, auch wenn sie abstürzt oder getötet wird:
/// <list type="bullet">
/// <item>Windows: Job-Objekt mit <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>; stirbt die App, schließt das System den
/// Handle und beendet den Job samt Begleitprozess.</item>
/// <item>Linux: ein Wächter (<c>/bin/sh</c>) prüft jede Sekunde, ob die App noch lebt, und beendet sonst genau die
/// gemerkte PID (nur wenn sie noch <c>whisper-server</c> heißt).</item>
/// </list>
/// </summary>
// Doku: https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information (abgerufen 2026-10-31),
// https://learn.microsoft.com/windows/win32/procthread/job-objects (abgerufen 2026-10-31)
internal sealed class ProcessGuard : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private IntPtr _job;
    private Process? _watcher;

    private ProcessGuard()
    {
    }

    /// <summary>Bindet <paramref name="child"/> an die Lebenszeit dieses Prozesses.</summary>
    public static ProcessGuard Attach(Process child)
    {
        var guard = new ProcessGuard();
        if (OperatingSystem.IsWindows())
        {
            guard._job = CreateJob();
            if (!AssignProcessToJobObject(guard._job, child.Handle))
            {
                throw new InvalidOperationException($"AssignProcessToJobObject: Fehler {Marshal.GetLastWin32Error()}");
            }
        }
        else
        {
            guard._watcher = StartWatcher(Environment.ProcessId, child.Id);
        }
        return guard;
    }

    /// <summary>Wächter-Skript: $1 = App-PID, $2 = Begleitprozess-PID.</summary>
    internal const string WatchScript =
        "while kill -0 \"$1\" 2>/dev/null && kill -0 \"$2\" 2>/dev/null; do sleep 1; done; " +
        "if ! kill -0 \"$1\" 2>/dev/null && [ \"$(cat /proc/$2/comm 2>/dev/null)\" = whisper-server ]; then kill \"$2\"; fi";

    private static Process StartWatcher(int appPid, int childPid)
    {
        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardInput = false };
        foreach (var a in new[] { "-c", WatchScript, "whisperloom-watch", appPid.ToString(), childPid.ToString() }) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("Wächter startet nicht");
    }

    /// <summary>Gibt Handle bzw. Wächter frei. Unter Windows beendet das Schließen des Jobs den Begleitprozess.</summary>
    public void Dispose()
    {
        if (_job != IntPtr.Zero)
        {
            CloseHandle(_job);
            _job = IntPtr.Zero;
        }
        _watcher?.Dispose(); // endet von selbst, sobald der Begleitprozess weg ist
        _watcher = null;
    }

    private static IntPtr CreateJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new InvalidOperationException($"CreateJobObject: Fehler {Marshal.GetLastWin32Error()}");
        var info = new JobObjectExtendedLimitInformationStruct { BasicLimitInformation = { LimitFlags = JobObjectLimitKillOnJobClose } };
        var size = Marshal.SizeOf<JobObjectExtendedLimitInformationStruct>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                var error = Marshal.GetLastWin32Error();
                CloseHandle(job);
                throw new InvalidOperationException($"SetInformationJobObject: Fehler {error}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationStruct
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks (wie Program.cs).
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
#pragma warning restore SYSLIB1054
}
