"""Portal-Seiten „Nutzung“ (Tabelle je Firma/Gruppe/Gerät und Monat, CSV-Export) und „Budget“ (Monatsdeckel,
Warnschwelle, Preistabelle) für den Proxy (W2-SRV2). Ansehen: alle Admins der Firma; Ändern: nur owner.
"""

import csv
import io
import sqlite3

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse, Response

from .. import budget, firmen, nutzung
from ..abhaengig import jetzt, verbindung
from .basis import Wer, csrf_pruefen, in_firma, owner, seite, zurueck

router = APIRouter()
_FORMEL = ("=", "+", "-", "@", "\t", "\r")


def _monat(request: Request, gewuenscht: str) -> str:
    return gewuenscht if nutzung.gueltiger_monat(gewuenscht) else nutzung.monat(jetzt(request))


@router.get("/nutzung", response_class=HTMLResponse)
def nutzung_seite(request: Request, monat: str = "", wer: Wer = Depends(in_firma),
                  con: sqlite3.Connection = Depends(verbindung)):
    m = _monat(request, monat)
    return seite(request, "nutzung.html", {
        "aktiv": "nutzung", "monat": m, "monate": nutzung.monate(con, wer.firma_id, nutzung.monat(jetzt(request))),
        "daten": nutzung.tabelle(con, wer.firma_id, m), "klassen": nutzung.KLASSEN, "betrag": nutzung.betrag_zeigen,
    }, wer=wer, con=con)


def _zelle(wert) -> str:
    """CSV-Formel-Injektion verhindern: Text, der mit =, +, -, @ beginnt, bekommt ein Hochkomma."""
    text = str(wert)
    return "'" + text if text.startswith(_FORMEL) else text


@router.get("/nutzung.csv")
def nutzung_csv(request: Request, monat: str = "", wer: Wer = Depends(in_firma),
                con: sqlite3.Connection = Depends(verbindung)):
    m = _monat(request, monat)
    daten = nutzung.tabelle(con, wer.firma_id, m)
    puffer = io.StringIO()
    w = csv.writer(puffer, lineterminator="\r\n")
    w.writerow(["month", "level", "id", "name", "group", "requests", "audio_minutes", "tokens_in", "tokens_out",
                "cost", *[f"errors_{k}" for k in nutzung.KLASSEN]])

    def zeile(ebene: str, ziel: str, name: str, gruppe: str, z: dict) -> None:
        w.writerow([_zelle(x) for x in (m, ebene, ziel, name, gruppe, z["anfragen"], round(z["audio_ms"] / 60_000, 2),
                    z["tokens_ein"], z["tokens_aus"], nutzung.betrag_zeigen(z["kosten_mikro"]) or "0",
                    *[z["fehler"].get(k, 0) for k in nutzung.KLASSEN])])

    zeile("tenant", wer.firma_id, "", "", daten["firma"])
    for g in daten["gruppen"]:
        zeile("group", g["gruppe_id"], g["name"], "", g)
    for d in daten["geraete"]:
        zeile("device", d["geraet_id"], d["name"], d["gruppe"], d)
    return Response("﻿" + puffer.getvalue(), media_type="text/csv; charset=utf-8",
                    headers={"Content-Disposition": f'attachment; filename="nutzung-{m}.csv"'})


@router.get("/budget", response_class=HTMLResponse)
def budget_seite(request: Request, wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    m = nutzung.monat(jetzt(request))
    return seite(request, "budget.html", {
        "aktiv": "budget", "monat": m, "stand": budget.stand(con, wer.firma_id, m), "preise": budget.preise(con, wer.firma_id),
        "gruppen": con.execute("SELECT id, name FROM gruppe WHERE firma_id = ? ORDER BY name", (wer.firma_id,)).fetchall(),
        "geraete": con.execute("SELECT id, name FROM geraet WHERE firma_id = ? AND status = 'approved' ORDER BY name",
                               (wer.firma_id,)).fetchall(),
        "betrag": nutzung.betrag_zeigen,
        # Review N-14: unter Auto-Freigabe holt sich ein Gerät per Verlassen und Neu-Verbinden ein frisches Budget.
        "auto_freigabe": bool(con.execute("SELECT auto_freigabe FROM firma WHERE id = ?", (wer.firma_id,)).fetchone()[0]),
    }, wer=wer, con=con)


def _ganz(text: str) -> int | None:
    text = text.strip()
    if not text:
        return None
    if not text.isdigit() or len(text) > 15:
        raise ValueError("zahl")
    return int(text)


@router.post("/budget")
def budget_speichern(request: Request, csrf: str = Form(""), ebene: str = Form(""), gruppe: str = Form(""),
                     geraet: str = Form(""), max_anfragen: str = Form(""), max_minuten: str = Form(""),
                     max_tokens: str = Form(""), max_kosten: str = Form(""), warn_prozent: str = Form("80"),
                     wer: Wer = Depends(owner), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    ziel = {"gruppe": gruppe, "geraet": geraet}.get(ebene, "")
    try:
        grenzen = {"max_anfragen": _ganz(max_anfragen), "max_audio_min": _ganz(max_minuten),
                   "max_tokens": _ganz(max_tokens), "max_kosten_mikro": nutzung.betrag_lesen(max_kosten)}
        budget.setzen(con, wer.firma_id, ebene, ziel, grenzen, _ganz(warn_prozent) or 80)
    except budget.BudgetFehler as ex:
        return zurueck("/budget", hinweis=str(ex).removeprefix("h."))
    except ValueError:
        return zurueck("/budget", hinweis="wert")
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "budget_gesetzt", f"{ebene}:{ziel}", jetzt(request))
    return zurueck("/budget", ok="gespeichert")


@router.post("/budget/entfernen")
def budget_entfernen(request: Request, csrf: str = Form(""), ebene: str = Form(""), ziel: str = Form(""),
                     wer: Wer = Depends(owner), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    if budget.loeschen(con, wer.firma_id, ebene, ziel):
        firmen.protokoll(con, wer.firma_id, wer.benutzer, "budget_entfernt", f"{ebene}:{ziel}", jetzt(request))
    return zurueck("/budget", ok="gespeichert")


@router.post("/budget/preis")
def preis_speichern(request: Request, csrf: str = Form(""), modell: str = Form(""), minute: str = Form(""),
                    mio_ein: str = Form(""), mio_aus: str = Form(""), wer: Wer = Depends(owner),
                    con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    try:
        budget.preis_setzen(con, wer.firma_id, modell, nutzung.betrag_lesen(minute), nutzung.betrag_lesen(mio_ein),
                            nutzung.betrag_lesen(mio_aus))
    except budget.BudgetFehler as ex:
        return zurueck("/budget", hinweis=str(ex).removeprefix("h."))
    except ValueError:
        return zurueck("/budget", hinweis="wert")
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "preis_gesetzt", modell.strip()[:60], jetzt(request))
    return zurueck("/budget", ok="gespeichert")


@router.post("/budget/preis/entfernen")
def preis_entfernen(request: Request, csrf: str = Form(""), modell: str = Form(""), wer: Wer = Depends(owner),
                    con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    if budget.preis_loeschen(con, wer.firma_id, modell):
        firmen.protokoll(con, wer.firma_id, wer.benutzer, "preis_entfernt", modell[:60], jetzt(request))
    return zurueck("/budget", ok="gespeichert")
