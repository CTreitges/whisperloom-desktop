"""Admin-Anmeldung: Passwort (argon2), TOTP, Portal-Sitzungen und CSRF.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/auth.py
Context7: argon2-cffi — PasswordHasher().hash/verify, VerifyMismatchError vor VerificationError fangen,
check_needs_rehash nach erfolgreichem verify.
"""

import sqlite3

from argon2 import PasswordHasher
from argon2.exceptions import InvalidHashError, VerificationError, VerifyMismatchError

from . import krypto, totp
from .krypto import Entschluesselungsfehler, Krypto

_ph = PasswordHasher()
# Gegen Zeitmessung: unbekannte Benutzer prüfen gegen einen Dummy-Hash.
_DUMMY = _ph.hash("whisperloom-dummy-passwort")

IDLE_MS = 30 * 60 * 1000
ABSOLUT_MS = 12 * 3600 * 1000
EINLADUNG_MS = 72 * 3600 * 1000
# Passwort-Fehlversuche zählen je (Benutzer, Client-IP bzw. IPv6-/64), nicht je Benutzer allein (Review M-5): Wer
# nur den Namen kennt, sperrt sonst den echten Admin aus. Erfolgreiche Logins zählen nicht.
FEHLVERSUCHE_MAX = 5
SPERRE_MS = 15 * 60 * 1000
# Weiche globale Grenze je Benutzer: Ab so vielen Fehlversuchen je Stunde (alle IPs) bekommt jede IP nur noch einen
# Versuch je Stunde. Eine neue IP (der echte Admin) darf weiter einmal, es gibt keine harte Sperre des Kontos.
GLOBAL_MAX = 50
GLOBAL_MS = 3600 * 1000
PASSWORT_MIN = 12
PASSWORT_MAX = 128


def passwort_hash(passwort: str) -> str:
    return _ph.hash(passwort)


def passwort_pruefen(hash_: str | None, passwort: str) -> bool:
    try:
        return _ph.verify(hash_ or _DUMMY, passwort) and hash_ is not None
    except VerifyMismatchError:
        return False
    except (VerificationError, InvalidHashError):
        return False


def passwort_regel(neu: str, wiederholung: str) -> str | None:
    """None = in Ordnung, sonst Textschlüssel für das Formular."""
    if neu != wiederholung:
        return "h.pw_ungleich"
    if len(neu) < PASSWORT_MIN:
        return "h.pw_kurz"
    if len(neu) > PASSWORT_MAX:
        return "h.pw_lang"
    return None


def _fehlversuch(con: sqlite3.Connection, row: sqlite3.Row, jetzt: int) -> None:
    n = row["fehlversuche"] + 1
    if n >= FEHLVERSUCHE_MAX:
        con.execute("UPDATE admin SET fehlversuche = 0, gesperrt_bis_ms = ? WHERE id = ?", (jetzt + SPERRE_MS, row["id"]))
    else:
        con.execute("UPDATE admin SET fehlversuche = ? WHERE id = ?", (n, row["id"]))


def _ip_gesperrt(con: sqlite3.Connection, b_hmac: str, ip_hmac: str, jetzt: int) -> bool:
    """Fehlversuche dieser IP für diesen Benutzer (seit dem letzten Erfolg) bzw. weiche Grenze über alle IPs."""
    row = con.execute(
        "SELECT"
        " SUM(ip_hmac = :i AND zeit_ms > MAX(:sperre, COALESCE((SELECT MAX(zeit_ms) FROM login_versuch"
        "     WHERE benutzer_hmac = :b AND ip_hmac = :i AND erfolg = 1), 0))) AS hier,"
        " SUM(ip_hmac = :i) AS hier_stunde, COUNT(*) AS gesamt "
        "FROM login_versuch WHERE benutzer_hmac = :b AND erfolg = 0 AND zeit_ms > :stunde",
        {"b": b_hmac, "i": ip_hmac, "sperre": jetzt - SPERRE_MS, "stunde": jetzt - GLOBAL_MS},
    ).fetchone()
    if (row["hier"] or 0) >= FEHLVERSUCHE_MAX:
        return True
    return row["gesamt"] >= GLOBAL_MAX and (row["hier_stunde"] or 0) >= 1


def anmelden(con: sqlite3.Connection, kry: Krypto, benutzer: str, passwort: str, ip: str, jetzt: int):
    """("ok", admin) | ("falsch", None) | ("gesperrt", None) | ("konto_gesperrt", None).

    `ip` ist der Limit-Schlüssel (IPv4-Adresse bzw. IPv6-/64, `netz.limit_ip`). Unbekannte Namen zählen genauso wie
    bekannte, damit beide gleich aussehen. Die Konto-Sperre `gesperrt_bis_ms` setzen nur noch TOTP-Fehlversuche
    (dafür braucht man das Passwort).
    """
    benutzer = benutzer.strip().lower()
    b_hmac, ip_hmac = kry.hmac("benutzer:" + benutzer), kry.hmac("ip:" + ip)
    row = con.execute("SELECT * FROM admin WHERE benutzer = ?", (benutzer,)).fetchone()
    if (row is not None and row["gesperrt_bis_ms"] > jetzt) or _ip_gesperrt(con, b_hmac, ip_hmac, jetzt):
        passwort_pruefen(None, passwort)  # gleiche Laufzeit; gesperrte Versuche zählen nicht weiter
        return ("gesperrt", None)
    ergebnis: tuple[str, sqlite3.Row | None]
    if passwort_pruefen(row["pw_hash"] if row is not None else None, passwort):
        if row["gesperrt"]:
            ergebnis = ("konto_gesperrt", None)
        else:
            ergebnis = ("ok", row)
            neu_hash = _ph.hash(passwort) if _ph.check_needs_rehash(row["pw_hash"]) else row["pw_hash"]
            con.execute("UPDATE admin SET fehlversuche = 0, pw_hash = ? WHERE id = ?", (neu_hash, row["id"]))
    else:
        ergebnis = ("falsch", None)
    con.execute(
        "INSERT INTO login_versuch (ip_hmac, benutzer_hmac, zeit_ms, erfolg) VALUES (?, ?, ?, ?)",
        (ip_hmac, b_hmac, jetzt, int(ergebnis[0] == "ok")),
    )
    con.execute("DELETE FROM login_versuch WHERE zeit_ms < ?", (jetzt - 86_400_000,))
    return ergebnis


