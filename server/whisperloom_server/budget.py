"""Budgets je Firma, Gruppe und Gerät (Monatsdeckel) und Preistabelle für die Kosten-Schätzung (W2-SRV2).

Ein Budget hat bis zu vier Grenzen (Anfragen, Audio-Minuten, Tokens ein+aus, Kosten); leer = keine Grenze. Ist eine
Grenze einer Ebene erreicht (≥ 100 %), lehnt der Proxy ab (`403 budget_exhausted`). Ab der Warnschwelle (Standard 80 %)
zeigt das Portal eine Warnung. Weiche Grenze: gleichzeitige Anfragen können sie knapp überschreiten.
"""

import re
import sqlite3

from . import nutzung

EBENEN = ("geraet", "gruppe", "firma")  # Prüfreihenfolge
GRENZEN = ("max_anfragen", "max_audio_min", "max_tokens", "max_kosten_mikro")
_MODELL = re.compile(r"^[\x21-\x7e]{1,100}$")


class BudgetFehler(ValueError):
    """Text = Hinweis-Schlüssel in texte/*.json."""


def setzen(con: sqlite3.Connection, firma_id: str, ebene: str, ziel_id: str, grenzen: dict[str, int | None],
           warn_prozent: int) -> None:
    if ebene not in EBENEN:
        raise BudgetFehler("h.wert")
    if ebene == "firma":
        ziel_id = ""
    else:
        tabelle = "gruppe" if ebene == "gruppe" else "geraet"
        if con.execute(f"SELECT 1 FROM {tabelle} WHERE id = ? AND firma_id = ?",  # noqa: S608 (feste Tabelle)
                       (ziel_id, firma_id)).fetchone() is None:
            raise BudgetFehler("h.wert")
    werte = [grenzen.get(g) for g in GRENZEN]
    if all(w is None for w in werte):
        raise BudgetFehler("h.budget_leer")
    if any(w is not None and (w < 0 or w > 10**15) for w in werte) or not 1 <= warn_prozent <= 100:
        raise BudgetFehler("h.wert")
    con.execute(
        "INSERT INTO budget (firma_id, ebene, ziel_id, max_anfragen, max_audio_min, max_tokens, max_kosten_mikro, "
        "warn_prozent) VALUES (?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT (firma_id, ebene, ziel_id) DO UPDATE SET "
        "max_anfragen = excluded.max_anfragen, max_audio_min = excluded.max_audio_min, max_tokens = excluded.max_tokens, "
        "max_kosten_mikro = excluded.max_kosten_mikro, warn_prozent = excluded.warn_prozent",
        (firma_id, ebene, ziel_id, *werte, warn_prozent),
    )


def loeschen(con: sqlite3.Connection, firma_id: str, ebene: str, ziel_id: str) -> bool:
    return con.execute("DELETE FROM budget WHERE firma_id = ? AND ebene = ? AND ziel_id = ?",
                       (firma_id, ebene, ziel_id)).rowcount == 1


def anteile(summe: dict, b: sqlite3.Row) -> dict[str, float]:
    """Grenze → verbrauchter Anteil in Prozent (nur gesetzte Grenzen)."""
    verbrauch = {
        "max_anfragen": summe["anfragen"],
        "max_audio_min": summe["audio_ms"] / 60_000,
        "max_tokens": summe["tokens_ein"] + summe["tokens_aus"],
        "max_kosten_mikro": summe["kosten_mikro"],
    }
    ergebnis = {}
    for g in GRENZEN:
        if b[g] is not None:
            ergebnis[g] = 100.0 if b[g] == 0 else 100.0 * verbrauch[g] / b[g]
    return ergebnis


def _budget(con: sqlite3.Connection, firma_id: str, ebene: str, ziel_id: str) -> sqlite3.Row | None:
    return con.execute("SELECT * FROM budget WHERE firma_id = ? AND ebene = ? AND ziel_id = ?",
                       (firma_id, ebene, ziel_id)).fetchone()


def ebenen(geraet: sqlite3.Row) -> list[tuple[str, str]]:
    """Budget-Ebenen eines Geräts in Prüfreihenfolge: (ebene, ziel_id)."""
    gruppe = [("gruppe", geraet["gruppe_id"])] if geraet["gruppe_id"] else []
    return [("geraet", geraet["id"]), *gruppe, ("firma", "")]


