"""Herkunft einer Anfrage: direkte Gegenstelle, vertraute Proxys, Client-IP, lokaler Zugriff.

Der Dienst läuft ohne uvicorn --proxy-headers: Nur wenn die direkte Gegenstelle in WL_TRUSTED_PROXIES liegt, gelten
X-Forwarded-For und der Anmelde-Header von trusted_header. Sonst werden beide ignoriert.
"""

import ipaddress

from fastapi import Request

_LOKALE_HOSTS = {"127.0.0.1", "::1", "localhost"}
_WEITERLEITUNG = ("x-forwarded-for", "forwarded", "x-real-ip", "x-forwarded-host")


def _ip(text: str):
    try:
        return ipaddress.ip_address(text.strip())
    except ValueError:
        return None


def gegenstelle(request: Request) -> str:
    return request.client.host if request.client else ""


def von_vertrautem_proxy(request: Request) -> bool:
    ip = _ip(gegenstelle(request))
    return ip is not None and any(ip in netz for netz in request.app.state.einst.vertraute_proxies)


def client_ip(request: Request) -> str:
    """Von einem vertrauten Proxy: rechte nicht vertraute Adresse aus X-Forwarded-For; sonst die Gegenstelle."""
    peer = gegenstelle(request) or "unbekannt"
    if not von_vertrautem_proxy(request):
        return peer
    netze = request.app.state.einst.vertraute_proxies
    for teil in reversed(request.headers.get("x-forwarded-for", "").split(",")):
        ip = _ip(teil)
        if ip is None:
            break
        if not any(ip in n for n in netze):
            return str(ip)
    return peer


def limit_ip(request: Request) -> str:
    """Schlüssel für Rate-Limits: IPv4 je Adresse, IPv6 je /64 (ein Anschluss hat meist ein ganzes /64)."""
    ip = _ip(client_ip(request))
    if ip is not None and ip.version == 6 and not ip.ipv4_mapped:
        return str(ipaddress.ip_network(f"{ip}/64", strict=False))
    return client_ip(request)


def ist_lokal(request: Request) -> bool:
    """local_only: Gegenstelle Loopback, kein Weiterleitungs-Header, Host-Header lokal.

    Ein Reverse-Proxy auf demselben Rechner (Gegenstelle 127.0.0.1) fällt damit heraus, sobald er einen der Header
    setzt oder den Host weitergibt; README warnt zusätzlich vor local_only hinter einem Proxy.
    """
    ip = _ip(gegenstelle(request))
    if ip is None or not ip.is_loopback or von_vertrautem_proxy(request):
        return False
    if any(h in request.headers for h in _WEITERLEITUNG):
        return False
    return (request.url.hostname or "") in _LOKALE_HOSTS
