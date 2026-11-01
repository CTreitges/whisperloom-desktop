"""Richtlinie (Fest/Vorgabe/Frei je Einstellung, Firma oder Gruppe) und Keys der Firma."""

import sqlite3

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse

from .. import firmen, katalog, richtlinie, schluessel, ssrf
from ..abhaengig import jetzt, verbindung
from ..katalog import Ungueltig
from ..log import ereignis
from ..ratelimit import STUNDE
from .basis import Wer, csrf_pruefen, fehlerseite, in_firma, seite, zurueck

router = APIRouter()
ERBEN = "inherit"


def _gruppe(con: sqlite3.Connection, fid: str, gid: str) -> sqlite3.Row | None:
    return con.execute("SELECT * FROM gruppe WHERE id = ? AND firma_id = ?", (gid, fid)).fetchone() if gid else None


def _abschnitte(con: sqlite3.Connection, fid: str, gid: str, eingaben: dict | None = None) -> list[dict]:
    eigen = richtlinie.eintraege(con, fid, gid)
    firma = richtlinie.eintraege(con, fid) if gid else {}
    abschnitte = []
    for gruppe in katalog.GRUPPEN:
        zeilen = []
        for e in (e for e in katalog.EINTRAEGE if e.gruppe == gruppe):
            stufe, wert = eigen.get(e.pfad, (ERBEN if gid else "free", None))
            if eingaben and e.pfad in eingaben:
                stufe, wert = eingaben[e.pfad]
            stufen = ((ERBEN,) if gid else ()) + katalog.stufen_fuer(e)
            zeilen.append({"e": e, "stufe": stufe, "wert": wert, "stufen": stufen, "geerbt": firma.get(e.pfad)})
        abschnitte.append({"name": gruppe, "zeilen": zeilen})
    return abschnitte


@router.get("/richtlinie", response_class=HTMLResponse)
def richtlinie_seite(request: Request, gruppe: str = "", wer: Wer = Depends(in_firma),
                     con: sqlite3.Connection = Depends(verbindung)):
    g = _gruppe(con, wer.firma_id, gruppe)
    if gruppe and g is None:
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    return _seite(request, wer, con, g)


def _seite(request: Request, wer: Wer, con: sqlite3.Connection, g, eingaben: dict | None = None,
           fehler: dict | None = None, status: int = 200):
    gruppen = con.execute("SELECT id, name FROM gruppe WHERE firma_id = ? ORDER BY name", (wer.firma_id,)).fetchall()
    return seite(request, "richtlinie.html", {
        "aktiv": "richtlinie", "gruppen": gruppen, "gruppe": g,
        "abschnitte": _abschnitte(con, wer.firma_id, g["id"] if g else "", eingaben), "fehler": fehler or {},
    }, status=status, wer=wer, con=con)


def _formwert(e: katalog.Eintrag, formular) -> object:
    if e.typ == "bool":
        return formular.get("w:" + e.pfad) == "on"
    return formular.get("w:" + e.pfad, "")


@router.post("/richtlinie", response_class=HTMLResponse)
async def richtlinie_speichern(request: Request, wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    formular = await request.form()
    csrf_pruefen(request, wer, str(formular.get("csrf", "")))
    gid = str(formular.get("gruppe", ""))
    g = _gruppe(con, wer.firma_id, gid)
    if gid and g is None:
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    eingaben: dict = {}
    fehler: dict = {}
    con.execute("BEGIN")
    try:
        for e in katalog.EINTRAEGE:
            stufe = str(formular.get("s:" + e.pfad, ERBEN if gid else "free"))
            wert = _formwert(e, formular)
            eingaben[e.pfad] = (stufe, wert)
            try:
                if stufe == ERBEN and gid:
                    richtlinie.loeschen(con, wer.firma_id, gid, e.pfad)
                else:
                    richtlinie.setzen(con, wer.firma_id, gid, e.pfad, stufe, wert)
            except Ungueltig as ex:
                fehler[e.pfad] = str(ex)
        if fehler:
            con.execute("ROLLBACK")
            return _seite(request, wer, con, g, eingaben, fehler, 400)
        firmen.protokoll(con, wer.firma_id, wer.benutzer, "richtlinie", gid or "firma", jetzt(request))
        con.execute("COMMIT")
    except BaseException:
        if con.in_transaction:
            con.execute("ROLLBACK")
        raise
    ereignis("richtlinie gespeichert", firma=wer.firma_id)
    return zurueck("/richtlinie" + (f"?gruppe={gid}" if gid else ""), ok="gespeichert")


@router.get("/schluessel", response_class=HTMLResponse)
def schluessel_seite(request: Request, wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    firma = con.execute("SELECT key_modus FROM firma WHERE id = ?", (wer.firma_id,)).fetchone()
    return seite(request, "schluessel.html", {
        "aktiv": "schluessel", "schluessel": schluessel.liste(con, wer.firma_id), "zugaenge": schluessel.ZUGAENGE,
        "anbieter": katalog.ANBIETER, "key_modus": firma["key_modus"],
    }, wer=wer, con=con)


@router.post("/schluessel")
def schluessel_speichern(request: Request, csrf: str = Form(""), name: str = Form(""), eigener_name: str = Form(""),
                         anbieter: str = Form(""), basis_url: str = Form(""), key: str = Form(""),
                         modell: str = Form(""), wer: Wer = Depends(in_firma),
                         con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    if request.app.state.begrenzer.erlaubt("schluessel:" + wer.firma_id, 30, STUNDE) is not None:
        return zurueck("/schluessel", hinweis="zu_oft")
    ziel = eigener_name.strip().lower() if name == "eigen" else name
    einst = request.app.state.einst
    # SSRF (W2-SRV2): interne Ziele nur mit Freigabe der Instanz (WL_UPSTREAM_ALLOW); der Proxy prüft je Anfrage erneut.
    if basis_url.strip() and ssrf.basis_fehler(basis_url.strip()):  # W2-SFIX: vor der Netzprüfung, eigener Hinweis
        return zurueck("/schluessel", hinweis="basis_url")
    if basis_url.strip() and not ssrf.beim_speichern_ok(basis_url.strip(), einst.upstream_erlaubt, einst.aufloeser):
        return zurueck("/schluessel", hinweis="ziel_verboten")
    try:
        schluessel.speichern(con, request.app.state.krypto, wer.firma_id, ziel, anbieter, basis_url, key, jetzt(request),
                             modell=modell)
    except schluessel.SchluesselFehler as ex:
        return zurueck("/schluessel", hinweis=str(ex).removeprefix("h."))
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "key_gespeichert", ziel, jetzt(request))
    ereignis("key gespeichert", firma=wer.firma_id, zugang=ziel)
    return zurueck("/schluessel", ok="key_gespeichert")


@router.post("/schluessel/{name}/entfernen")
def schluessel_entfernen(name: str, request: Request, csrf: str = Form(""), wer: Wer = Depends(in_firma),
                         con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    if not schluessel.entfernen(con, wer.firma_id, name):
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "key_entfernt", name, jetzt(request))
    return zurueck("/schluessel", ok="key_entfernt")
