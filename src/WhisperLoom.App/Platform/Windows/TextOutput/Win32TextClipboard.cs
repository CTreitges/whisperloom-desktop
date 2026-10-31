using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhisperLoom.App.Platform.Windows.TextOutput;

/// <summary>
/// Win32-Zwischenablage mit Sicherung aller Speicher-Formate (HGLOBAL) und privatem Setzen: ein Diktat landet weder im
/// Zwischenablage-Verlauf (Win+V) noch in der Cloud-Synchronisierung noch bei Zwischenablage-Überwachern.
/// GDI-Formate (Bitmap, Metafile, Palette) lassen sich nicht kopieren; Bilder bleiben über CF_DIB/CF_DIBV5 erhalten.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats und
// learn.microsoft.com/windows/win32/dataxchg/standard-clipboard-formats (abgerufen 2026-10-31).
[SupportedOSPlatform("windows")]
internal sealed class Win32TextClipboard : IClipboardAdapter
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    private static readonly uint ExcludeFormat = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint HistoryFormat = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    private static readonly uint CloudFormat = RegisterClipboardFormatW("CanUploadToCloudClipboard");

    public uint SequenceNumber => GetClipboardSequenceNumber();

    public ClipboardSnapshot Save()
    {
        var items = new List<(uint, byte[])>();
        Open();
        try
        {
            for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                if (!Copyable(format))
                {
                    continue;
                }
                var handle = GetClipboardData(format);
                var size = handle == IntPtr.Zero ? 0 : (long)GlobalSize(handle);
                var source = size > 0 ? GlobalLock(handle) : IntPtr.Zero;
                if (source == IntPtr.Zero)
                {
                    continue;
                }
                try
                {
                    var data = new byte[size];
                    Marshal.Copy(source, data, 0, data.Length);
                    items.Add((format, data));
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
        }
        finally
        {
            CloseClipboard();
        }
        return new ClipboardSnapshot(items);
    }

    public void SetText(string text, bool @private)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        System.Text.Encoding.Unicode.GetBytes(text, bytes);
        Write([(CfUnicodeText, bytes)], @private);
    }

    public void Restore(ClipboardSnapshot snapshot) => Write(snapshot.Items, @private: true);

    private static void Write(IReadOnlyList<(uint Format, byte[] Data)> items, bool @private)
    {
        Open();
        try
        {
            EmptyClipboard();
            if (items.Count == 0)
            {
                return;
            }
            var all = new List<(uint Format, byte[] Data)>();
            if (@private)
            {
                // Laut Doku: ExcludeClipboardContentFromMonitorProcessing mit beliebigem Inhalt, die beiden anderen als DWORD 0.
                // Zuerst, damit kein Beobachter den Text ohne diese Kennzeichen sieht.
                all.Add((ExcludeFormat, new byte[4]));
                all.Add((HistoryFormat, new byte[4]));
                all.Add((CloudFormat, new byte[4]));
            }
            all.AddRange(items.Where(i => !IsPrivacyFormat(i.Format)));
            foreach (var (format, data) in all)
            {
                var handle = GlobalAlloc(GmemMoveable, (UIntPtr)Math.Max(data.Length, 1));
                var target = GlobalLock(handle);
                Marshal.Copy(data, 0, target, data.Length);
                GlobalUnlock(handle);
                if (SetClipboardData(format, handle) == IntPtr.Zero)
                {
                    throw ClipboardBusyException.FromLastError("Schreiben fehlgeschlagen");
                }
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool IsPrivacyFormat(uint format) => format == ExcludeFormat || format == HistoryFormat || format == CloudFormat;

    // CF_BITMAP, CF_METAFILEPICT, CF_PALETTE, CF_ENHMETAFILE, Owner-Display und private GDI-Objekte sind keine HGLOBALs.
    private static bool Copyable(uint format) =>
        format is not (2 or 3 or 9 or 14 or 0x80 or 0x82 or 0x83 or 0x8E) and not (>= 0x300 and <= 0x3FF);

    private static void Open()
    {
        // Ein anderes Programm kann die Zwischenablage kurz halten: einige Versuche.
        for (var attempt = 0; !OpenClipboard(IntPtr.Zero); attempt++)
        {
            if (attempt == 10)
            {
                throw new ClipboardBusyException("Zwischenablage ist belegt.");
            }
            Thread.Sleep(50);
        }
    }

#pragma warning disable SYSLIB1054 // DllImport reicht hier; LibraryImport bräuchte AllowUnsafeBlocks.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr handle);
#pragma warning restore SYSLIB1054
}
