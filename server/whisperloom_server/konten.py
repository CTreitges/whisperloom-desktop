"""Admin-Konten (Instanz-Admin, owner, admin) und Einladungs-Links. Genutzt von CLI und Portal."""

import re
import sqlite3

from . import auth, krypto

BENUTZER = re.compile(r"^[a-z0-9][a-z0-9._@-]{1,63}$")
ROLLEN = ("owner", "admin")


class KontoFehler(ValueError):
    pass


def anlegen(con: sqlite3.Connection, firma_id: str | None, benutzer: str, rolle: str, jetzt: int,
            passwort: str | None = None) -> str:
    benutzer = benutzer.strip().lower()
    if not BENUTZER.match(benutzer):
        raise KontoFehler("h.benutzer")
    if (rolle == "instance") != (firma_id is None) or rolle not in (*ROLLEN, "instance"):
        raise KontoFehler("h.rolle")
    if con.execute("SELECT 1 FROM admin WHERE benutzer = ?", (benutzer,)).fetchone():
        raise KontoFehler("h.benutzer_vergeben")
    aid = krypto.kurz_id("a_")
    con.execute(
        "INSERT INTO admin (id, firma_id, benutzer, rolle, pw_hash, angelegt_ms) VALUES (?, ?, ?, ?, ?, ?)",
        (aid, firma_id, benutzer, rolle, auth.passwort_hash(passwort) if passwort else None, jetzt),
    )
    return aid


def einladung(con: sqlite3.Connection, basis_url: str, admin_id: str, jetzt: int) -> tuple[str, int]:
    """Neuer Einladungs-Link (einmal verwendbar); alte Links und alle Sitzungen des Kontos werden ungültig."""
    tok = krypto.token()
    ablauf = jetzt + auth.EINLADUNG_MS
    con.execute("DELETE FROM einladung WHERE admin_id = ?", (admin_id,))
    con.execute(
        "INSERT INTO einladung (token_hash, admin_id, ablauf_ms) VALUES (?, ?, ?)", (krypto.sha256(tok), admin_id, ablauf)
    )
    auth.sitzungen_beenden(con, admin_id)
    return f"{basis_url}/einladung/{tok}", ablauf


def einladung_laden(con: sqlite3.Connection, token: str, jetzt: int):
    if len(token) > 100:
        return None
    return con.execute(
        "SELECT e.*, a.benutzer, a.firma_id, a.rolle FROM einladung e JOIN admin a ON a.id = e.admin_id "
        "WHERE e.token_hash = ? AND e.verwendet = 0 AND e.ablauf_ms > ?",
        (krypto.sha256(token), jetzt),
    ).fetchone()


def owner_anzahl(con: sqlite3.Connection, firma_id: str) -> int:
    return con.execute(
        "SELECT COUNT(*) FROM admin WHERE firma_id = ? AND rolle = 'owner' AND gesperrt = 0", (firma_id,)
    ).fetchone()[0]
