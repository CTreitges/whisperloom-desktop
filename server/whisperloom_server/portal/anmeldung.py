"""Login mit Passwort (+ TOTP), TOTP einrichten, Einladung annehmen, Abmelden, Sprache wählen.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/portal.py (Login, Einladung, Logout)
TOTP-Pflicht: Erlaubt die Firma (bzw. Instanz) `password` nicht, braucht jedes Konto TOTP und richtet es beim ersten
Login ein. Erlaubt sie `password`, gilt TOTP nur für Konten, die es selbst eingeschaltet haben.
"""

import sqlite3

from fastapi import APIRouter, Depends, Form, Request
from fastapi.responses import HTMLResponse, RedirectResponse

from .. import auth, firmen, konten, krypto, totp
from ..abhaengig import jetzt, verbindung
from ..log import ereignis
from ..netz import client_ip
from ..ratelimit import VIERTELSTUNDE
from .basis import (
    PASSWORT_ARTEN,
    SPRACH_COOKIE,
    TEXTE,
    Wer,
    angemeldet,
    cookie_name,
    cookie_setzen,
    csrf_pruefen,
    fehlerseite,
    herkunft_ok,
    seite,
    verboten,
    vor_csrf,
    vor_csrf_ok,
)

router = APIRouter()
AUSSTELLER = "WhisperLoom"


def _arten(request: Request, con: sqlite3.Connection, admin) -> tuple[str, ...]:
    einst = request.app.state.einst
    if admin["firma_id"] is None:
        return einst.anmeldearten
    return firmen.anmeldearten(con.execute("SELECT * FROM firma WHERE id = ?", (admin["firma_id"],)).fetchone(),
                               einst.anmeldearten)


def totp_pflicht(arten: tuple[str, ...]) -> bool:
    return "password" not in arten


def _vor_seite(request: Request, name: str, ctx: dict, status: int = 200):
    token, neu = vor_csrf(request)
    antwort = seite(request, name, {**ctx, "csrf": token}, status)
    if neu:
        cookie_setzen(request, antwort, "wl_csrf", neu)
    return antwort


def _sitzung(request: Request) -> str | None:
    return request.cookies.get(cookie_name(request, "wl_sitzung"))


def _neue_sitzung(request: Request, con: sqlite3.Connection, admin_id: str, stufe: str, ziel: str):
    antwort = RedirectResponse(ziel, status_code=303)
    cookie_setzen(request, antwort, "wl_sitzung", auth.sitzung_anlegen(con, admin_id, stufe, jetzt(request)))
    return antwort


def _passwort_aktiv(request: Request) -> bool:
    return any(a in request.app.state.einst.anmeldearten for a in PASSWORT_ARTEN)


@router.get("/login", response_class=HTMLResponse)
def login_seite(request: Request):
    if not _passwort_aktiv(request):
        return RedirectResponse("/", status_code=303)
    return _vor_seite(request, "login.html", {})


def _nach_passwort(request: Request, con: sqlite3.Connection, admin) -> RedirectResponse | None:
    arten = _arten(request, con, admin)
    if not any(a in arten for a in PASSWORT_ARTEN):
        return None
    if admin["totp_enc"]:
        return _neue_sitzung(request, con, admin["id"], "totp", "/login/totp")
    if totp_pflicht(arten):
        return _neue_sitzung(request, con, admin["id"], "totp_einrichten", "/login/totp-einrichten")
    return _neue_sitzung(request, con, admin["id"], "voll", "/")


@router.post("/login", response_class=HTMLResponse)
def login(request: Request, benutzername: str = Form(""), passwort: str = Form(""), csrf: str = Form(""),
          con: sqlite3.Connection = Depends(verbindung)):
    if not _passwort_aktiv(request):
        return RedirectResponse("/", status_code=303)
    if not vor_csrf_ok(request, csrf):
        return verboten(request)
    ip = client_ip(request)
    begrenzer = request.app.state.begrenzer
    if (begrenzer.erlaubt("login-ip:" + ip, 20, VIERTELSTUNDE) is not None
            or begrenzer.erlaubt("login-benutzer:" + benutzername.strip().lower()[:64], 10, VIERTELSTUNDE) is not None):
        return _vor_seite(request, "login.html", {"fehler": "h.zu_oft"}, 429)
    ergebnis, admin = auth.anmelden(con, request.app.state.krypto, benutzername[:64], passwort[:256], ip, jetzt(request))
    weiter = _nach_passwort(request, con, admin) if ergebnis == "ok" else None
    if weiter is None:
        if ergebnis == "ok":
            ergebnis = "falsch"  # Anmeldeart für diese Firma aus: nicht verraten, dass das Passwort stimmte
        ereignis("login fehler", fehler=ergebnis)
        status = 200 if ergebnis == "falsch" else 403
        return _vor_seite(request, "login.html", {"fehler": "h.login_" + ergebnis}, status)
    ereignis("login passwort", admin=admin["id"])
    return weiter


