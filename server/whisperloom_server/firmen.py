"""Firmen (Mandanten), Firmencodes und Audit-Log.

Firmencodes liegen als HMAC (Suche) und verschlüsselt (Anzeige im Portal) vor, nie im Klartext.
Ein Code gehört zur Firma (gruppe_id NULL) oder zu einer Gruppe; Rotieren ersetzt ihn, der alte gilt sofort nicht mehr.
"""

import re
import sqlite3

from . import krypto
from .config import ANMELDEARTEN
from .krypto import Entschluesselungsfehler, Krypto

SLUG = re.compile(r"^[a-z0-9-]{2,40}$")


class FirmenFehler(ValueError):
    pass


def name_pruefen(name: str, max_laenge: int = 80) -> str:
    neu = "".join(c for c in name if c.isprintable()).strip()
    if not 1 <= len(neu) <= max_laenge:
        raise FirmenFehler("h.name")
    return neu


def anlegen(con: sqlite3.Connection, kry: Krypto, slug: str, name: str, jetzt: int, quelle: str = "portal") -> tuple[str, str]:
    """→ (firma_id, Firmencode im Klartext, nur zur einmaligen Ausgabe)."""
    if not SLUG.match(slug):
        raise FirmenFehler("h.slug")
    if con.execute("SELECT 1 FROM firma WHERE slug = ?", (slug,)).fetchone():
        raise FirmenFehler("h.slug_vergeben")
    fid = krypto.kurz_id("t_")
    con.execute(
        "INSERT INTO firma (id, slug, name, quelle, angelegt_ms) VALUES (?, ?, ?, ?, ?)",
        (fid, slug, name_pruefen(name), quelle, jetzt),
    )
    return fid, code_setzen(con, kry, fid, None, jetzt)


def code_setzen(con: sqlite3.Connection, kry: Krypto, firma_id: str, gruppe_id: str | None, jetzt: int,
                code: str | None = None) -> str:
    """Neuer (oder vorgegebener) Code für Firma bzw. Gruppe; ein vorhandener wird ersetzt."""
    code = code or krypto.neuer_code()
    enc, nonce = kry.verschluesseln(code, f"{firma_id}|code")
    con.execute(
        "DELETE FROM firmencode WHERE firma_id = ? AND gruppe_id IS ?", (firma_id, gruppe_id)
    )
    con.execute(
        "INSERT INTO firmencode (id, firma_id, gruppe_id, code_hmac, code_enc, code_nonce, angelegt_ms) "
        "VALUES (?, ?, ?, ?, ?, ?, ?)",
        (krypto.kurz_id("k_"), firma_id, gruppe_id, kry.hmac("code:" + code), enc, nonce, jetzt),
    )
    return code


def code_entfernen(con: sqlite3.Connection, firma_id: str, gruppe_id: str) -> None:
    con.execute("DELETE FROM firmencode WHERE firma_id = ? AND gruppe_id = ?", (firma_id, gruppe_id))


def code_lesen(con: sqlite3.Connection, kry: Krypto, firma_id: str, gruppe_id: str | None) -> str | None:
    row = con.execute(
        "SELECT code_enc, code_nonce FROM firmencode WHERE firma_id = ? AND gruppe_id IS ?", (firma_id, gruppe_id)
    ).fetchone()
    if row is None:
        return None
    try:
        return krypto.code_anzeigen(kry.entschluesseln(row["code_enc"], row["code_nonce"], f"{firma_id}|code"))
    except Entschluesselungsfehler:
        return None


def per_code(con: sqlite3.Connection, kry: Krypto, eingabe: str) -> sqlite3.Row | None:
    """Firmencode → Zeile mit firma_id, gruppe_id und Firmenfeldern; ungültig → None."""
    code = krypto.code_normalisieren(eingabe)
    if code is None:
        return None
    return con.execute(
        "SELECT c.gruppe_id, f.* FROM firmencode c JOIN firma f ON f.id = c.firma_id WHERE c.code_hmac = ?",
        (kry.hmac("code:" + code),),
    ).fetchone()


def anmeldearten(firma: sqlite3.Row, instanz: tuple[str, ...]) -> tuple[str, ...]:
    """Erlaubte Anmeldearten der Firma: Einschränkung der Firma ∩ Instanz (leer = alle der Instanz)."""
    alle = tuple(a for a in instanz if a in ANMELDEARTEN)
    eigen = [a for a in firma["anmeldearten"].split(",") if a]
    if not eigen:
        return alle
    schnitt = tuple(a for a in alle if a in eigen)
    if schnitt:
        return schnitt
    # Die Instanz bietet keine der gewählten Arten mehr an: auf die strengste Art ausweichen, nie auf eine ausgeschlossene.
    return ("password_totp",) if "password_totp" in alle else ()


def protokoll(con: sqlite3.Connection, firma_id: str | None, akteur: str, aktion: str, ziel: str, jetzt: int) -> None:
    """Audit-Eintrag: nur Metadaten (wer, was, welches Objekt), nie Werte, Keys oder Codes."""
    con.execute(
        "INSERT INTO audit (firma_id, akteur, aktion, ziel, zeit_ms) VALUES (?, ?, ?, ?, ?)",
        (firma_id, akteur[:64], aktion[:64], ziel[:120], jetzt),
    )
