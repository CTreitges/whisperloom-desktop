using System.Globalization;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Prüft eine eingetippte Base-URL (Port von <c>api/ServerUrlCheck.kt</c>). Unverschlüsseltes http:// ist nur zu privaten
/// Adressen sinnvoll (LAN, VPN, Tailscale) — sonst ein Hinweis.
/// </summary>
public static class ServerUrlCheck
{
    public enum Severity
    {
        /// <summary>Zugang gilt als unvollständig.</summary>
        Error,

        /// <summary>Nur Hinweis.</summary>
        Warning,
    }

    public sealed record Problem(Severity Severity, string Message);

    public const string MsgInvalid = "Ungültige URL";
    public const string MsgScheme = "URL muss mit http:// oder https:// beginnen";
    public const string MsgNoHost = "Kein Host in der URL";
    public const string MsgHttpsRequired = "Dieser Anbieter braucht https://";
    public const string MsgPublicHttp = "Unverschlüsselt über das Internet — https:// oder VPN (Tailscale) verwenden";
    public const string MsgV1 = "Base-URL endet normalerweise auf /v1";

    private static readonly string[] PrivateSuffixes = [".local", ".lan", ".home.arpa", ".internal"];

    /// <summary>localhost, RFC 1918, Link-Local, Tailscale-CGNAT (100.64/10), IPv6-ULA/Loopback, mDNS-Endungen.</summary>
    public static bool IsPrivateHost(string host)
    {
        var h = host.Trim().ToLowerInvariant().TrimStart('[').TrimEnd(']');
        if (h.Length == 0) return false;
        if (h == "localhost" || PrivateSuffixes.Any(s => h.EndsWith(s, StringComparison.Ordinal))) return true;
        if (h.Contains(':', StringComparison.Ordinal))
        {
            return h == "::1" || h.StartsWith("fc", StringComparison.Ordinal) || h.StartsWith("fd", StringComparison.Ordinal) ||
                h.StartsWith("fe80:", StringComparison.Ordinal);
        }
        var parts = h.Split('.');
        if (parts.Length != 4) return false;
        var p = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out p[i]) || p[i] > 255) return false;
        }
        return p[0] == 10 || p[0] == 127 ||
            (p[0] == 172 && p[1] is >= 16 and <= 31) ||
            (p[0] == 192 && p[1] == 168) ||
            (p[0] == 169 && p[1] == 254) ||
            (p[0] == 100 && p[1] is >= 64 and <= 127);
    }

    /// <summary>null = in Ordnung. Der /v1-Hinweis gilt nur für den eigenen Server.</summary>
    public static Problem? Check(string baseUrl, ProviderInfo provider)
    {
        var trimmed = baseUrl.Trim();
        if (trimmed.Length == 0) return new Problem(Severity.Error, MsgInvalid);
        var sep = trimmed.IndexOf("://", StringComparison.Ordinal);
        var scheme = sep < 0 ? "" : trimmed[..sep].ToLowerInvariant();
        if (scheme is not ("http" or "https")) return new Problem(Severity.Error, MsgScheme);
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var u)) return new Problem(Severity.Error, MsgInvalid);
        if (string.IsNullOrWhiteSpace(u.Host)) return new Problem(Severity.Error, MsgNoHost);
        if (scheme == "http")
        {
            if (!provider.AllowsHttp) return new Problem(Severity.Error, MsgHttpsRequired);
            if (!IsPrivateHost(u.Host)) return new Problem(Severity.Warning, MsgPublicHttp);
        }
        if (provider.IsCustom && !u.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.Ordinal))
        {
            return new Problem(Severity.Warning, MsgV1);
        }
        return null;
    }
}
