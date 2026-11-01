"""Datei-Modus (WL_AUTH_MODES=file): Firmen, Gruppen, Richtlinie und Keys aus einer YAML- oder JSON-Datei, ohne Portal.

Gelesen beim Start, bei SIGHUP (POSIX) und wenn sich die Datei ändert (Prüfung alle 2 s). Ein Fehler in der Datei
ändert nichts: der vorige Stand bleibt, das Log nennt nur Firma/Pfad und Fehlerklasse. Werte der Form `env:NAME`
(Keys, Codes) liest der Server aus der Umgebung, damit keine Keys in der Datei stehen müssen.
Format: server/README.md „Datei-Modus“, Beispiel server/beispiele/firmen.yaml.
"""

import contextlib
import json
import os
import signal
import sqlite3
import threading
from pathlib import Path

import yaml

from . import db, firmen, krypto, richtlinie, schluessel
from .config import Einstellungen
from .katalog import Ungueltig
from .krypto import Krypto
from .log import ereignis

_FIRMA_FELDER = {"slug", "name", "code", "keyMode", "autoApprove", "maxPending", "blocked", "policy", "groups", "keys"}


class DateiFehler(ValueError):
    pass


def _env(wert, wo: str) -> str:
    if not isinstance(wert, str):
        raise DateiFehler(f"{wo}: Text erwartet")
    if wert.startswith("env:"):
        name = wert.removeprefix("env:")
        if name not in os.environ:
            raise DateiFehler(f"{wo}: Umgebungsvariable {name} fehlt")
        return os.environ[name]
    return wert


def lesen(pfad: Path) -> list[dict]:
    text = pfad.read_text(encoding="utf-8")
    daten = json.loads(text) if pfad.suffix.lower() == ".json" else yaml.safe_load(text)
    if not isinstance(daten, dict) or not isinstance(daten.get("tenants"), list):
        raise DateiFehler("oberste Ebene braucht die Liste tenants")
    return daten["tenants"]


def _richtlinie(con: sqlite3.Connection, fid: str, gid: str, eintraege, wo: str) -> None:
    if eintraege is None:
        return
    if not isinstance(eintraege, dict):
        raise DateiFehler(f"{wo}.policy: Objekt erwartet")
    for pfad, e in eintraege.items():
        if not isinstance(e, dict) or set(e) - {"level", "value"}:
            raise DateiFehler(f"{wo}.policy.{pfad}: {{level, value}} erwartet")
        try:
            richtlinie.setzen(con, fid, gid, str(pfad), str(e.get("level", "")), e.get("value"))
        except Ungueltig as ex:
            raise DateiFehler(f"{wo}.policy.{pfad}: {ex}") from ex


def _firma(con: sqlite3.Connection, kry: Krypto, t: dict, jetzt: int) -> str:
    if not isinstance(t, dict) or set(t) - _FIRMA_FELDER:
        raise DateiFehler(f"tenant: unbekannte Felder {sorted(set(t) - _FIRMA_FELDER) if isinstance(t, dict) else ''}")
    slug = str(t.get("slug", ""))
    wo = f"tenants[{slug}]"
    row = con.execute("SELECT id, quelle FROM firma WHERE slug = ?", (slug,)).fetchone()
    try:
        if row is None:
            fid, _ = firmen.anlegen(con, kry, slug, str(t.get("name", slug)), jetzt, quelle="file")
        elif row["quelle"] != "file":
            raise DateiFehler(f"{wo}: Firma stammt aus dem Portal")
        else:
            fid = row["id"]
        name = firmen.name_pruefen(str(t.get("name", slug)))
    except firmen.FirmenFehler as ex:
        raise DateiFehler(f"{wo}: {ex}") from ex
    if not isinstance(t.get("keys", {}) or {}, dict) or not isinstance(t.get("groups", []) or [], list):
        raise DateiFehler(f"{wo}: keys muss ein Objekt, groups eine Liste sein")
    if not isinstance(t.get("maxPending", 50), int) or isinstance(t.get("maxPending", 50), bool):
        raise DateiFehler(f"{wo}.maxPending: Zahl erwartet")
    modus = t.get("keyMode", "proxy")
    if modus not in ("proxy", "distribute"):
        raise DateiFehler(f"{wo}.keyMode: proxy oder distribute")
    con.execute(
        "UPDATE firma SET name = ?, key_modus = ?, auto_freigabe = ?, max_wartend = ?, gesperrt = ? WHERE id = ?",
        (name, modus, int(bool(t.get("autoApprove", False))), int(t.get("maxPending", 50)), int(bool(t.get("blocked"))), fid),
    )
    if "code" in t:
        code = krypto.code_normalisieren(_env(t["code"], f"{wo}.code"))
        if code is None:
            raise DateiFehler(f"{wo}.code: kein gültiger Firmencode")
        firmen.code_setzen(con, kry, fid, None, jetzt, code)
    con.execute("DELETE FROM richtlinie WHERE firma_id = ?", (fid,))
    _richtlinie(con, fid, "", t.get("policy"), wo)
    _gruppen(con, kry, fid, t.get("groups") or [], wo, jetzt)
    con.execute("DELETE FROM schluessel WHERE firma_id = ?", (fid,))
    for name_, k in (t.get("keys") or {}).items():
        if not isinstance(k, dict) or set(k) - {"provider", "baseUrl", "key"}:
            raise DateiFehler(f"{wo}.keys.{name_}: {{provider, baseUrl, key}} erwartet")
        try:
            schluessel.speichern(con, kry, fid, str(name_), str(k.get("provider", name_)), str(k.get("baseUrl", "")),
                                 _env(k.get("key"), f"{wo}.keys.{name_}"), jetzt)
        except schluessel.SchluesselFehler as ex:
            raise DateiFehler(f"{wo}.keys.{name_}: {ex}") from ex
    return fid


