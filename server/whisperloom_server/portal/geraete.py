"""Übersicht, Geräte (Freigabe, Sperre, Name, Gruppe, Löschen) und Gruppen samt Gruppen-Code."""

import sqlite3

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse

from .. import firmen, krypto
from ..abhaengig import jetzt, verbindung
from ..log import ereignis
from .basis import Wer, csrf_pruefen, fehlerseite, in_firma, seite, zurueck

router = APIRouter()


def _geraete(con: sqlite3.Connection, fid: str) -> list[sqlite3.Row]:
    return con.execute(
        "SELECT g.*, gr.name AS gruppe_name FROM geraet g LEFT JOIN gruppe gr ON gr.id = g.gruppe_id "
        "WHERE g.firma_id = ? ORDER BY g.status = 'pending' DESC, g.name",
        (fid,),
    ).fetchall()


def _gruppen(con: sqlite3.Connection, fid: str) -> list[sqlite3.Row]:
    return con.execute(
        "SELECT gr.*, (SELECT COUNT(*) FROM geraet g WHERE g.gruppe_id = gr.id) AS anzahl "
        "FROM gruppe gr WHERE gr.firma_id = ? ORDER BY gr.name",
        (fid,),
    ).fetchall()


@router.get("/", response_class=HTMLResponse)
def uebersicht(request: Request, wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    fid = wer.firma_id
    geraete = _geraete(con, fid)
    firma = con.execute("SELECT * FROM firma WHERE id = ?", (fid,)).fetchone()
    return seite(request, "uebersicht.html", {
        "aktiv": "uebersicht",
        "wartend": [g for g in geraete if g["status"] == "pending"],
        "anzahl": {s: sum(1 for g in geraete if g["status"] == s) for s in ("approved", "pending", "blocked")},
        "code": firmen.code_lesen(con, request.app.state.krypto, fid, None),
        "firma": firma,
        "schluessel_anzahl": con.execute("SELECT COUNT(*) FROM schluessel WHERE firma_id = ?", (fid,)).fetchone()[0],
        "gruppen_anzahl": con.execute("SELECT COUNT(*) FROM gruppe WHERE firma_id = ?", (fid,)).fetchone()[0],
    }, wer=wer, con=con)


@router.get("/geraete", response_class=HTMLResponse)
def geraete(request: Request, wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    return seite(request, "geraete.html", {
        "aktiv": "geraete", "geraete": _geraete(con, wer.firma_id), "gruppen": _gruppen(con, wer.firma_id),
    }, wer=wer, con=con)


_AKTIONEN = {
    "freigeben": ("UPDATE geraet SET status = 'approved' WHERE id = ? AND firma_id = ? AND status != 'approved'", "freigegeben"),
    "sperren": ("UPDATE geraet SET status = 'blocked' WHERE id = ? AND firma_id = ?", "gesperrt"),
    "entsperren": ("UPDATE geraet SET status = 'approved' WHERE id = ? AND firma_id = ? AND status = 'blocked'", "entsperrt"),
    "loeschen": ("DELETE FROM geraet WHERE id = ? AND firma_id = ?", "geloescht"),
}


@router.post("/geraete/{gid}/{aktion}")
def geraet_aktion(gid: str, aktion: str, request: Request, csrf: str = Form(""), name: str = Form(""),
                  gruppe: str = Form(""), wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    fid = wer.firma_id
    if aktion in _AKTIONEN:
        sql, ergebnis = _AKTIONEN[aktion]
        ok = con.execute(sql, (gid, fid)).rowcount == 1
    elif aktion == "umbenennen":
        try:
            neu = firmen.name_pruefen(name, 64)
        except firmen.FirmenFehler:
            return zurueck("/geraete", hinweis="name")
        ok = con.execute("UPDATE geraet SET name = ? WHERE id = ? AND firma_id = ?", (neu, gid, fid)).rowcount == 1
        ergebnis = "umbenannt"
    elif aktion == "gruppe":
        if gruppe and con.execute("SELECT 1 FROM gruppe WHERE id = ? AND firma_id = ?", (gruppe, fid)).fetchone() is None:
            return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
        sql = "UPDATE geraet SET gruppe_id = ? WHERE id = ? AND firma_id = ?"
        ok = con.execute(sql, (gruppe or None, gid, fid)).rowcount == 1
        ergebnis = "gruppe"
    else:
        ok = False
    if not ok:
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    firmen.protokoll(con, fid, wer.benutzer, "geraet_" + aktion, gid, jetzt(request))
    ereignis("geraet " + aktion, geraet=gid, firma=fid)
    if aktion == "sperren" and con.execute("SELECT auto_freigabe FROM firma WHERE id = ?", (fid,)).fetchone()[0]:
        # Unter Auto-Freigabe meldet sich ein Gerät mit bekanntem Firmencode sofort neu an (wieder freigegeben).
        ergebnis = "gesperrt_auto"
    return zurueck("/geraete", ok=ergebnis)


@router.get("/gruppen", response_class=HTMLResponse)
def gruppen(request: Request, wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    kry = request.app.state.krypto
    zeilen = [{"g": g, "code": firmen.code_lesen(con, kry, wer.firma_id, g["id"])} for g in _gruppen(con, wer.firma_id)]
    return seite(request, "gruppen.html", {"aktiv": "gruppen", "gruppen": zeilen}, wer=wer, con=con)


@router.post("/gruppen")
def gruppe_neu(request: Request, csrf: str = Form(""), name: str = Form(""), wer: Wer = Depends(in_firma),
               con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    try:
        neu = firmen.name_pruefen(name, 60)
    except firmen.FirmenFehler:
        return zurueck("/gruppen", hinweis="name")
    if con.execute("SELECT 1 FROM gruppe WHERE firma_id = ? AND name = ?", (wer.firma_id, neu)).fetchone():
        return zurueck("/gruppen", hinweis="name_vergeben")
    gid = krypto.kurz_id("g_")
    con.execute(
        "INSERT INTO gruppe (id, firma_id, name, angelegt_ms) VALUES (?, ?, ?, ?)", (gid, wer.firma_id, neu, jetzt(request))
    )
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "gruppe_neu", gid, jetzt(request))
    return zurueck("/gruppen", ok="gruppe_neu")


@router.post("/gruppen/{gid}/{aktion}")
def gruppe_aktion(gid: str, aktion: str, request: Request, csrf: str = Form(""), name: str = Form(""),
                  wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    fid = wer.firma_id
    if con.execute("SELECT 1 FROM gruppe WHERE id = ? AND firma_id = ?", (gid, fid)).fetchone() is None:
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    zeit = jetzt(request)
    if aktion == "umbenennen":
        try:
            neu = firmen.name_pruefen(name, 60)
        except firmen.FirmenFehler:
            return zurueck("/gruppen", hinweis="name")
        if con.execute("SELECT 1 FROM gruppe WHERE firma_id = ? AND name = ? AND id != ?", (fid, neu, gid)).fetchone():
            return zurueck("/gruppen", hinweis="name_vergeben")
        con.execute("UPDATE gruppe SET name = ? WHERE id = ? AND firma_id = ?", (neu, gid, fid))
        ergebnis = "umbenannt"
    elif aktion == "loeschen":
        con.execute("DELETE FROM richtlinie WHERE firma_id = ? AND gruppe_id = ?", (fid, gid))
        con.execute("DELETE FROM gruppe WHERE id = ? AND firma_id = ?", (gid, fid))
        ergebnis = "geloescht"
    elif aktion == "code":
        firmen.code_setzen(con, request.app.state.krypto, fid, gid, zeit)
        ergebnis = "code_neu"
    elif aktion == "code_entfernen":
        firmen.code_entfernen(con, fid, gid)
        ergebnis = "code_entfernt"
    else:
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    firmen.protokoll(con, fid, wer.benutzer, "gruppe_" + aktion, gid, zeit)
    return zurueck("/gruppen", ok=ergebnis)
