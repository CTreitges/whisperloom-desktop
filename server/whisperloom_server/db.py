"""SQLite-Schema mit fortlaufenden Migrationen. stdlib sqlite3, eine Verbindung je Anfrage, WAL.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/db.py
Erweiterung (z. B. W2-SRV2 Nutzung/Budget): neuen Eintrag an MIGRATIONEN anhängen, bestehende nie ändern.
Jede Tabelle mit Firmenbezug hat `firma_id`; jede Abfrage im Portal filtert danach (Mandanten-Trennung).
"""

import os
import sqlite3
from pathlib import Path

_V1 = """
CREATE TABLE firma (
    id TEXT PRIMARY KEY,
    slug TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    key_modus TEXT NOT NULL DEFAULT 'proxy' CHECK (key_modus IN ('proxy', 'distribute')),
    auto_freigabe INTEGER NOT NULL DEFAULT 0,
    anmeldearten TEXT NOT NULL DEFAULT '',
    quelle TEXT NOT NULL DEFAULT 'portal' CHECK (quelle IN ('portal', 'file')),
    gesperrt INTEGER NOT NULL DEFAULT 0,
    max_wartend INTEGER NOT NULL DEFAULT 50,
    angelegt_ms INTEGER NOT NULL
);
CREATE TABLE gruppe (
    id TEXT PRIMARY KEY,
    firma_id TEXT NOT NULL REFERENCES firma(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    angelegt_ms INTEGER NOT NULL,
    UNIQUE (firma_id, name)
);
CREATE INDEX gruppe_firma ON gruppe(firma_id);
CREATE TABLE firmencode (
    id TEXT PRIMARY KEY,
    firma_id TEXT NOT NULL REFERENCES firma(id) ON DELETE CASCADE,
    gruppe_id TEXT REFERENCES gruppe(id) ON DELETE CASCADE,
    code_hmac TEXT NOT NULL UNIQUE,
    code_enc BLOB NOT NULL,
    code_nonce BLOB NOT NULL,
    angelegt_ms INTEGER NOT NULL,
    UNIQUE (firma_id, gruppe_id)
);
CREATE TABLE admin (
    id TEXT PRIMARY KEY,
    firma_id TEXT REFERENCES firma(id) ON DELETE CASCADE,
    benutzer TEXT NOT NULL UNIQUE,
    rolle TEXT NOT NULL CHECK (rolle IN ('instance', 'owner', 'admin')),
    pw_hash TEXT,
    totp_enc BLOB,
    totp_nonce BLOB,
    totp_schritt INTEGER NOT NULL DEFAULT 0,
    gesperrt INTEGER NOT NULL DEFAULT 0,
    fehlversuche INTEGER NOT NULL DEFAULT 0,
    gesperrt_bis_ms INTEGER NOT NULL DEFAULT 0,
    angelegt_ms INTEGER NOT NULL,
    CHECK ((rolle = 'instance') = (firma_id IS NULL))
);
CREATE INDEX admin_firma ON admin(firma_id);
CREATE TABLE einladung (
    token_hash TEXT PRIMARY KEY,
    admin_id TEXT NOT NULL REFERENCES admin(id) ON DELETE CASCADE,
    ablauf_ms INTEGER NOT NULL,
    verwendet INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE sitzung (
    id_hash TEXT PRIMARY KEY,
    admin_id TEXT NOT NULL REFERENCES admin(id) ON DELETE CASCADE,
    csrf TEXT NOT NULL,
    stufe TEXT NOT NULL CHECK (stufe IN ('voll', 'totp', 'totp_einrichten')),
    totp_neu_enc BLOB,
    totp_neu_nonce BLOB,
    angelegt_ms INTEGER NOT NULL,
    zuletzt_ms INTEGER NOT NULL
);
CREATE TABLE login_versuch (
    id INTEGER PRIMARY KEY,
    ip_hmac TEXT NOT NULL,
    benutzer_hmac TEXT NOT NULL,
    zeit_ms INTEGER NOT NULL,
    erfolg INTEGER NOT NULL
);
CREATE INDEX login_versuch_zeit ON login_versuch(zeit_ms);
CREATE TABLE geraet (
    id TEXT PRIMARY KEY,
    firma_id TEXT NOT NULL REFERENCES firma(id) ON DELETE CASCADE,
    gruppe_id TEXT REFERENCES gruppe(id) ON DELETE SET NULL,
    token_hash TEXT NOT NULL UNIQUE,
    token_neu_hash TEXT UNIQUE,
    token_neu_ms INTEGER NOT NULL DEFAULT 0,
    token_ms INTEGER NOT NULL,
    name TEXT NOT NULL,
    plattform TEXT NOT NULL,
    app_version TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL CHECK (status IN ('pending', 'approved', 'blocked')),
    angelegt_ms INTEGER NOT NULL,
    zuletzt_ms INTEGER NOT NULL
);
CREATE INDEX geraet_firma ON geraet(firma_id);
CREATE TABLE richtlinie (
    firma_id TEXT NOT NULL REFERENCES firma(id) ON DELETE CASCADE,
    gruppe_id TEXT NOT NULL DEFAULT '',
    pfad TEXT NOT NULL,
    stufe TEXT NOT NULL CHECK (stufe IN ('fixed', 'default', 'free')),
    wert TEXT NOT NULL,
    PRIMARY KEY (firma_id, gruppe_id, pfad)
);
CREATE TABLE schluessel (
    firma_id TEXT NOT NULL REFERENCES firma(id) ON DELETE CASCADE,
    zugang TEXT NOT NULL,
    anbieter TEXT NOT NULL,
    basis_url TEXT NOT NULL DEFAULT '',
    chiffre BLOB NOT NULL,
    nonce BLOB NOT NULL,
    letzte4 TEXT NOT NULL,
    geaendert_ms INTEGER NOT NULL,
    PRIMARY KEY (firma_id, zugang)
);
CREATE TABLE audit (
    id INTEGER PRIMARY KEY,
    firma_id TEXT,
    akteur TEXT NOT NULL,
    aktion TEXT NOT NULL,
    ziel TEXT NOT NULL DEFAULT '',
    zeit_ms INTEGER NOT NULL
);
CREATE INDEX audit_firma_zeit ON audit(firma_id, zeit_ms);
"""

MIGRATIONEN: list[str] = [_V1]
SCHEMA_VERSION = len(MIGRATIONEN)


def verbinden(pfad: Path) -> sqlite3.Connection:
    con = sqlite3.connect(pfad, timeout=10, check_same_thread=False, isolation_level=None)
    con.row_factory = sqlite3.Row
    con.execute("PRAGMA foreign_keys = ON")
    con.execute("PRAGMA busy_timeout = 10000")
    return con


def einrichten(pfad: Path) -> None:
    """Legt die DB an (Rechte 600) bzw. spielt fehlende Migrationen ein."""
    neu = not pfad.exists()
    pfad.parent.mkdir(parents=True, exist_ok=True)
    con = verbinden(pfad)
    try:
        con.execute("PRAGMA journal_mode = WAL")
        version = con.execute("PRAGMA user_version").fetchone()[0]
        if version > SCHEMA_VERSION:
            raise RuntimeError(f"unbekannte Schema-Version {version} (Server zu alt)")
        for nummer in range(version, SCHEMA_VERSION):
            con.executescript("BEGIN;" + MIGRATIONEN[nummer] + f"PRAGMA user_version = {nummer + 1};COMMIT;")
    finally:
        con.close()
    if neu and os.name == "posix":
        os.chmod(pfad, 0o600)