def _gruppen(con: sqlite3.Connection, kry: Krypto, fid: str, gruppen: list, wo: str, jetzt: int) -> None:
    namen = []
    for g in gruppen:
        if not isinstance(g, dict) or set(g) - {"name", "code", "policy"}:
            raise DateiFehler(f"{wo}.groups: {{name, code, policy}} erwartet")
        name = firmen.name_pruefen(str(g.get("name", "")), 60)
        namen.append(name)
        row = con.execute("SELECT id FROM gruppe WHERE firma_id = ? AND name = ?", (fid, name)).fetchone()
        gid = row["id"] if row else krypto.kurz_id("g_")
        if row is None:
            con.execute("INSERT INTO gruppe (id, firma_id, name, angelegt_ms) VALUES (?, ?, ?, ?)", (gid, fid, name, jetzt))
        _richtlinie(con, fid, gid, g.get("policy"), f"{wo}.groups[{name}]")
        if "code" in g:
            code = krypto.code_normalisieren(_env(g["code"], f"{wo}.groups[{name}].code"))
            if code is None:
                raise DateiFehler(f"{wo}.groups[{name}].code: kein gültiger Firmencode")
            firmen.code_setzen(con, kry, fid, gid, jetzt, code)
        else:
            firmen.code_entfernen(con, fid, gid)
    platz = ",".join("?" * len(namen)) or "''"
    con.execute(f"DELETE FROM gruppe WHERE firma_id = ? AND name NOT IN ({platz})", (fid, *namen))  # noqa: S608


def anwenden(einst: Einstellungen, kry: Krypto, eintraege: list[dict]) -> int:
    """Alles in einer Transaktion; Fehler → nichts geändert. Firmen aus der Datei, die fehlen, werden gesperrt."""
    con = db.verbinden(einst.db_pfad)
    try:
        con.execute("BEGIN IMMEDIATE")
        try:
            ids = [_firma(con, kry, t, einst.uhr()) for t in eintraege]
            platz = ",".join("?" * len(ids)) or "''"
            con.execute(f"UPDATE firma SET gesperrt = 1 WHERE quelle = 'file' AND id NOT IN ({platz})", ids)  # noqa: S608
            con.execute("COMMIT")
        except BaseException:
            con.execute("ROLLBACK")
            raise
    finally:
        con.close()
    return len(ids)


class Beobachter:
    def __init__(self, einst: Einstellungen, kry: Krypto, intervall_s: float = 2.0):
        self.einst, self.kry, self.intervall_s = einst, kry, intervall_s
        self._mtime: float | None = None
        self._halt = threading.Event()
        self._faden: threading.Thread | None = None

    def laden(self, streng: bool = False) -> bool:
        """True = übernommen. streng (Start): Fehler bricht ab, sonst bleibt der vorige Stand."""
        pfad = self.einst.konfig_datei
        try:
            self._mtime = pfad.stat().st_mtime
            anzahl = anwenden(self.einst, self.kry, lesen(pfad))
        except Exception as ex:  # noqa: BLE001 — auch sqlite3/Typfehler aus der Datei: Stand bleibt, Dienst läuft weiter
            ereignis("datei fehler", fehler=type(ex).__name__)
            if streng:
                raise DateiFehler(f"{pfad}: {ex}") from ex
            return False
        ereignis("datei geladen", status=anzahl)
        return True

    def pruefen(self) -> bool:
        try:
            mtime = self.einst.konfig_datei.stat().st_mtime
        except OSError:
            return False
        return mtime != self._mtime and self.laden()

    def _schleife(self) -> None:
        while not self._halt.wait(self.intervall_s):
            try:
                self.pruefen()
            except Exception as ex:  # noqa: BLE001 — der Beobachter darf nie sterben
                ereignis("datei fehler", fehler=type(ex).__name__)

    def starten(self) -> None:
        if hasattr(signal, "SIGHUP"):
            with contextlib.suppress(ValueError):  # nicht im Haupt-Thread (z. B. Tests)
                signal.signal(signal.SIGHUP, lambda *_: self.laden())
        self._faden = threading.Thread(target=self._schleife, name="wl-datei", daemon=True)
        self._faden.start()

    def stoppen(self) -> None:
        self._halt.set()
