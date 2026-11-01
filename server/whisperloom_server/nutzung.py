"""Nutzungsstatistik des Proxys je Gerät, Gruppe, Firma und Monat (W2-SRV2). Nur Zähler: Anfragen, Audio-Dauer,
Tokens (aus `usage` der Anbieter-Antwort), geschätzte Kosten und Fehler je Klasse. Kein Text, kein Audio, keine Keys.

Monate in UTC (`YYYY-MM`). Die Gruppe wird beim Zählen festgehalten: Wechselt ein Gerät die Gruppe, zählt der Rest des
Monats für die neue Gruppe. Jede Abfrage filtert nach firma_id (Mandanten-Trennung).
"""

import sqlite3
import time
from decimal import Decimal, InvalidOperation

KLASSEN = ("upstream_error", "upstream_timeout", "budget", "invalid_request")
MIKRO = 1_000_000
_SUMMEN = ("COALESCE(SUM(anfragen), 0) AS anfragen, COALESCE(SUM(audio_ms), 0) AS audio_ms, "
           "COALESCE(SUM(tokens_ein), 0) AS tokens_ein, COALESCE(SUM(tokens_aus), 0) AS tokens_aus, "
           "COALESCE(SUM(kosten_mikro), 0) AS kosten_mikro")


def monat(ms: int) -> str:
    return time.strftime("%Y-%m", time.gmtime(ms / 1000))


def gueltiger_monat(text: str) -> bool:
    return len(text) == 7 and text[4] == "-" and text[:4].isdigit() and text[5:].isdigit() and 1 <= int(text[5:]) <= 12


def betrag_lesen(text: str) -> int | None:
    """„0,006“ / „12.5“ → Millionstel (ganzzahlig); leer → None. Negativ, unlesbar oder > 10^9 → ValueError."""
    text = text.strip().replace(",", ".")
    if not text:
        return None
    try:
        wert = Decimal(text)
    except InvalidOperation as e:
        raise ValueError("betrag") from e
    if not wert.is_finite() or wert < 0 or wert > 10**9:
        raise ValueError("betrag")
    return int(wert * MIKRO)


def betrag_zeigen(mikro: int | None) -> str:
    if mikro is None:
        return ""
    return f"{Decimal(mikro) / MIKRO:.6f}".rstrip("0").rstrip(".")


def kosten_mikro(con: sqlite3.Connection, firma_id: str, modell: str, audio_ms: int, ein: int, aus: int) -> int:
    p = con.execute("SELECT * FROM preis WHERE firma_id = ? AND modell = ?", (firma_id, modell)).fetchone()
    if p is None:
        return 0
    return round(p["minute_mikro"] * audio_ms / 60_000 + p["mio_ein_mikro"] * ein / MIKRO + p["mio_aus_mikro"] * aus / MIKRO)


def erfassen(con: sqlite3.Connection, geraet: sqlite3.Row, zeit: int, modell: str, audio_ms: int = 0, ein: int = 0,
             aus: int = 0) -> None:
    """Eine weitergeleitete Anfrage zählen (auch eine, die beim Anbieter scheiterte: sie hat ihn erreicht)."""
    kosten = kosten_mikro(con, geraet["firma_id"], modell, audio_ms, ein, aus)
    con.execute(
        "INSERT INTO nutzung (firma_id, geraet_id, gruppe_id, monat, anfragen, audio_ms, tokens_ein, tokens_aus, "
        "kosten_mikro) VALUES (?, ?, ?, ?, 1, ?, ?, ?, ?) ON CONFLICT (firma_id, geraet_id, gruppe_id, monat) DO UPDATE "
        "SET anfragen = anfragen + 1, audio_ms = audio_ms + excluded.audio_ms, tokens_ein = tokens_ein + "
        "excluded.tokens_ein, tokens_aus = tokens_aus + excluded.tokens_aus, kosten_mikro = kosten_mikro + "
        "excluded.kosten_mikro",
        (geraet["firma_id"], geraet["id"], geraet["gruppe_id"] or "", monat(zeit), audio_ms, ein, aus, kosten),
    )