def _zwischen(request: Request, con: sqlite3.Connection, stufe: str):
    s = auth.sitzung_laden(con, _sitzung(request), jetzt(request))
    if s is None or s["stufe"] != stufe:
        return None
    return s


@router.get("/login/totp", response_class=HTMLResponse)
def totp_seite(request: Request, con: sqlite3.Connection = Depends(verbindung)):
    s = _zwischen(request, con, "totp")
    if s is None:
        return RedirectResponse("/login", status_code=303)
    return seite(request, "login_totp.html", {"csrf": s["csrf"]})


def _voll(con: sqlite3.Connection, s) -> None:
    con.execute("UPDATE sitzung SET stufe = 'voll', totp_neu_enc = NULL, totp_neu_nonce = NULL WHERE id_hash = ?",
                (s["id_hash"],))


def _totp_versuch(request: Request, s) -> bool:
    return request.app.state.begrenzer.erlaubt("totp:" + s["admin_id"], 5, VIERTELSTUNDE) is None


@router.post("/login/totp", response_class=HTMLResponse)
def totp_login(request: Request, code: str = Form(""), csrf: str = Form(""), con: sqlite3.Connection = Depends(verbindung)):
    s = _zwischen(request, con, "totp")
    if s is None:
        return RedirectResponse("/login", status_code=303)
    if not (herkunft_ok(request) and krypto.gleich(s["csrf"], csrf)):
        return verboten(request)
    if not _totp_versuch(request, s):
        auth.sitzung_loeschen(con, _sitzung(request))
        return _vor_seite(request, "login.html", {"fehler": "h.zu_oft"}, 429)
    admin = con.execute("SELECT * FROM admin WHERE id = ?", (s["admin_id"],)).fetchone()
    if not auth.totp_pruefen(con, request.app.state.krypto, admin, code[:12], jetzt(request)):
        ereignis("login fehler", fehler="totp", admin=admin["id"])
        return seite(request, "login_totp.html", {"csrf": s["csrf"], "fehler": "h.totp_falsch"}, 200)
    _voll(con, s)
    ereignis("login ok", admin=admin["id"], art="totp")
    return RedirectResponse("/", status_code=303)


def _einrichten_ctx(request: Request, con: sqlite3.Connection, s) -> dict:
    kry = request.app.state.krypto
    geheimnis = auth.totp_geheimnis(kry, s["totp_neu_enc"], s["totp_neu_nonce"], s["admin_id"])
    if geheimnis is None:
        geheimnis = totp.neues_geheimnis()
        enc, nonce = kry.verschluesseln(geheimnis, f"{s['admin_id']}|totp")
        con.execute("UPDATE sitzung SET totp_neu_enc = ?, totp_neu_nonce = ? WHERE id_hash = ?", (enc, nonce, s["id_hash"]))
    gruppen = " ".join(geheimnis[i : i + 4] for i in range(0, len(geheimnis), 4))
    return {"csrf": s["csrf"], "geheimnis": gruppen, "uri": totp.uri(geheimnis, s["benutzer"], AUSSTELLER)}


def _einrichten_sitzung(request: Request, con: sqlite3.Connection):
    """Einrichten beim Login (Stufe totp_einrichten) oder später aus dem Konto (volle Sitzung)."""
    s = auth.sitzung_laden(con, _sitzung(request), jetzt(request))
    if s is None or s["stufe"] not in ("totp_einrichten", "voll"):
        return None
    return s


@router.get("/login/totp-einrichten", response_class=HTMLResponse)
def totp_einrichten_seite(request: Request, con: sqlite3.Connection = Depends(verbindung)):
    s = _einrichten_sitzung(request, con)
    if s is None:
        return RedirectResponse("/login", status_code=303)
    return seite(request, "totp_einrichten.html", _einrichten_ctx(request, con, s))


