using System.Diagnostics;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Secrets;

/// <summary>Ergebnis eines Hilfsprogramms. stdout enthält bei <c>lookup</c> den Key — nie loggen.</summary>
internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Startet ein Hilfsprogramm (secret-tool, wl-copy, xclip); austauschbar für Tests.</summary>
internal interface IProcessRunner
{
    /// <exception cref="System.ComponentModel.Win32Exception">Programm nicht gefunden.</exception>
    Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct);
}

internal sealed class ProcessRunner : IProcessRunner
{
    public static readonly ProcessRunner Instance = new();

    public async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }
        using var process = Process.Start(info) ?? throw new IOException($"{file} startet nicht.");
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
        }
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}

/// <summary>
/// Keys im Linux Secret Service über <c>secret-tool</c> (libsecret-tools). Attribute: <c>service</c> = Präfix ohne
/// Doppelpunkt (Standard „WhisperLoom“), <c>account</c> = Name. Der Wert geht nur über stdin, nie als Argument.
/// </summary>
// Doku: manpages.ubuntu.com/manpages/noble/man1/secret-tool.1.html (abgerufen 2026-10-30).
internal sealed class SecretToolStore(string prefix, IProcessRunner runner) : ISecretStore
{
    private readonly string _service = prefix.TrimEnd(':');

    public string Kind => "Secret Service";

    /// <summary>
    /// Läuft ein Secret Service? <c>search</c> endet mit 0 (Treffer) oder 1 ohne Meldung (keine Treffer);
    /// ohne Dienst meldet secret-tool einen D-Bus-Fehler auf stderr.
    /// </summary>
    public static async Task<bool> IsAvailableAsync(IProcessRunner runner, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var r = await runner.RunAsync("secret-tool", ["search", "service", "WhisperLoom-probe"], null, timeout.Token).ConfigureAwait(false);
            return r.ExitCode == 0 || (r.ExitCode == 1 && string.IsNullOrWhiteSpace(r.StdErr));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        var r = await runner.RunAsync("secret-tool", ["lookup", "service", _service, "account", name], null, ct).ConfigureAwait(false);
        if (r.ExitCode == 0 && r.StdOut.Length > 0)
        {
            return r.StdOut.TrimEnd('\n');
        }
        return r.ExitCode is 0 or 1 && string.IsNullOrWhiteSpace(r.StdErr)
            ? null
            : throw new IOException($"Secret Service: Lesen fehlgeschlagen (Exit {r.ExitCode}).");
    }

    public async Task SetAsync(string name, string value, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        SecretStoreNames.CheckValue(value);
        var r = await runner.RunAsync(
            "secret-tool",
            ["store", $"--label={_service}:{name}", "service", _service, "account", name],
            value,
            ct).ConfigureAwait(false);
        if (r.ExitCode != 0)
        {
            throw new IOException($"Secret Service: Speichern fehlgeschlagen (Exit {r.ExitCode}).");
        }
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        var r = await runner.RunAsync("secret-tool", ["clear", "service", _service, "account", name], null, ct).ConfigureAwait(false);
        if (r.ExitCode is not (0 or 1) || !string.IsNullOrWhiteSpace(r.StdErr))
        {
            throw new IOException($"Secret Service: Löschen fehlgeschlagen (Exit {r.ExitCode}).");
        }
    }
}

/// <summary>Wählt die Ablage je OS: Windows Credential Manager; Linux Secret Service, sonst 600-Datei.</summary>
internal static class SecretStoreFactory
{
    public static async Task<ISecretStore> CreateAsync(string dataDir, string prefix, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore(prefix);
        }
        if (await SecretToolStore.IsAvailableAsync(ProcessRunner.Instance, ct).ConfigureAwait(false))
        {
            return new SecretToolStore(prefix, ProcessRunner.Instance);
        }
        return new FileSecretStore(Path.Combine(dataDir, "secrets.json"), prefix);
    }
}
