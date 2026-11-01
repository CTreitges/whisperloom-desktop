"""Richtlinie mit drei Stufen je Pfad (Fest/Vorgabe/Frei) und Auflösung Firma → Gruppe.

Gespeichert je (firma_id, gruppe_id, pfad): Firma = gruppe_id ''. Auf Firmenebene heißt „Frei“: keine Zeile.
Eine Gruppe überschreibt die Firma je Pfad, auch mit „Frei“ (Zeile mit stufe 'free'); ohne Zeile erbt sie.
Ausgabe für die Client-API nach docs/server-api.md §3.3 (`settings` + `policy.levels`).
"""

import json
import sqlite3

from .katalog import Ungueltig, eintrag, stufen_fuer, wert_pruefen

STUFEN = ("fixed", "default", "free")


def setzen(con: sqlite3.Connection, firma_id: str, gruppe_id: str, pfad: str, stufe: str, roh) -> None:
    e = eintrag(pfad)
    if stufe not in STUFEN:
        raise Ungueltig("h.wert")
    if stufe == "fixed" and e.kuerzel:
        raise Ungueltig("h.kuerzel_fest")
    if stufe not in stufen_fuer(e):
        raise Ungueltig("h.wert")
    if stufe == "free" and not gruppe_id:
        loeschen(con, firma_id, gruppe_id, pfad)
        return
    wert = None if stufe == "free" else wert_pruefen(e, roh)
    con.execute(
        "INSERT INTO richtlinie (firma_id, gruppe_id, pfad, stufe, wert) VALUES (?, ?, ?, ?, ?) "
        "ON CONFLICT (firma_id, gruppe_id, pfad) DO UPDATE SET stufe = excluded.stufe, wert = excluded.wert",
        (firma_id, gruppe_id, pfad, stufe, json.dumps(wert)),
    )


def loeschen(con: sqlite3.Connection, firma_id: str, gruppe_id: str, pfad: str) -> None:
    con.execute("DELETE FROM richtlinie WHERE firma_id = ? AND gruppe_id = ? AND pfad = ?", (firma_id, gruppe_id, pfad))


def eintraege(con: sqlite3.Connection, firma_id: str, gruppe_id: str = "") -> dict[str, tuple[str, object]]:
    rows = con.execute(
        "SELECT pfad, stufe, wert FROM richtlinie WHERE firma_id = ? AND gruppe_id = ?", (firma_id, gruppe_id)
    ).fetchall()
    return {r["pfad"]: (r["stufe"], json.loads(r["wert"])) for r in rows}


def aufloesen(con: sqlite3.Connection, firma_id: str, gruppe_id: str | None) -> dict[str, tuple[str, object]]:
    """Wirksame Stufen für ein Gerät: Firma, darüber die Gruppe; „Frei“ fällt heraus."""
    wirksam = eintraege(con, firma_id)
    if gruppe_id:
        wirksam.update(eintraege(con, firma_id, gruppe_id))
    return {p: sw for p, sw in wirksam.items() if sw[0] != "free"}


def _einsetzen(ziel: dict, pfad: str, wert) -> None:
    *abschnitte, blatt = pfad.split(".")
    for a in abschnitte:
        ziel = ziel.setdefault(a, {})
    ziel[blatt] = wert


def als_konfig(wirksam: dict[str, tuple[str, object]]) -> tuple[dict, dict]:
    """(settings, policy) nach docs/server-api.md §3.3."""
    settings: dict = {"schemaVersion": 1}
    levels: dict[str, str] = {}
    policy: dict = {}
    for pfad in sorted(wirksam):
        stufe, wert = wirksam[pfad]
        if pfad.startswith("policy."):
            if stufe == "fixed":
                policy[pfad.removeprefix("policy.")] = wert
            continue
        if stufe == "fixed" and eintrag(pfad).kuerzel:
            stufe = "default"  # Altbestand absichern: Tastenkürzel sind nie Fest
        _einsetzen(settings, pfad, wert)
        levels[pfad] = stufe
    if wirksam.get("history.enabled", ("", None))[0] == "fixed":
        policy["historyEnabled"] = wirksam["history.enabled"][1]
    return settings, {"levels": levels, **policy}
