"""TOTP nach RFC 6238 (SHA-1, 6 Ziffern, 30 s) mit Schutz gegen Wiederverwendung.

# Doku: https://datatracker.ietf.org/doc/html/rfc6238 und https://datatracker.ietf.org/doc/html/rfc4226 (abgerufen 2026-11-01)
# Doku: https://github.com/google/google-authenticator/wiki/Key-Uri-Format (abgerufen 2026-11-01)
"""

import base64
import hashlib
import hmac
import os
import struct
from urllib.parse import quote

SCHRITT_S = 30
ZIFFERN = 6
FENSTER = 1  # ±1 Schritt Uhrabweichung


def neues_geheimnis() -> str:
    """160 bit, Base32 ohne Padding (Format der Authenticator-Apps)."""
    return base64.b32encode(os.urandom(20)).decode("ascii").rstrip("=")


def _code(geheimnis: str, schritt: int) -> str:
    schluessel = base64.b32decode(geheimnis + "=" * (-len(geheimnis) % 8))
    digest = hmac.new(schluessel, struct.pack(">Q", schritt), hashlib.sha1).digest()
    versatz = digest[-1] & 0x0F
    zahl = struct.unpack(">I", digest[versatz : versatz + 4])[0] & 0x7FFFFFFF
    return str(zahl % 10**ZIFFERN).zfill(ZIFFERN)


def code_fuer(geheimnis: str, jetzt_ms: int) -> str:
    return _code(geheimnis, jetzt_ms // 1000 // SCHRITT_S)


def pruefen(geheimnis: str, eingabe: str, jetzt_ms: int, letzter_schritt: int) -> int | None:
    """Gültig → verwendeter Schritt (zum Speichern), sonst None. Ein Schritt ≤ letzter_schritt gilt nie zweimal."""
    eingabe = "".join(eingabe.split())
    if len(eingabe) != ZIFFERN or not eingabe.isdigit():
        return None
    aktuell = jetzt_ms // 1000 // SCHRITT_S
    for schritt in range(aktuell - FENSTER, aktuell + FENSTER + 1):
        if schritt > letzter_schritt and hmac.compare_digest(_code(geheimnis, schritt), eingabe):
            return schritt
    return None


def uri(geheimnis: str, konto: str, aussteller: str) -> str:
    return f"otpauth://totp/{quote(aussteller)}:{quote(konto)}?secret={geheimnis}&issuer={quote(aussteller)}"
