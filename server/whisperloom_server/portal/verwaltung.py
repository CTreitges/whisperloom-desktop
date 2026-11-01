"""Admins der Firma (Rollen owner/admin, Einladung, Sperre, TOTP), eigenes Konto, Audit-Log, Einstellungen der Firma."""

import sqlite3

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse

from .. import auth, firmen, konten
from ..abhaengig import jetzt, verbindung
from ..config import ANMELDEARTEN
from ..ratelimit import VIERTELSTUNDE
from .anmeldung import totp_pflicht
from .basis import Wer, cookie_name, csrf_pruefen, fehlerseite, in_firma, owner, seite, zurueck

router = APIRouter()


def _admins_ctx(con: sqlite3.Connection, wer: Wer) -> dict:
    admins = con.execute(
        "SELECT id, benutzer, rolle, gesperrt, totp_enc IS NOT NULL AS totp, pw_hash IS NOT NULL AS aktiv "
        "FROM admin WHERE firma_id = ? ORDER BY rolle DESC, benutzer",
        (wer.firma_id,),
    ).fetchall()
    protokoll = con.execute(
        "SELECT akteur, aktion, ziel, zeit_ms FROM audit WHERE firma_id = ? ORDER BY id DESC LIMIT 50", (wer.firma_id,)
    ).fetchall()
    ich = con.execute("SELECT totp_enc IS NOT NULL AS totp FROM admin WHERE id = ?", (wer.admin_id,)).fetchone()
    return {"aktiv": "admins", "admins": admins, "protokoll": protokoll, "ich_totp": bool(ich and ich["totp"])}


def _arten_der_firma(request: Request, con: sqlite3.Connection, fid: str) -> tuple[str, ...]:
    return firmen.anmeldearten(con.execute("SELECT * FROM firma WHERE id = ?", (fid,)).fetchone(),
                               request.app.state.einst.anmeldearten)


