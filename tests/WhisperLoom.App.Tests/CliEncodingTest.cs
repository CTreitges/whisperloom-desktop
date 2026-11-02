using System.Diagnostics;
using System.Text;

namespace WhisperLoom.App.Tests;

/// <summary>
/// Fund WLD-FIX-9 (W3-FUND): Umgeleitete CLI-Ausgabe (Skripte, Smokes) kommt als UTF-8 an, nicht in der Konsolen-Codepage
/// (unter Windows sonst cp1252 → Umlaute als Ersatzzeichen).
/// </summary>
public sealed class CliEncodingTest
{
    [Fact]
    public async Task Redirected_stderr_is_utf8()
    {
        var (code, _, stderr) = await RunAsync("--gibt-es-nicht");

        Assert.Equal(2, code);
        Assert.Contains("<präfix>", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain((char)0xFFFD, stderr); // Ersatzzeichen
    }

    [Fact]
    public async Task Redirected_stdout_is_utf8()
    {
        var (code, stdout, _) = await RunAsync("--help");

        Assert.Equal(0, code);
        Assert.Contains("<präfix>", stdout, StringComparison.Ordinal);
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "WhisperLoom.exe" : "WhisperLoom");
        var psi = File.Exists(exe) ? new ProcessStartInfo(exe) : new ProcessStartInfo("dotnet") { ArgumentList = { Path.Combine(AppContext.BaseDirectory, "WhisperLoom.dll") } };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = new UTF8Encoding(false);
        psi.StandardErrorEncoding = new UTF8Encoding(false);
        psi.UseShellExecute = false;
        using var process = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        return (process.ExitCode, await stdout, await stderr);
    }
}
