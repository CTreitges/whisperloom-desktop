"""Instanz-Ebene: Firmen anlegen und sperren (Instanz-Admin bzw. local_only), Firma wählen (local_only).

Der Instanz-Admin sieht nur Name, Slug und Zähler der Firmen, nie deren Geräte, Keys oder Richtlinien.
"""

import sqlite3

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse, RedirectResponse

from .. import firmen, konten, krypto
from ..abhaengig import jetzt, verbindung
from .basis import FIRMA_COOKIE, Wer, cookie_setzen, csrf_pruefen, fehlerseite, instanz, seite, zurueck

router = APIRouter(prefix="/instanz")


def _firmen(con: sqlite3.Connection) -> list[sqlite3.Row]:
    return con.execute(
        "SELECT f.id, f.slug, f.name, f.gesperrt, f.quelle, "
        "(SELECT COUNT(*) FROM geraet g WHERE g.firma_id = f.id) AS geraete, "
        "(SELECT COUNT(*) FROM admin a WHERE a.firma_id = f.id) AS admins FROM firma f ORDER BY f.name"
    ).fetchall()


def _ctx(con: sqlite3.Connection, extra: dict | None = None) -> dict:
    return {"aktiv": "instanz", "firmen": _firmen(con), **(extra or {})}


@router.get("", response_class=HTMLResponse)
def uebersicht(request: Request, wer: Wer = Depends(instanz), con: sqlite3.Connection = Depends(verbindung)):
    return seite(request, "instanz.html", _ctx(con), wer=wer)


@router.post("/firmen", response_class=HTMLResponse)
def firma_neu(request: Request, csrf: str = Form(""), slug: str = Form(""), name: str = Form(""), owner: str = Form(""),
              wer: Wer = Depends(instanz), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    zeit = jetzt(request)
    con.execute("BEGIN")
    try:
        fid, code = firmen.anlegen(con, request.app.state.krypto, slug.strip().lower(), name, zeit)
        link = None
        if owner.strip():
            aid = konten.anlegen(con, fid, owner, "owner", zeit)
            link, _ = konten.einladung(con, request.app.state.einst.basis_url, aid, zeit)
        firmen.protokoll(con, None, wer.benutzer, "firma_neu", fid, zeit)
        con.execute("COMMIT")
    except (firmen.FirmenFehler, konten.KontoFehler) as ex:
        con.execute("ROLLBACK")
        return zurueck("/instanz", hinweis=str(ex).removeprefix("h."))
    neu = {"name": name.strip(), "code": krypto.code_anzeigen(code), "link": link}
    return seite(request, "instanz.html", _ctx(con, {"neu": neu}), wer=wer)


@router.post("/firmen/{fid}/{aktion}")
def firma_aktion(fid: str, aktion: str, request: Request, csrf: str = Form(""), wer: Wer = Depends(instanz),
                 con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    if con.execute("SELECT 1 FROM firma WHERE id = ?", (fid,)).fetchone() is None:
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    if aktion == "oeffnen" and wer.art == "lokal":
        antwort = RedirectResponse("/", status_code=303)
        cookie_setzen(request, antwort, FIRMA_COOKIE, fid)
        return antwort
    if aktion not in ("sperren", "entsperren"):
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    con.execute("UPDATE firma SET gesperrt = ? WHERE id = ?", (int(aktion == "sperren"), fid))
    firmen.protokoll(con, None, wer.benutzer, "firma_" + aktion, fid, jetzt(request))
    return zurueck("/instanz", ok="gespeichert")
