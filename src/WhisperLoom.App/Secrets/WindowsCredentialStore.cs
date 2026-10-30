using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Secrets;

/// <summary>
/// Keys im Windows Credential Manager (generische Anmeldeinformation, Ziel <c>&lt;präfix&gt;&lt;name&gt;</c>,
/// z. B. <c>WhisperLoom:stt</c>). Ohne Admin-Rechte, nur für den angemeldeten Benutzer.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/api/wincred/nf-wincred-credwritew und -credreadw (abgerufen 2026-10-30).
[SupportedOSPlatform("windows")]
internal sealed class WindowsCredentialStore(string prefix) : ISecretStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string Kind => "Credential Manager";

    public Task<string?> GetAsync(string name, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        if (!CredRead(prefix + name, CredTypeGeneric, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? Task.FromResult<string?>(null)
                : throw new IOException($"Credential Manager: Lesen fehlgeschlagen (Fehler {error}).");
        }
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            var value = cred.CredentialBlobSize == 0
                ? ""
                : Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2);
            return Task.FromResult<string?>(value);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public Task SetAsync(string name, string value, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        SecretStoreNames.CheckValue(value);
        var blob = Encoding.Unicode.GetBytes(value);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = prefix + name,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = "WhisperLoom",
            };
            if (!CredWrite(ref cred, 0))
            {
                throw new IOException($"Credential Manager: Schreiben fehlgeschlagen (Fehler {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        if (!CredDelete(prefix + name, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new IOException($"Credential Manager: Löschen fehlgeschlagen (Fehler {error}).");
            }
        }
        return Task.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks.
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
#pragma warning restore SYSLIB1054
}
