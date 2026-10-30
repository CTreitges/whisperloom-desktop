using System.Text.Json;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Secrets;

/// <summary>
/// Rückfall unter Linux ohne Secret Service: <c>&lt;Datenordner&gt;/secrets.json</c> (Standard
/// <c>~/.config/whisperloom/secrets.json</c>) mit Rechten 600. Die UI zeigt dazu einen Hinweis
/// (<see cref="Kind"/> = "Datei"). Schlüssel in der Datei: <c>&lt;präfix&gt;&lt;name&gt;</c>.
/// </summary>
internal sealed class FileSecretStore(string filePath, string prefix) : ISecretStore
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public string Kind => "Datei";

    public string FilePath => filePath;

    public async Task<string?> GetAsync(string name, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return Read().GetValueOrDefault(prefix + name);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SetAsync(string name, string value, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        SecretStoreNames.CheckValue(value);
        await Update(d => d[prefix + name] = value, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        SecretStoreNames.Check(name);
        await Update(d => d.Remove(prefix + name), ct).ConfigureAwait(false);
    }

    private async Task Update(Action<Dictionary<string, string>> change, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var data = Read();
            change(data);
            Write(data);
        }
        finally
        {
            Gate.Release();
        }
    }

    private Dictionary<string, string> Read()
    {
        if (!File.Exists(filePath))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (JsonException)
        {
            throw new IOException($"Key-Datei {filePath} ist beschädigt.");
        }
    }

    private void Write(Dictionary<string, string> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmp = filePath + ".tmp";
        // Doku: learn.microsoft.com/dotnet/api/system.io.filestreamoptions.unixcreatemode (abgerufen 2026-10-30).
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }
        using (var stream = new FileStream(tmp, options))
        {
            JsonSerializer.Serialize(stream, data);
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tmp, OwnerOnly);
        }
        File.Move(tmp, filePath, overwrite: true);
    }
}
