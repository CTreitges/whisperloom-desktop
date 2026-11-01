"""SSRF-Schutz für Anbieter-URLs, die ein Firmen-Admin einträgt (Proxy, W2-SRV2).

Regel: Ziel nur http/https, ohne Zugangsdaten in der URL. Jede aufgelöste Adresse muss global erreichbar sein; verboten
sind Loopback, private Netze, link-local (inkl. Metadaten 169.254.169.254), CGNAT, ULA, Multicast, reservierte und
unspezifizierte Adressen. Ausnahme nur über WL_UPSTREAM_ALLOW (Netze, die der Instanz-Admin freigibt, z. B. der eigene
whisper-server im Firmennetz). Eine Freigabe mit Port (`127.0.0.2:8100`, `[fd00::5]:8100`, W2-SFIX) gilt nur für diesen
Port, ohne Port für alle Ports (rückwärtskompatibel).

Basis-URL (W2-SFIX): nur http/https mit Host, optional Port und Pfad. Fragment, Query, Benutzerinfo, Backslash, Leer- und
Steuerzeichen sind verboten, beim Speichern und je Anfrage; sonst schneidet ein `#`/`?` den angehängten Anbieter-Pfad ab
und der Admin wählt den Pfad auf dem Ziel frei. Den Anbieter-Pfad setzt url_bauen aus URL-Bausteinen zusammen.

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
from urllib.parse import urlsplit, urlunsplit

Netz = ipaddress.IPv4Network | ipaddress.IPv6Network
Aufloeser = Callable[[str, int], list[str]]

# Metadaten-Dienste der Cloud-Anbieter: immer gesperrt, auch wenn WL_UPSTREAM_ALLOW ihr Netz freigibt (Prüfrunde 1
# Befund 4). AWS/GCP/Azure/Oracle/DigitalOcean 169.254.169.254, AWS IPv6 fd00:ec2::254, AWS ECS 169.254.170.2,
# Alibaba 100.100.100.200.
METADATEN = {ipaddress.ip_address("100.100.100.200"), ipaddress.ip_address("169.254.169.254"),
             ipaddress.ip_address("169.254.170.2"), ipaddress.ip_address("fd00:ec2::254")}


class ZielVerboten(ValueError):
    """Grund als kurzer Code (nur fürs Log, ohne URL): schema, zugangsdaten, fragment, host, aufloesung, adresse."""


@dataclass(frozen=True)
class Ziel:
    url: str  # URL mit der geprüften IP statt des Namens
    host_header: str
    sni: str | None  # nur https


@dataclass(frozen=True)
class Freigabe:
    netz: Netz
    port: int | None = None  # None = alle Ports

    def gilt(self, ip: ipaddress.IPv4Address | ipaddress.IPv6Address, port: int) -> bool:
        return ip.version == self.netz.version and ip in self.netz and self.port in (None, port)


def _freigabe(text: str) -> Freigabe:
    """`IP`, `CIDR`, `IP:Port`, `CIDR:Port`, `[IPv6]`, `[IPv6]:Port`; IPv6 mit Port nur in eckigen Klammern."""
    port = None
    if text.startswith("["):
        adresse, klammer, rest = text[1:].partition("]")
        if not klammer or (rest and not rest.startswith(":")):
            raise ValueError(f"{text!r}: [IPv6]:Port erwartet")
        port = rest[1:] if rest else None
    elif text.count(":") == 1:
        adresse, _, port = text.partition(":")
    else:
        adresse = text
    if port is not None and not (port.isascii() and port.isdigit() and 1 <= int(port) <= 65535):
        raise ValueError(f"{text!r}: Port 1-65535 erwartet")
    return Freigabe(ipaddress.ip_network(adresse, strict=False), int(port) if port is not None else None)


def freigaben_lesen(text: str) -> tuple[Freigabe, ...]:
    """WL_UPSTREAM_ALLOW: kommagetrennte Freigaben; wirft ValueError bei ungültigen Einträgen."""
    return tuple(_freigabe(t.strip()) for t in text.split(",") if t.strip())


def basis_fehler(url: str) -> str | None:
    """Strenge Prüfung einer Basis-URL ohne Netzzugriff (Speichern und je Anfrage). Gibt den Grund zurück (zeichen,
    port, schema, host) oder None. Verboten sind #, ?, @ (Benutzerinfo), Backslash, Leer- und Steuerzeichen."""
    if any(z in "\\@#?" or z.isspace() or not z.isprintable() for z in url):
        return "zeichen"
    try:
        teile = urlsplit(url)
        _ = teile.port  # wirft ValueError bei ungültigem Port
    except ValueError:
        return "port"
    if teile.scheme not in ("http", "https"):
        return "schema"
    if not teile.hostname:
        return "host"
    return None


def url_bauen(basis: str, pfad: str) -> str:
    """Hängt den Anbieter-Pfad an den Pfad der Basis-URL (URL-Bausteine statt String-Verkettung): Query und Fragment
    der Basis fallen weg und können den angehängten Pfad nicht abschneiden."""
    teile = urlsplit(basis)
    return urlunsplit((teile.scheme, teile.netloc, teile.path.rstrip("/") + pfad, "", ""))


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


def adresse_verboten(ip: ipaddress.IPv4Address | ipaddress.IPv6Address, erlaubt: tuple[Freigabe, ...],
                     port: int) -> bool:
    if isinstance(ip, ipaddress.IPv6Address) and (v4 := _eingebettet(ip)) is not None:
        ip = v4
    if ip in METADATEN or any(ip.version == n.version and ip in n for n in GESPERRT):
        return True
    if any(f.gilt(ip, port) for f in erlaubt):
        return False
    # is_global gilt in Python auch für Multicast (224.0.0.0/4, ff00::/8) – deshalb eigens ausschließen.
    return ip.is_multicast or not ip.is_global


def pruefen(url: str, erlaubt: tuple[Freigabe, ...], aufloeser: Aufloeser) -> Ziel:
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
    if "#" in url or "\\" in url:
        raise ZielVerboten("fragment")
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
        if adresse_verboten(ip, erlaubt, port):
            raise ZielVerboten("adresse")
        geprueft.append(ip)
    ip = geprueft[0]
    ip_text = f"[{ip}]" if ip.version == 6 else str(ip)
    netloc = f"{ip_text}:{port}"
    host_header = teile.netloc.rsplit("@", 1)[-1]
    rest = teile.path + (f"?{teile.query}" if teile.query else "")
    return Ziel(f"{teile.scheme}://{netloc}{rest}", host_header, host if teile.scheme == "https" else None)


def beim_speichern_ok(url: str, erlaubt: tuple[Freigabe, ...], aufloeser: Aufloeser) -> bool:
    """Formularprüfung: verbotene Ziele ablehnen; ein (noch) nicht auflösbarer Name ist erlaubt, die Anfrage prüft erneut."""
    try:
        pruefen(url, erlaubt, aufloeser)
    except ZielVerboten as e:
        return str(e) == "aufloesung"
    return True