def erschoepft(con: sqlite3.Connection, geraet: sqlite3.Row, zeit: int,
               offen: dict[tuple[str, str, str], tuple[int, int]] | None = None) -> str | None:
    """Ebene, deren Budget im laufenden Monat erreicht ist (geraet, gruppe, firma), sonst None.
    `offen`: laufende, noch nicht gezählte Anfragen je (firma_id, ebene, ziel) als (Anzahl, Audio-ms) – Reservierung
    (Prüfrunde 1 Befund 3): gleichzeitige Anfragen verhalten sich wie nacheinander gestellte. Tokens und Kosten
    stehen erst nach der Antwort fest; dort kann eine Welle gleichzeitiger Anfragen den Deckel überschreiten."""
    m = nutzung.monat(zeit)
    for ebene, ziel in ebenen(geraet):
        b = _budget(con, geraet["firma_id"], ebene, ziel)
        if b is None:
            continue
        s = nutzung.summe(con, geraet["firma_id"], m, ebene, ziel)
        n, ms = (offen or {}).get((geraet["firma_id"], ebene, ziel), (0, 0))
        s = {**s, "anfragen": s["anfragen"] + n, "audio_ms": s["audio_ms"] + ms}
        if any(p >= 100 for p in anteile(s, b).values()):
            return ebene
    return None


def stand(con: sqlite3.Connection, firma_id: str, monat_: str) -> list[dict]:
    """Alle Budgets der Firma mit Verbrauch, höchstem Anteil und Zustand ok | warnung | erreicht (für das Portal)."""
    namen = {("gruppe", r["id"]): r["name"] for r in con.execute("SELECT id, name FROM gruppe WHERE firma_id = ?", (firma_id,))}
    namen |= {("geraet", r["id"]): r["name"] for r in con.execute("SELECT id, name FROM geraet WHERE firma_id = ?", (firma_id,))}
    liste = []
    for b in con.execute("SELECT * FROM budget WHERE firma_id = ? ORDER BY CASE ebene WHEN 'firma' THEN 0 WHEN 'gruppe' "
                         "THEN 1 ELSE 2 END, ziel_id", (firma_id,)):
        if b["ebene"] != "firma" and (b["ebene"], b["ziel_id"]) not in namen:
            continue  # Gruppe bzw. Gerät gelöscht
        s = nutzung.summe(con, firma_id, monat_, b["ebene"], b["ziel_id"])
        a = anteile(s, b)
        hoechst = max(a.values(), default=0.0)
        zustand = "erreicht" if hoechst >= 100 else ("warnung" if hoechst >= b["warn_prozent"] else "ok")
        liste.append({"b": b, "name": namen.get((b["ebene"], b["ziel_id"]), ""), "summe": s, "anteile": a,
                      "prozent": min(int(hoechst), 999), "zustand": zustand})
    return liste


# --- Preistabelle ---


def preis_setzen(con: sqlite3.Connection, firma_id: str, modell: str, minute: int | None, mio_ein: int | None,
                 mio_aus: int | None) -> None:
    modell = modell.strip()
    if not _MODELL.match(modell):
        raise BudgetFehler("h.wert")
    if minute is None and mio_ein is None and mio_aus is None:
        raise BudgetFehler("h.budget_leer")
    con.execute(
        "INSERT INTO preis (firma_id, modell, minute_mikro, mio_ein_mikro, mio_aus_mikro) VALUES (?, ?, ?, ?, ?) "
        "ON CONFLICT (firma_id, modell) DO UPDATE SET minute_mikro = excluded.minute_mikro, "
        "mio_ein_mikro = excluded.mio_ein_mikro, mio_aus_mikro = excluded.mio_aus_mikro",
        (firma_id, modell, minute or 0, mio_ein or 0, mio_aus or 0),
    )


def preis_loeschen(con: sqlite3.Connection, firma_id: str, modell: str) -> bool:
    return con.execute("DELETE FROM preis WHERE firma_id = ? AND modell = ?", (firma_id, modell)).rowcount == 1


def preise(con: sqlite3.Connection, firma_id: str) -> list[sqlite3.Row]:
    return con.execute("SELECT * FROM preis WHERE firma_id = ? ORDER BY modell", (firma_id,)).fetchall()
