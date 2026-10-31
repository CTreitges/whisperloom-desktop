using System.Net.NetworkInformation;
using System.Net.Sockets;
using WhisperLoom.Core.Api;

namespace WhisperLoom.Core.Pipeline;

/// <summary>
/// Netz-Vorprüfung vor jeder Online-Anfrage der Textverbesserung (Port von <c>api/NetworkCheck.kt</c>): ohne Netz
/// entscheidet die Regel sofort (lokal oder ohne KI), statt dass das Diktat im Connect-Zeitlimit hängt. Schickt nichts los.
/// </summary>
public static class NetworkAvailability
{
    /// <summary>
    /// Internet zählt erst „validiert“ — oder <paramref name="proven"/>. Für einen Server im eigenen Netz reicht ein aktives
    /// Netz (Heim-WLAN ohne Internet, VPN).
    /// </summary>
    public static bool Available(bool activeNetwork, bool validated, bool ownNetwork, bool proven = false) =>
        activeNetwork && (validated || ownNetwork || proven);

    /// <summary>Server im eigenen Netz: private Adressen (<see cref="ServerUrlCheck.IsPrivateHost"/>) und Tailscale-Namen (*.ts.net).</summary>
    public static bool IsOwnNetwork(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) || uri.Host.Length == 0) return false;
        var host = uri.Host.ToLowerInvariant().Trim('[', ']');
        return ServerUrlCheck.IsPrivateHost(host) || host.EndsWith(".ts.net", StringComparison.Ordinal);
    }

    /// <summary>
    /// Desktop-Prüfung für <paramref name="baseUrl"/>. Loopback braucht kein Netz. „Aktiv“ = eine Schnittstelle außer
    /// Loopback/Tunnel ist oben; „validiert“ (Android: NET_CAPABILITY_VALIDATED) = eine davon hat ein Standard-Gateway —
    /// virtuelle Adapter (WSL, Hyper-V) ohne Gateway zählen so nicht als Internet.
    /// </summary>
    // Doku: https://learn.microsoft.com/dotnet/api/system.net.networkinformation.networkinterface.getallnetworkinterfaces (abgerufen 2026-10-31)
    public static bool For(string baseUrl, bool proven)
    {
        if (Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) && uri.IsLoopback) return true;
        bool active = false, validated = false;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }
                active = true;
                validated |= nic.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
                    !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any));
            }
        }
        catch (NetworkInformationException)
        {
            // Unbekannt: lieber versuchen als ein Diktat grundlos ohne KI ausgeben.
            return true;
        }
        return Available(active, validated, IsOwnNetwork(baseUrl), proven);
    }
}
