"""SSRF-Schutz für Anbieter-URLs, die ein Firmen-Admin einträgt (Proxy, W2-SRV2).

Regel: Ziel nur http/https, ohne Zugangsdaten in der URL. Jede aufgelöste Adresse muss global erreichbar sein; verboten
sind Loopback, private Netze, link-local (inkl. Metadaten 169.254.169.254), CGNAT, ULA, Multicast, reservierte und
unspezifizierte Adressen. Ausnahme nur über WL_UPSTREAM_ALLOW (Netze, die der Instanz-Admin freigibt, z. B. der eigene
whisper-server im Firmennetz).

DNS-Rebinding: Der Proxy löst den Namen selbst auf, prüft *alle* Adressen und verbindet sich danach mit genau der
geprüften IP (Host-Header und TLS-SNI bleiben der Name). Eine zweite Auflösung durch die HTTP-Bibliothek gibt es nicht.
Weiterleitungen folgt der Proxy nicht.
Doku: https://docs.python.org/3/library/ipaddress.html (is_global, ipv4_mapped; abgerufen 2026-11-01),
https://www.python-httpx.org/advanced/extensions/ (sni_hostname; abgerufen 2026-11-01).
"""

import ipaddress
import socket
from collections.abc import Callable
from dataclasses import dataclass
from urllib.parse import urlsplit

Netz = ipaddress.IPv4Network | ipaddress.IPv6Network
Aufloeser = Callable[[str, int], list[str]]

# Metadaten-Dienste der Cloud-Anbieter: immer gesperrt, auch wenn WL_UPSTREAM_ALLOW ihr Netz freigibt (Prüfrunde 1
# Befund 4). AWS/GCP/Azure/Oracle/DigitalOcean 169.254.169.254, AWS IPv6 fd00:ec2::254, AWS ECS 169.254.170.2,
# Alibaba 100.100.100.200.
METADATEN = {ipaddress.ip_address("100.100.100.200"), ipaddress.ip_address("169.254.169.254"),
             ipaddress.ip_address("169.254.170.2"), ipaddress.ip_address("fd00:ec2::254")}


class ZielVerboten(ValueError):
    """Grund als kurzer Code (nur fürs Log, ohne URL): schema, zugangsdaten, host, aufloesung, adresse."""


@dataclass(frozen=True)
class Ziel:
    url: str  # URL mit der geprüften IP statt des Namens
    host_header: str
    sni: str | None  # nur https


def netze_lesen(text: str) -> tuple[Netz, ...]:
    return tuple(ipaddress.ip_network(t.strip(), strict=False) for t in text.split(",") if t.strip())


def system_aufloesen(host: str, port: int) -> list[str]:
    infos = socket.getaddrinfo(host, port, type=socket.SOCK_STREAM)
    return list(dict.fromkeys(str(i[4][0]) for i in infos))


# Netze, die Python als global einstuft, die aber nicht direkt erreichbar sein sollen (6to4-Relay, site-local).
GESPERRT = (ipaddress.ip_network("192.88.99.0/24"), ipaddress.ip_network("fec0::/10"))
NAT64 = ipaddress.ip_network("64:ff9b::/96")


def _eingebettet(ip: ipaddress.IPv6Address) -> ipaddress.IPv4Address | None:
    """IPv4 in IPv6: gemappt (::ffff:a.b.c.d), NAT64 (64:ff9b::a.b.c.d), IPv4-kompatibel (::a.b.c.d), 6to4 (2002::/16)."""
    if ip.ipv4_mapped is not None:
        return ip.ipv4_mapped
    if ip in NAT64 or (int(ip) >> 32 == 0 and int(ip) > 1):
        return ipaddress.IPv4Address(int(ip) & 0xFFFFFFFF)
    return ip.sixtofour


def adresse_verboten(ip: ipaddress.IPv4Address | ipaddress.IPv6Address, erlaubt: tuple[Netz, ...]) -> bool:
    if isinstance(ip, ipaddress.IPv6Address) and (v4 := _eingebettet(ip)) is not None:
        ip = v4
    if ip in METADATEN or any(ip.version == n.version and ip in n for n in GESPERRT):
        return True
    if any(ip.version == n.version and ip in n for n in erlaubt):
        return False
    # is_global gilt in Python auch für Multicast (224.0.0.0/4, ff00::/8) – deshalb eigens ausschließen.
    return ip.is_multicast or not ip.is_global


def pruefen(url: str, erlaubt: tuple[Netz, ...], aufloeser: Aufloeser) -> Ziel:
    """Prüft die URL samt DNS-Auflösung; gibt die URL mit eingesetzter IP zurück. Wirft ZielVerboten."""
    try:
        teile = urlsplit(url)
        port = teile.port or (443 if teile.scheme == "https" else 80)
    except ValueError as e:
        raise ZielVerboten("host") from e
    if teile.scheme not in ("http", "https"):
        raise ZielVerboten("schema")
    if teile.username is not None or teile.password is not None:
        raise ZielVerboten("zugangsdaten")
    host = teile.hostname or ""
    if not host:
        raise ZielVerboten("host")
    try:
        adressen = aufloeser(host, port)
    except (OSError, UnicodeError) as e:
        raise ZielVerboten("aufloesung") from e
    if not adressen:
        raise ZielVerboten("aufloesung")
    geprueft = []
    for a in adressen:
        try:
            ip = ipaddress.ip_address(a.split("%")[0])
        except ValueError as e:
            raise ZielVerboten("adresse") from e
        if adresse_verboten(ip, erlaubt):
            raise ZielVerboten("adresse")
        geprueft.append(ip)
    ip = geprueft[0]
    ip_text = f"[{ip}]" if ip.version == 6 else str(ip)
    netloc = f"{ip_text}:{port}"
    host_header = teile.netloc.rsplit("@", 1)[-1]
    rest = teile.path + (f"?{teile.query}" if teile.query else "")
    return Ziel(f"{teile.scheme}://{netloc}{rest}", host_header, host if teile.scheme == "https" else None)


def beim_speichern_ok(url: str, erlaubt: tuple[Netz, ...], aufloeser: Aufloeser) -> bool:
    """Formularprüfung: verbotene Ziele ablehnen; ein (noch) nicht auflösbarer Name ist erlaubt, die Anfrage prüft erneut."""
    try:
        pruefen(url, erlaubt, aufloeser)
    except ZielVerboten as e:
        return str(e) == "aufloesung"
    return True
