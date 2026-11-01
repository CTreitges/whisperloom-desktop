"""Einstellungen der Instanz aus der Umgebung und Laden des Hauptschlüssels (README „Konfiguration“).

Basis: whisperloom-android feat/simple-portal server/simple-whisper/config.py
"""

import ipaddress
import os
import stat
import time
from collections.abc import Callable
from dataclasses import dataclass, field
from pathlib import Path

from . import ssrf

# Admin-Anmeldearten. `file` schließt alle anderen aus (kein Portal).
ANMELDEARTEN = ("password_totp", "password", "trusted_header", "local_only")
DATEI_MODUS = "file"

Netz = ipaddress.IPv4Network | ipaddress.IPv6Network


def jetzt_ms() -> int:
    return int(time.time() * 1000)


class KonfigFehler(RuntimeError):
    pass


def anmeldearten_lesen(text: str) -> tuple[str, ...]:
    arten = tuple(dict.fromkeys(t.strip() for t in text.split(",") if t.strip()))
    if not arten:
        raise KonfigFehler("WL_AUTH_MODES ist leer")
    if DATEI_MODUS in arten:
        if len(arten) > 1:
            raise KonfigFehler("WL_AUTH_MODES: file lässt sich nicht mit anderen Anmeldearten kombinieren")
        return arten
    falsch = [a for a in arten if a not in ANMELDEARTEN]
    if falsch:
        raise KonfigFehler(f"WL_AUTH_MODES: unbekannt {', '.join(falsch)} (erlaubt: {', '.join(ANMELDEARTEN)}, file)")
    return arten


def netze_lesen(text: str) -> tuple[Netz, ...]:
    try:
        return tuple(ipaddress.ip_network(t.strip(), strict=False) for t in text.split(",") if t.strip())
    except ValueError as e:
        raise KonfigFehler(f"WL_TRUSTED_PROXIES: {e}") from e


def upstream_netze_lesen(text: str) -> tuple[ssrf.Freigabe, ...]:
    try:
        return ssrf.freigaben_lesen(text)
    except ValueError as e:
        raise KonfigFehler(f"WL_UPSTREAM_ALLOW: {e}") from e


@dataclass
class Einstellungen:
    daten: Path
    basis_url: str = "http://127.0.0.1:8080"
    anmeldearten: tuple[str, ...] = ("password_totp",)
    vertraute_proxies: tuple[Netz, ...] = ()
    header_benutzer: str = "Remote-User"
    konfig_datei: Path | None = None
    master_hex: str | None = None
    min_app_version: str = ""
    # Proxy (W2-SRV2): interne Ziele, die der Instanz-Admin für Anbieter-URLs freigibt (ssrf.py, mit Port ab W2-SFIX),
    # und DNS-Auflösung.
    upstream_erlaubt: tuple[ssrf.Freigabe, ...] = ()
    uhr: Callable[[], int] = field(default=jetzt_ms)
    aufloeser: ssrf.Aufloeser = field(default=ssrf.system_aufloesen)

    @property
    def db_pfad(self) -> Path:
        return self.daten / "whisperloom-server.db"

    @property
    def ausgabe(self) -> Path:
        return self.daten / "ausgabe"

    @property
    def datei_modus(self) -> bool:
        return self.anmeldearten == (DATEI_MODUS,)

    @property
    def sicher(self) -> bool:
        """https-Basis-URL → Secure-Cookies mit __Host-Präfix; http nur für lokale Tests und local_only."""
        return self.basis_url.startswith("https://")

    def pruefen(self) -> None:
        if not self.basis_url.startswith(("https://", "http://")):
            raise KonfigFehler("WL_BASE_URL muss mit https:// oder http:// beginnen")
        if self.datei_modus and self.konfig_datei is None:
            raise KonfigFehler("WL_AUTH_MODES=file braucht WL_CONFIG_FILE")
        if "trusted_header" in self.anmeldearten and not self.vertraute_proxies:
            raise KonfigFehler("trusted_header braucht WL_TRUSTED_PROXIES (IP-Adressen bzw. Netze des Proxys)")

    @classmethod
    def aus_umgebung(cls) -> "Einstellungen":
        env = os.environ
        daten = env.get("WL_DATA_DIR") or str(Path.home() / ".local/share/whisperloom-server")
        datei = env.get("WL_CONFIG_FILE")
        e = cls(
            daten=Path(daten),
            basis_url=env.get("WL_BASE_URL", cls.basis_url).rstrip("/"),
            # Standard (Christof-Entscheidung 2026-11-01): Zwei-Faktor optional, beim ersten Login überspringbar.
            anmeldearten=anmeldearten_lesen(env.get("WL_AUTH_MODES", "password_totp,password")),
            vertraute_proxies=netze_lesen(env.get("WL_TRUSTED_PROXIES", "")),
            header_benutzer=env.get("WL_TRUSTED_HEADER", cls.header_benutzer),
            konfig_datei=Path(datei) if datei else None,
            master_hex=env.get("WL_MASTER_KEY") or None,
            min_app_version=env.get("WL_MIN_APP_VERSION", ""),
            upstream_erlaubt=upstream_netze_lesen(env.get("WL_UPSTREAM_ALLOW", "")),
        )
        e.pruefen()
        return e

    def master_pfad(self) -> Path:
        eigen = os.environ.get("WL_MASTER_KEY_FILE")
        if eigen:
            return Path(eigen)
        cred = os.environ.get("CREDENTIALS_DIRECTORY")
        if cred and (Path(cred) / "master.key").exists():
            return Path(cred) / "master.key"
        return self.daten / "master.key"


def nur_fuer_besitzer(pfad: Path) -> bool:
    """Unter POSIX: keine Rechte für Gruppe/Andere. Unter Windows gibt es kein chmod-Modell → True."""
    if os.name != "posix":
        return True
    return stat.S_IMODE(pfad.stat().st_mode) & 0o077 == 0


def _hex_zu_master(roh: str) -> bytes:
    try:
        master = bytes.fromhex(roh.strip())
    except ValueError as e:
        raise KonfigFehler("Hauptschlüssel ist kein Hex") from e
    if len(master) != 32:
        raise KonfigFehler("Hauptschlüssel muss 32 Byte (64 Hex-Zeichen) haben")
    return master


def lade_master(einst: Einstellungen) -> bytes:
    if einst.master_hex:
        return _hex_zu_master(einst.master_hex)
    pfad = einst.master_pfad()
    if not pfad.exists():
        raise KonfigFehler(f"Hauptschlüssel fehlt: {pfad} (python -m whisperloom_server init)")
    if pfad.parent == einst.daten and not nur_fuer_besitzer(pfad):
        raise KonfigFehler(f"Hauptschlüssel hat zu weite Rechte: {pfad} (chmod 600)")
    try:
        return _hex_zu_master(pfad.read_bytes().decode("ascii"))
    except UnicodeDecodeError as e:
        raise KonfigFehler("Hauptschlüssel ist kein Hex") from e
