"""Keys je Firma, AES-GCM-verschlüsselt (Hauptschlüssel aus Env/Datei, nie in der DB).

Name eines Keys wie `secrets` einer Provisioning-Datei (docs/settings-format.md): Zugang `stt`, `stt_fallback`, `llm`,
`llm_fallback` (mit Anbieter und optional Basis-URL) oder eine Anbieter-ID (auch eigener Anbieter).
Proxy-Modus (W2-SRV2) liest Keys über [lesen]; Verteil-Modus gibt sie an freigegebene Geräte ([fuer_geraet]).
"""

import re
import sqlite3

from .katalog import ANBIETER
from .krypto import Krypto

ZUGAENGE = ("stt", "stt_fallback", "llm", "llm_fallback")
ABSCHNITT = {"stt": "stt", "stt_fallback": "sttFallback", "llm": "llm", "llm_fallback": "llmFallback"}
_NAME = re.compile(r"^[a-z0-9][a-z0-9_-]{1,39}$")
_KEY = re.compile(r"^[\x21-\x7e]{8,500}$")
_URL = re.compile(r"^https?://[^\s]{1,300}$")


class SchluesselFehler(ValueError):
    pass


def speichern(con: sqlite3.Connection, kry: Krypto, firma_id: str, name: str, anbieter: str, basis_url: str, key: str,
              jetzt: int) -> None:
    name, anbieter, basis_url, key = name.strip(), anbieter.strip(), basis_url.strip(), key.strip()
    if not _NAME.match(name):
        raise SchluesselFehler("h.name")
    if name in ZUGAENGE:
        if anbieter not in ANBIETER:
            raise SchluesselFehler("h.anbieter")
    else:
        anbieter = name
    if basis_url and not _URL.match(basis_url):
        raise SchluesselFehler("h.wert")
    if not _KEY.match(key):
        raise SchluesselFehler("h.kein_key")
    chiffre, nonce = kry.verschluesseln(key, f"{firma_id}|{name}")
    con.execute(
        "INSERT INTO schluessel (firma_id, zugang, anbieter, basis_url, chiffre, nonce, letzte4, geaendert_ms) "
        "VALUES (?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT (firma_id, zugang) DO UPDATE SET anbieter = excluded.anbieter, "
        "basis_url = excluded.basis_url, chiffre = excluded.chiffre, nonce = excluded.nonce, letzte4 = excluded.letzte4, "
        "geaendert_ms = excluded.geaendert_ms",
        (firma_id, name, anbieter, basis_url, chiffre, nonce, key[-4:], jetzt),
    )


def entfernen(con: sqlite3.Connection, firma_id: str, name: str) -> bool:
    return con.execute("DELETE FROM schluessel WHERE firma_id = ? AND zugang = ?", (firma_id, name)).rowcount == 1


def liste(con: sqlite3.Connection, firma_id: str) -> list[sqlite3.Row]:
    """Ohne Chiffre: Name, Anbieter, Basis-URL, letzte 4 Zeichen, Änderungszeit."""
    return con.execute(
        "SELECT zugang, anbieter, basis_url, letzte4, geaendert_ms FROM schluessel WHERE firma_id = ? ORDER BY zugang",
        (firma_id,),
    ).fetchall()


def lesen(con: sqlite3.Connection, kry: Krypto, firma_id: str, name: str) -> str | None:
    row = con.execute(
        "SELECT chiffre, nonce FROM schluessel WHERE firma_id = ? AND zugang = ?", (firma_id, name)
    ).fetchone()
    return None if row is None else kry.entschluesseln(row["chiffre"], row["nonce"], f"{firma_id}|{name}")


def fuer_geraet(con: sqlite3.Connection, kry: Krypto, firma_id: str) -> tuple[dict[str, str], dict[str, tuple[str, str]]]:
    """Verteil-Modus: (secrets Name → Key, Zugang → (Anbieter, Basis-URL)). Nur für freigegebene Geräte aufrufen."""
    secrets: dict[str, str] = {}
    zugaenge: dict[str, tuple[str, str]] = {}
    for r in liste(con, firma_id):
        secrets[r["zugang"]] = lesen(con, kry, firma_id, r["zugang"]) or ""
        if r["zugang"] in ZUGAENGE:
            zugaenge[r["zugang"]] = (r["anbieter"], r["basis_url"])
    return secrets, zugaenge
