using System.IO.Compression;
using WhisperLoom.App.Platform.Sidecar;

namespace WhisperLoom.App.Tests.Sidecar;

/// <summary>
/// W2-FIX N-9 (Sonde <c>security-review-tmp/hook-supply/probe-output-2.txt</c>): Ein Zip mit „made by Unix“ trägt unter
/// Windows <c>..\x</c>, absolute und laufwerksrelative Namen ungekürzt in <c>ZipArchiveEntry.Name</c>. Keiner der beiden
/// Entpacker schreibt damit außerhalb des Zielordners.
/// </summary>
public sealed class ZipSlipTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wl-zipslip-" + Guid.NewGuid().ToString("N"));

    public ZipSlipTest() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Zip wie die Sonde: Einträge schreiben, danach im Central Directory „version made by“ auf Unix (3) setzen.</summary>
    private string UnixZip(params string[] names)
    {
        var path = Path.Combine(_root, "unixmadeby.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var name in names)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write("x");
            }
        }
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i + 6 < bytes.Length; i++)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4b && bytes[i + 2] == 0x01 && bytes[i + 3] == 0x02)
            {
                bytes[i + 5] = 3; // Kopf „version made by“, oberes Byte = Betriebssystem (3 = Unix)
            }
        }
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string[] Evil()
    {
        var abs = Path.Combine(_root, "abs", "evilU3.txt");
        return [@"..\evilU1.txt", @"..\..\evilU2.txt", abs, "C:evilU4.txt", "whisper-server.exe", "llama-server.exe"];
    }

    [Theory]
    [InlineData("sidecar")]
    [InlineData("llama")]
    public void Unix_made_zip_never_writes_outside_the_target(string extractor)
    {
        var archive = UnixZip(Evil());
        using (var check = ZipFile.OpenRead(archive))
        {
            Assert.Contains(check.Entries, e => e.Name == @"..\evilU1.txt"); // die Lage aus der Sonde ist hergestellt
        }
        var target = Path.Combine(_root, "out", "deep", "target");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var cwd = Environment.CurrentDirectory;

        if (extractor == "sidecar")
        {
            SidecarRelease.Extract(archive, target);
        }
        else
        {
            LlamaRelease.Extract(archive, target);
        }

        var outside = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && f != archive).ToList();
        Assert.Empty(outside);
        Assert.False(File.Exists(Path.Combine(cwd, "evilU4.txt")));
        Assert.True(File.Exists(Path.Combine(target, "whisper-server.exe")));
        Assert.True(File.Exists(Path.Combine(target, "llama-server.exe")));
        Assert.Empty(Directory.GetDirectories(target));
    }

    [Theory]
    [InlineData("a.txt", true)]
    [InlineData(@"..\a.txt", true)] // nur der Dateiname zählt, im Zielordner
    [InlineData("../a.txt", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData("sub/", false)]
    [InlineData("C:a.txt", false)]
    [InlineData("a.txt:stream", false)]
    [InlineData("CON", false)] // Gerätenamen (Prüfer-Befund 9)
    [InlineData("nul.txt", false)]
    [InlineData("aux .dll", false)]
    [InlineData("COM1.dll", false)]
    [InlineData("lpt9", false)]
    [InlineData("console.dll", true)]
    [InlineData("com10.dll", true)]
    public void SafePath_only_plain_names_inside_the_folder(string name, bool ok)
    {
        var path = ArchiveNames.SafePath(_root, name);
        Assert.Equal(ok, path is not null);
        if (path is not null)
        {
            Assert.Equal(Path.GetFullPath(_root), Path.GetDirectoryName(path));
        }
    }
}