def fehler(con: sqlite3.Connection, geraet: sqlite3.Row, zeit: int, klasse: str) -> None:
    con.execute(
        "INSERT INTO nutzung_fehler (firma_id, geraet_id, gruppe_id, monat, klasse, anzahl) VALUES (?, ?, ?, ?, ?, 1) "
        "ON CONFLICT (firma_id, geraet_id, gruppe_id, monat, klasse) DO UPDATE SET anzahl = anzahl + 1",
        (geraet["firma_id"], geraet["id"], geraet["gruppe_id"] or "", monat(zeit), klasse),
    )


def summe(con: sqlite3.Connection, firma_id: str, monat_: str, ebene: str = "firma", ziel_id: str = "") -> dict:
    sql = f"SELECT {_SUMMEN} FROM nutzung WHERE firma_id = ? AND monat = ?"  # noqa: S608 (feste Spalten)
    werte: tuple = (firma_id, monat_)
    if ebene == "gruppe":
        sql, werte = sql + " AND gruppe_id = ?", (*werte, ziel_id)
    elif ebene == "geraet":
        sql, werte = sql + " AND geraet_id = ?", (*werte, ziel_id)
    return dict(con.execute(sql, werte).fetchone())


def _fehler_je(con: sqlite3.Connection, firma_id: str, monat_: str, spalte: str) -> dict[str, dict[str, int]]:
    ergebnis: dict[str, dict[str, int]] = {}
    for r in con.execute(
        f"SELECT {spalte} AS ziel, klasse, SUM(anzahl) AS n FROM nutzung_fehler WHERE firma_id = ? AND monat = ? "  # noqa: S608
        f"GROUP BY {spalte}, klasse",
        (firma_id, monat_),
    ):
        ergebnis.setdefault(r["ziel"], {})[r["klasse"]] = r["n"]
    return ergebnis


def tabelle(con: sqlite3.Connection, firma_id: str, monat_: str) -> dict:
    """Für Portal und CSV: Firma gesamt, je Gruppe, je Gerät – jeweils Zähler und Fehler je Klasse."""
    namen = {r["id"]: r["name"] for r in con.execute("SELECT id, name FROM gruppe WHERE firma_id = ?", (firma_id,))}
    geraete = {r["id"]: r for r in con.execute("SELECT id, name, gruppe_id FROM geraet WHERE firma_id = ?", (firma_id,))}
    fehler_geraet = _fehler_je(con, firma_id, monat_, "geraet_id")
    fehler_gruppe = _fehler_je(con, firma_id, monat_, "gruppe_id")
    fehler_firma: dict[str, int] = {}
    for je in fehler_geraet.values():
        for k, n in je.items():
            fehler_firma[k] = fehler_firma.get(k, 0) + n
    gruppen = []
    for r in con.execute(f"SELECT gruppe_id, {_SUMMEN} FROM nutzung WHERE firma_id = ? AND monat = ? "  # noqa: S608
                         "GROUP BY gruppe_id ORDER BY gruppe_id", (firma_id, monat_)):
        gruppen.append({**dict(r), "name": namen.get(r["gruppe_id"], ""), "fehler": fehler_gruppe.get(r["gruppe_id"], {})})
    zeilen = []
    for r in con.execute(f"SELECT geraet_id, {_SUMMEN} FROM nutzung WHERE firma_id = ? AND monat = ? "  # noqa: S608
                         "GROUP BY geraet_id ORDER BY geraet_id", (firma_id, monat_)):
        g = geraete.get(r["geraet_id"])
        zeilen.append({**dict(r), "name": g["name"] if g else "", "geloescht": g is None,
                       "gruppe": namen.get(g["gruppe_id"], "") if g else "", "fehler": fehler_geraet.get(r["geraet_id"], {})})
    return {"firma": {**summe(con, firma_id, monat_), "fehler": fehler_firma}, "gruppen": gruppen, "geraete": zeilen}


def monate(con: sqlite3.Connection, firma_id: str, aktuell: str) -> list[str]:
    vorhanden = {r[0] for r in con.execute("SELECT DISTINCT monat FROM nutzung WHERE firma_id = ?", (firma_id,))}
    vorhanden |= {r[0] for r in con.execute("SELECT DISTINCT monat FROM nutzung_fehler WHERE firma_id = ?", (firma_id,))}
    return sorted(vorhanden | {aktuell}, reverse=True)
