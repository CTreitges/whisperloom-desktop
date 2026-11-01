using WhisperLoom.Core.Api;

namespace WhisperLoom.App.Management;

/// <summary>Server-Adresse und Firmencode aus einem Einladungslink <c>whisperloom://join?server=…&amp;code=…</c>.</summary>
public sealed record JoinLink(string Server, string Code)
{
    public const string Scheme = "whisperloom";

    /// <summary>null = kein Einladungslink bzw. ohne <c>server</c> oder <c>code</c>.</summary>
    public static JoinLink? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("join", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string? server = null, code = null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var cut = pair.IndexOf('=');
            if (cut <= 0)
            {
                continue;
            }
            var value = Uri.UnescapeDataString(pair[(cut + 1)..].Replace('+', ' '));
            switch (pair[..cut].ToLowerInvariant())
            {
                case "server": server = value; break;
                case "code": code = value; break;
            }
        }
        return string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(code) ? null : new JoinLink(server.Trim(), code.Trim());
    }
}

/// <summary>
/// Eingaben der Firmen-Anbindung prüfen (<c>docs/server-api.md</c> §1, §1.3). Fehler als Lokalisierungs-Schlüssel, damit
/// Assistent und CLI dieselbe Regel nutzen.
/// </summary>
public static class EnrollInput
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Firmencode normalisieren: ohne Bindestriche, Leerzeichen und Groß-/Kleinschreibung, <c>O→0</c>, <c>I/L→1</c>;
    /// Ergebnis <c>ABCD-EFGH-JKMN-PQRS</c>. null = kein gültiger Code (16 Zeichen Crockford-Base32).
    /// </summary>
    public static string? NormalizeCode(string? input)
    {
        if (input is null)
        {
            return null;
        }
        var chars = new List<char>();
        foreach (var c in input.ToUpperInvariant())
        {
            if (c is '-' or ' ' or '\t')
            {
                continue;
            }
            var mapped = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
            if (!Crockford.Contains(mapped))
            {
                return null;
            }
            chars.Add(mapped);
        }
        if (chars.Count != 16)
        {
            return null;
        }
        var flat = new string([.. chars]);
        return $"{flat[..4]}-{flat[4..8]}-{flat[8..12]}-{flat[12..]}";
    }

    /// <summary>
    /// Server-Adresse normalisieren: getrimmt, ohne abschließenden <c>/</c> und ohne angehängtes <c>/api/v1</c>.
    /// Gültig: <c>https://…</c>, <c>http://</c> nur zu privaten Adressen (wie <c>stt.baseUrl</c>), ohne Query und Anmeldedaten.
    /// </summary>
    /// <returns>(normalisierte Adresse, null) oder (null, Fehler-Schlüssel).</returns>
    public static (string? Url, string? ErrorKey) NormalizeServer(string? input)
    {
        var text = (input ?? "").Trim().TrimEnd('/');
        if (text.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^"/api/v1".Length].TrimEnd('/');
        }
        if (text.Length == 0)
        {
            return (null, "Company_ServerMissing");
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.Host.Length == 0
            || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
        {
            return (null, "Company_ServerInvalid");
        }
        if (uri.Scheme == "http" && !ServerUrlCheck.IsPrivateHost(uri.Host))
        {
            return (null, "Company_ServerHttps");
        }
        return (text, null);
    }

    /// <summary>Gerätename fürs Portal: Rechnername, höchstens 64 Zeichen.</summary>
    public static string DefaultDeviceName() =>
        Environment.MachineName is { Length: > 0 } name ? (name.Length <= 64 ? name : name[..64]) : "WhisperLoom";

    /// <summary><c>platform</c> der Server-API.</summary>
    public static string Platform() => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other";
}