@router.post("/login/totp-einrichten", response_class=HTMLResponse)
def totp_einrichten(request: Request, code: str = Form(""), csrf: str = Form(""),
                    con: sqlite3.Connection = Depends(verbindung)):
    s = _einrichten_sitzung(request, con)
    if s is None:
        return RedirectResponse("/login", status_code=303)
    if not (herkunft_ok(request) and krypto.gleich(s["csrf"], csrf)):
        return verboten(request)
    if not _totp_versuch(request, s):
        return seite(request, "totp_einrichten.html", {**_einrichten_ctx(request, con, s), "fehler": "h.zu_oft"}, 429)
    kry = request.app.state.krypto
    geheimnis = auth.totp_geheimnis(kry, s["totp_neu_enc"], s["totp_neu_nonce"], s["admin_id"])
    schritt = totp.pruefen(geheimnis, code[:12], jetzt(request), 0) if geheimnis else None
    if schritt is None:
        return seite(request, "totp_einrichten.html", {**_einrichten_ctx(request, con, s), "fehler": "h.totp_falsch"})
    auth.totp_speichern(con, kry, s["admin_id"], geheimnis, schritt)
    _voll(con, s)
    admin = con.execute("SELECT firma_id, benutzer FROM admin WHERE id = ?", (s["admin_id"],)).fetchone()
    firmen.protokoll(con, admin["firma_id"], admin["benutzer"], "totp_ein", admin["benutzer"], jetzt(request))
    return RedirectResponse("/?ok=totp_ein", status_code=303)


@router.get("/einladung/{token}", response_class=HTMLResponse)
def einladung_seite(token: str, request: Request, con: sqlite3.Connection = Depends(verbindung)):
    e = konten.einladung_laden(con, token, jetzt(request))
    if e is None:
        return fehlerseite(request, "f.link_titel", "f.link", 404)
    return _vor_seite(request, "einladung.html", {"benutzer": e["benutzer"]})


@router.post("/einladung/{token}", response_class=HTMLResponse)
def einladung(token: str, request: Request, passwort: str = Form(""), passwort2: str = Form(""), csrf: str = Form(""),
              con: sqlite3.Connection = Depends(verbindung)):
    if not vor_csrf_ok(request, csrf):
        return verboten(request)
    if request.app.state.begrenzer.erlaubt("einladung-ip:" + client_ip(request), 20, VIERTELSTUNDE) is not None:
        return fehlerseite(request, "f.zu_oft_titel", "h.zu_oft", 429)
    zeit = jetzt(request)
    e = konten.einladung_laden(con, token, zeit)
    if e is None:
        return fehlerseite(request, "f.link_titel", "f.link", 404)
    regel = auth.passwort_regel(passwort, passwort2)
    if regel:
        return _vor_seite(request, "einladung.html", {"benutzer": e["benutzer"], "fehler": regel}, 400)
    con.execute("UPDATE einladung SET verwendet = 1 WHERE token_hash = ?", (e["token_hash"],))
    con.execute("UPDATE admin SET pw_hash = ?, fehlversuche = 0, gesperrt_bis_ms = 0 WHERE id = ?",
                (auth.passwort_hash(passwort), e["admin_id"]))
    auth.sitzungen_beenden(con, e["admin_id"])
    firmen.protokoll(con, e["firma_id"], e["benutzer"], "einladung_angenommen", e["benutzer"], zeit)
    admin = con.execute("SELECT * FROM admin WHERE id = ?", (e["admin_id"],)).fetchone()
    return _nach_passwort(request, con, admin) or RedirectResponse("/login", status_code=303)


@router.post("/logout")
def logout(request: Request, csrf: str = Form(""), wer: Wer = Depends(angemeldet),
           con: sqlite3.Connection = Depends(verbindung)):
    csrf_pruefen(request, wer, csrf)
    auth.sitzung_loeschen(con, _sitzung(request))
    antwort = RedirectResponse("/login" if _passwort_aktiv(request) else "/", status_code=303)
    antwort.delete_cookie(cookie_name(request, "wl_sitzung"), path="/", secure=request.app.state.einst.sicher,
                          httponly=True, samesite="strict")
    return antwort


@router.get("/sprache/{code}")
def sprache_waehlen(code: str, request: Request):
    """Nur ein Cookie; Ziel immer eine feste interne Seite (kein offener Redirect)."""
    antwort = RedirectResponse("/", status_code=303)
    if code in TEXTE:
        antwort.set_cookie(SPRACH_COOKIE, code, secure=request.app.state.einst.sicher, samesite="lax", path="/",
                           max_age=365 * 86400)
    return antwort