def sperre_aufheben(con: sqlite3.Connection, kry: Krypto, admin_id: str, benutzer: str) -> None:
    """Konto-Sperre und gezählte Fehlversuche zurücksetzen (Portal „Entsperren“, Einladung, CLI `admin-unlock`)."""
    con.execute("UPDATE admin SET fehlversuche = 0, gesperrt_bis_ms = 0 WHERE id = ?", (admin_id,))
    con.execute("DELETE FROM login_versuch WHERE benutzer_hmac = ? AND erfolg = 0", (kry.hmac("benutzer:" + benutzer),))


# --- TOTP-Geheimnis: verschlüsselt am Admin, beim Einrichten vorläufig an der Sitzung ---


def totp_geheimnis(kry: Krypto, enc: bytes | None, nonce: bytes | None, admin_id: str) -> str | None:
    if not enc:
        return None
    try:
        return kry.entschluesseln(enc, nonce, f"{admin_id}|totp")
    except Entschluesselungsfehler:
        return None


def totp_pruefen(con: sqlite3.Connection, kry: Krypto, admin: sqlite3.Row, code: str, jetzt: int) -> bool:
    geheimnis = totp_geheimnis(kry, admin["totp_enc"], admin["totp_nonce"], admin["id"])
    if geheimnis is None:
        return False
    schritt = totp.pruefen(geheimnis, code, jetzt, admin["totp_schritt"])
    if schritt is None:
        _fehlversuch(con, admin, jetzt)
        return False
    con.execute("UPDATE admin SET totp_schritt = ?, fehlversuche = 0 WHERE id = ?", (schritt, admin["id"]))
    return True


def totp_speichern(con: sqlite3.Connection, kry: Krypto, admin_id: str, geheimnis: str | None, schritt: int = 0) -> None:
    enc, nonce = kry.verschluesseln(geheimnis, f"{admin_id}|totp") if geheimnis else (None, None)
    con.execute("UPDATE admin SET totp_enc = ?, totp_nonce = ?, totp_schritt = ? WHERE id = ?", (enc, nonce, schritt, admin_id))


# --- Sitzungen: Cookie-Wert 256 bit, in der DB nur als SHA-256 ---


def sitzung_anlegen(con: sqlite3.Connection, admin_id: str, stufe: str, jetzt: int) -> str:
    wert = krypto.token()
    con.execute(
        "INSERT INTO sitzung (id_hash, admin_id, csrf, stufe, angelegt_ms, zuletzt_ms) VALUES (?, ?, ?, ?, ?, ?)",
        (krypto.sha256(wert), admin_id, krypto.token(), stufe, jetzt, jetzt),
    )
    return wert


def sitzung_laden(con: sqlite3.Connection, wert: str | None, jetzt: int):
    if not wert or len(wert) > 100:
        return None
    row = con.execute(
        "SELECT s.*, a.benutzer, a.rolle, a.firma_id, a.gesperrt, a.totp_enc IS NOT NULL AS hat_totp "
        "FROM sitzung s JOIN admin a ON a.id = s.admin_id "
        "WHERE s.id_hash = ?",
        (krypto.sha256(wert),),
    ).fetchone()
    if row is None:
        return None
    if row["zuletzt_ms"] < jetzt - IDLE_MS or row["angelegt_ms"] < jetzt - ABSOLUT_MS or row["gesperrt"]:
        con.execute("DELETE FROM sitzung WHERE id_hash = ?", (row["id_hash"],))
        return None
    con.execute("UPDATE sitzung SET zuletzt_ms = ? WHERE id_hash = ?", (jetzt, row["id_hash"]))
    return row


def sitzung_loeschen(con: sqlite3.Connection, wert: str | None) -> None:
    if wert:
        con.execute("DELETE FROM sitzung WHERE id_hash = ?", (krypto.sha256(wert),))


def sitzungen_beenden(con: sqlite3.Connection, admin_id: str, ausser_wert: str | None = None) -> None:
    behalten = krypto.sha256(ausser_wert) if ausser_wert else ""
    con.execute("DELETE FROM sitzung WHERE admin_id = ? AND id_hash != ?", (admin_id, behalten))


# --- CSRF ohne Sitzung (Login, Einladung, local_only): Double-Submit (Cookie-Wert + HMAC davon im Formular) ---


def vor_csrf(kry: Krypto, cookie_wert: str) -> str:
    return kry.hmac("csrf:" + cookie_wert)


def vor_csrf_ok(kry: Krypto, cookie_wert: str | None, formular: str | None) -> bool:
    if not cookie_wert or not formular:
        return False
    return krypto.gleich(vor_csrf(kry, cookie_wert), formular)