@router.get("/admins", response_class=HTMLResponse)
def admins(request: Request, wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    ctx = _admins_ctx(con, wer)
    ctx["totp_abschaltbar"] = not totp_pflicht(_arten_der_firma(request, con, wer.firma_id))
    return seite(request, "admins.html", ctx, wer=wer, con=con)


def _einladung_zeigen(request: Request, con: sqlite3.Connection, wer: Wer, aid: str, benutzer: str):
    link, ablauf = konten.einladung(con, request.app.state.einst.basis_url, aid, jetzt(request))
    ctx = {**_admins_ctx(con, wer), "einladung": {"benutzer": benutzer, "link": link, "ablauf": ablauf}}
    ctx["totp_abschaltbar"] = not totp_pflicht(_arten_der_firma(request, con, wer.firma_id))
    return seite(request, "admins.html", ctx, wer=wer, con=con)


@router.post("/admins", response_class=HTMLResponse)
def admin_neu(request: Request, csrf: str = Form(""), benutzer: str = Form(""), rolle: str = Form("admin"),
              wer: Wer = Depends(owner), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    try:
        aid = konten.anlegen(con, wer.firma_id, benutzer, rolle if rolle in konten.ROLLEN else "", jetzt(request))
    except konten.KontoFehler as ex:
        return zurueck("/admins", hinweis=str(ex).removeprefix("h."))
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "admin_neu", benutzer.strip().lower(), jetzt(request))
    return _einladung_zeigen(request, con, wer, aid, benutzer.strip().lower())


@router.post("/admins/{aid}/{aktion}", response_class=HTMLResponse)
def admin_aktion(aid: str, aktion: str, request: Request, csrf: str = Form(""), rolle: str = Form(""),
                 wer: Wer = Depends(owner), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    fid = wer.firma_id
    a = con.execute("SELECT * FROM admin WHERE id = ? AND firma_id = ?", (aid, fid)).fetchone()
    if a is None:
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    letzter_owner = a["rolle"] == "owner" and konten.ist_aktiv(con, aid) and konten.owner_anzahl(con, fid) <= 1
    zeit = jetzt(request)
    if aktion == "einladung":
        firmen.protokoll(con, fid, wer.benutzer, "admin_einladung", a["benutzer"], zeit)
        return _einladung_zeigen(request, con, wer, aid, a["benutzer"])
    if aktion in ("sperren", "loeschen", "rolle") and (letzter_owner and not (aktion == "rolle" and rolle == "owner")):
        return zurueck("/admins", hinweis="letzter_owner")
    if aktion == "sperren":
        con.execute("UPDATE admin SET gesperrt = 1 WHERE id = ?", (aid,))
        auth.sitzungen_beenden(con, aid)
    elif aktion == "entsperren":
        con.execute("UPDATE admin SET gesperrt = 0 WHERE id = ?", (aid,))
        auth.sperre_aufheben(con, request.app.state.krypto, aid, a["benutzer"])
    elif aktion == "loeschen":
        con.execute("DELETE FROM admin WHERE id = ?", (aid,))
    elif aktion == "rolle" and rolle in konten.ROLLEN:
        con.execute("UPDATE admin SET rolle = ? WHERE id = ?", (rolle, aid))
    elif aktion == "totp_zuruecksetzen":
        auth.totp_speichern(con, request.app.state.krypto, aid, None)
        auth.sitzungen_beenden(con, aid)
    else:
        return fehlerseite(request, "f.nicht_gefunden_titel", "f.nicht_gefunden", 404)
    firmen.protokoll(con, fid, wer.benutzer, "admin_" + aktion, a["benutzer"], zeit)
    return zurueck("/admins", ok="gespeichert")


@router.post("/konto/passwort", response_class=HTMLResponse)
def konto_passwort(request: Request, csrf: str = Form(""), alt: str = Form(""), neu: str = Form(""), neu2: str = Form(""),
                   wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    if wer.admin_id is None:
        return zurueck("/admins", hinweis="kein_konto")
    if request.app.state.begrenzer.erlaubt("passwort:" + wer.admin_id, 10, VIERTELSTUNDE) is not None:
        return zurueck("/admins", hinweis="zu_oft")
    a = con.execute("SELECT pw_hash FROM admin WHERE id = ?", (wer.admin_id,)).fetchone()
    if not auth.passwort_pruefen(a["pw_hash"], alt[:256]):
        return zurueck("/admins", hinweis="pw_alt")
    regel = auth.passwort_regel(neu, neu2)
    if regel:
        return zurueck("/admins", hinweis=regel.removeprefix("h."))
    con.execute("UPDATE admin SET pw_hash = ? WHERE id = ?", (auth.passwort_hash(neu), wer.admin_id))
    auth.sitzungen_beenden(con, wer.admin_id, ausser_wert=request.cookies.get(cookie_name(request, "wl_sitzung")))
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "passwort", wer.benutzer, jetzt(request))
    return zurueck("/admins", ok="passwort")


@router.post("/konto/totp-aus")
def konto_totp_aus(request: Request, csrf: str = Form(""), passwort: str = Form(""), wer: Wer = Depends(in_firma),
                   con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    if wer.admin_id is None or totp_pflicht(_arten_der_firma(request, con, wer.firma_id)):
        return zurueck("/admins", hinweis="totp_pflicht")
    if request.app.state.begrenzer.erlaubt("passwort:" + wer.admin_id, 10, VIERTELSTUNDE) is not None:
        return zurueck("/admins", hinweis="zu_oft")
    a = con.execute("SELECT pw_hash FROM admin WHERE id = ?", (wer.admin_id,)).fetchone()
    if not auth.passwort_pruefen(a["pw_hash"], passwort[:256]):
        return zurueck("/admins", hinweis="pw_alt")
    auth.totp_speichern(con, request.app.state.krypto, wer.admin_id, None)
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "totp_aus", wer.benutzer, jetzt(request))
    return zurueck("/admins", ok="totp_aus")


# --- Einstellungen der Firma (nur owner) ---


@router.get("/firma", response_class=HTMLResponse)
def firma_seite(request: Request, wer: Wer = Depends(in_firma), con: sqlite3.Connection = Depends(verbindung)):
    firma = con.execute("SELECT * FROM firma WHERE id = ?", (wer.firma_id,)).fetchone()
    return seite(request, "firma.html", {
        "aktiv": "firma", "firma": firma, "code": firmen.code_lesen(con, request.app.state.krypto, wer.firma_id, None),
        "instanz_arten": [a for a in request.app.state.einst.anmeldearten if a in ANMELDEARTEN],
        "firma_arten": firmen.anmeldearten(firma, request.app.state.einst.anmeldearten),
    }, wer=wer, con=con)


@router.post("/firma")
async def firma_speichern(request: Request, wer: Wer = Depends(owner), con: sqlite3.Connection = Depends(verbindung)):
    formular = await request.form()
    csrf_pruefen(request, wer, str(formular.get("csrf", "")))
    instanz = [a for a in request.app.state.einst.anmeldearten if a in ANMELDEARTEN]
    arten = [a for a in instanz if formular.get("art:" + a) == "on"]
    if not arten:
        return zurueck("/firma", hinweis="keine_art")
    if wer.art == "sitzung" and not any(a in arten for a in ("password", "password_totp")):
        return zurueck("/firma", hinweis="eigene_art")
    if (wer.art == "header" and "trusted_header" not in arten) or (wer.art == "lokal" and "local_only" not in arten):
        return zurueck("/firma", hinweis="eigene_art")
    modus = str(formular.get("key_modus", "proxy"))
    if modus not in ("proxy", "distribute"):
        return zurueck("/firma", hinweis="wert")
    try:
        name = firmen.name_pruefen(str(formular.get("name", "")))
        max_wartend = int(str(formular.get("max_wartend", "50")))
    except (firmen.FirmenFehler, ValueError):
        return zurueck("/firma", hinweis="wert")
    if not 1 <= max_wartend <= 10000:
        return zurueck("/firma", hinweis="wert")
    con.execute(
        "UPDATE firma SET name = ?, key_modus = ?, auto_freigabe = ?, anmeldearten = ?, max_wartend = ? WHERE id = ?",
        (name, modus, int(formular.get("auto_freigabe") == "on"),
         ",".join(arten), max_wartend, wer.firma_id),
    )
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "firma_einstellungen", wer.firma_id, jetzt(request))
    return zurueck("/firma", ok="gespeichert")


@router.post("/firma/code")
def firma_code(request: Request, csrf: str = Form(""), wer: Wer = Depends(owner), con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    firmen.code_setzen(con, request.app.state.krypto, wer.firma_id, None, jetzt(request))
    firmen.protokoll(con, wer.firma_id, wer.benutzer, "code_rotiert", wer.firma_id, jetzt(request))
    return zurueck("/firma", ok="code_neu")
