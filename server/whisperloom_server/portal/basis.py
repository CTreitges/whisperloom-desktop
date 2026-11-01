"""Portal-Grundlagen: angemeldete Person (alle Anmeldearten), CSRF, Texte DE/EN, Seitenaufbau.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/portal.py
Mandanten-Trennung: Firmen-Routen nehmen `firma_id` nur aus [Wer], nie aus URL oder Formular.
"""

import json
import re
import sqlite3
import time
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlsplit

from fastapi import Depends, Request
from fastapi.responses import RedirectResponse
from fastapi.templating import Jinja2Templates

from .. import auth, firmen, krypto, netz
from ..abhaengig import jetzt, verbindung

_PAKET = Path(__file__).parent.parent
# Context7: FastAPI Jinja2Templates(directory=...) + TemplateResponse(request=..., name=..., context=...).
# Autoescape ist für .html an (Jinja2-Standard in Starlette).
templates = Jinja2Templates(directory=str(_PAKET / "templates"))
TEXTE = {s: json.loads((_PAKET / "texte" / f"{s}.json").read_text(encoding="utf-8")) for s in ("de", "en")}
SPRACH_COOKIE = "wl_sprache"
FIRMA_COOKIE = "wl_firma"
PASSWORT_ARTEN = ("password", "password_totp")
_LOKAL_ORIGIN = re.compile(r"^https?://(127\.0\.0\.1|localhost|\[::1\])(:\d{1,5})?$")

# Navigation der Firmen-Seiten (W2-SRV2 hängt „Nutzung“ und „Budget“ an).
NAVIGATION: list[tuple[str, str]] = [
    ("/", "uebersicht"), ("/geraete", "geraete"), ("/gruppen", "gruppen"), ("/richtlinie", "richtlinie"),
    ("/schluessel", "schluessel"), ("/admins", "admins"), ("/firma", "firma"),
]


@dataclass
class Wer:
    art: str  # sitzung | header | lokal
    benutzer: str
    rolle: str  # instance | owner | admin (lokal: owner + instanz)
    firma_id: str | None
    csrf: str
    admin_id: str | None = None
    instanz: bool = False
    neues_cookie: str | None = None

    @property
    def owner(self) -> bool:
        return self.rolle == "owner"


class Weiter(Exception):
    """Abbruch mit fertiger Antwort (Weiterleitung, 403)."""

    def __init__(self, antwort):
        self.antwort = antwort


def weiter_antwort(_request: Request, exc: Weiter):
    return exc.antwort


def sprache(request: Request) -> str:
    s = request.cookies.get(SPRACH_COOKIE)
    if s in TEXTE:
        return s
    kopf = request.headers.get("accept-language", "").lower()
    return "en" if kopf.startswith("en") else "de"


def text(sprache_: str, schluessel: str) -> str:
    return TEXTE[sprache_].get(schluessel) or TEXTE["de"].get(schluessel) or schluessel


def option(sprache_: str, pfad: str, wert) -> str:
    """Anzeigename eines Auswahlwerts: o.<pfad>.<wert>, sonst o.<wert>, sonst der Wert selbst."""
    t = TEXTE[sprache_]
    return t.get(f"o.{pfad}.{wert}") or t.get(f"o.{wert}") or str(wert)


def _zeit(ms: int | None) -> str:
    return time.strftime("%Y-%m-%d %H:%M", time.localtime(ms / 1000)) if ms else "–"


templates.env.filters["zeit"] = _zeit


def cookie_name(request: Request, basis: str) -> str:
    return ("__Host-" + basis) if request.app.state.einst.sicher else basis


def cookie_setzen(request: Request, antwort, name: str, wert: str, http_only: bool = True) -> None:
    antwort.set_cookie(cookie_name(request, name), wert, httponly=http_only, secure=request.app.state.einst.sicher,
                       samesite="strict", path="/")


def seite(request: Request, name: str, ctx: dict, status: int = 200, wer: Wer | None = None,
          con: sqlite3.Connection | None = None):
    s = sprache(request)
    ok = request.query_params.get("ok", "")
    hinweis = request.query_params.get("hinweis", "")
    basis = {
        "t": lambda k: text(s, k),
        "opt": lambda p, w: option(s, p, w),
        "sprache": s,
        "ok_text": TEXTE[s].get("ok." + ok) if ok else None,
        "hinweis_text": TEXTE[s].get("h." + hinweis) if hinweis else None,
        "wer": wer,
        "navigation": NAVIGATION if wer and wer.firma_id else [],
        "pfad": request.url.path,
        "firma_name": "",
    }
    if wer and wer.firma_id and con is not None:
        row = con.execute("SELECT name FROM firma WHERE id = ?", (wer.firma_id,)).fetchone()
        basis["firma_name"] = row["name"] if row else ""
    antwort = templates.TemplateResponse(request=request, name=name, context={**basis, **ctx}, status_code=status)
    if wer and wer.neues_cookie:
        cookie_setzen(request, antwort, "wl_csrf", wer.neues_cookie)
    return antwort


def fehlerseite(request: Request, titel: str, inhalt: str, status: int):
    return seite(request, "fehler.html", {"titel_key": titel, "text_key": inhalt}, status)


def zurueck(pfad: str, ok: str | None = None, hinweis: str | None = None) -> RedirectResponse:
    q = f"?ok={ok}" if ok else (f"?hinweis={hinweis}" if hinweis else "")
    trenner = "&" if "?" in pfad and q else ""
    return RedirectResponse(pfad + (q.replace("?", trenner, 1) if trenner else q), status_code=303)


# --- CSRF ---


def herkunft_ok(request: Request, lokal: bool = False) -> bool:
    """Origin (bzw. Referer) muss die Basis-URL sein; local_only akzeptiert jede Loopback-Herkunft."""
    origin = request.headers.get("origin")
    if origin is None:
        referer = request.headers.get("referer")
        if referer is None:
            return True
        teile = urlsplit(referer)
        origin = f"{teile.scheme}://{teile.netloc}"
    return origin == request.app.state.einst.basis_url or (lokal and bool(_LOKAL_ORIGIN.match(origin)))


def vor_csrf(request: Request) -> tuple[str, str | None]:
    """(Formular-Token, neuer Cookie-Wert oder None) für Formulare ohne Sitzung."""
    wert = request.cookies.get(cookie_name(request, "wl_csrf"))
    neu = None
    if not wert or len(wert) > 64:
        wert = neu = krypto.token()
    return auth.vor_csrf(request.app.state.krypto, wert), neu


def vor_csrf_ok(request: Request, csrf: str, lokal: bool = False) -> bool:
    return herkunft_ok(request, lokal) and auth.vor_csrf_ok(
        request.app.state.krypto, request.cookies.get(cookie_name(request, "wl_csrf")), csrf
    )


def verboten(request: Request):
    return fehlerseite(request, "f.neu_laden_titel", "f.neu_laden", 403)


def csrf_pruefen(request: Request, wer: Wer, csrf: str) -> None:
    if wer.art == "lokal":
        ok = vor_csrf_ok(request, csrf, lokal=True)
    else:
        ok = herkunft_ok(request) and krypto.gleich(wer.csrf, csrf or "")
    if not ok:
        raise Weiter(verboten(request))


# --- Wer ist angemeldet? ---


def _lokal(request: Request, con: sqlite3.Connection) -> Wer:
    token, neu = vor_csrf(request)
    fid = request.cookies.get(cookie_name(request, FIRMA_COOKIE))
    firma = con.execute("SELECT * FROM firma WHERE id = ?", (fid,)).fetchone() if fid else None
    if firma is None or "local_only" not in firmen.anmeldearten(firma, request.app.state.einst.anmeldearten):
        alle = [f for f in con.execute("SELECT * FROM firma ORDER BY name").fetchall()
                if "local_only" in firmen.anmeldearten(f, request.app.state.einst.anmeldearten)]
        firma = alle[0] if len(alle) == 1 else None
    return Wer("lokal", "lokal", "owner", firma["id"] if firma else None, token, instanz=True, neues_cookie=neu)


def _header(request: Request, con: sqlite3.Connection, benutzer: str) -> Wer:
    a = con.execute(
        "SELECT a.*, f.anmeldearten, f.gesperrt AS firma_gesperrt FROM admin a LEFT JOIN firma f ON f.id = a.firma_id "
        "WHERE a.benutzer = ?",
        (benutzer.strip().lower()[:64],),
    ).fetchone()
    einst = request.app.state.einst
    erlaubt = a is not None and not a["gesperrt"] and (
        a["firma_id"] is None or (not a["firma_gesperrt"] and "trusted_header" in firmen.anmeldearten(a, einst.anmeldearten))
    )
    if not erlaubt:
        raise Weiter(fehlerseite(request, "f.kein_zugang_titel", "f.kein_zugang", 403))
    csrf = request.app.state.krypto.hmac(f"hcsrf:{a['id']}:{a['benutzer']}")
    return Wer("header", a["benutzer"], a["rolle"], a["firma_id"], csrf, a["id"], instanz=a["rolle"] == "instance")


def _sitzung(request: Request, con: sqlite3.Connection) -> Wer | None:
    s = auth.sitzung_laden(con, request.cookies.get(cookie_name(request, "wl_sitzung")), jetzt(request))
    if s is None or s["stufe"] != "voll":
        return None
    if s["firma_id"]:
        firma = con.execute("SELECT * FROM firma WHERE id = ?", (s["firma_id"],)).fetchone()
        arten = firmen.anmeldearten(firma, request.app.state.einst.anmeldearten)
        if firma["gesperrt"] or not any(a in arten for a in PASSWORT_ARTEN):
            return None
    else:
        arten = request.app.state.einst.anmeldearten
    if "password" not in arten and not s["hat_totp"]:
        # Die Anmeldeart wurde verschärft (TOTP jetzt Pflicht): Sitzung ohne zweiten Faktor endet.
        auth.sitzung_loeschen(con, request.cookies.get(cookie_name(request, "wl_sitzung")))
        return None
    return Wer("sitzung", s["benutzer"], s["rolle"], s["firma_id"], s["csrf"], s["admin_id"], instanz=s["rolle"] == "instance")


def wer_laden(request: Request, con: sqlite3.Connection) -> Wer | None:
    einst = request.app.state.einst
    arten = einst.anmeldearten
    if "local_only" in arten and netz.ist_lokal(request):
        return _lokal(request, con)
    if arten == ("local_only",):
        raise Weiter(fehlerseite(request, "f.nur_lokal_titel", "f.nur_lokal", 403))
    if "trusted_header" in arten and netz.von_vertrautem_proxy(request):
        werte = request.headers.getlist(einst.header_benutzer)
        if len(werte) > 1:
            # Mehrfacher Header: ein Wert kann vom Client stammen, den der Proxy nur ergänzt statt ersetzt hat.
            raise Weiter(fehlerseite(request, "f.kein_zugang_titel", "f.kein_zugang", 403))
        if werte and werte[0]:
            return _header(request, con, werte[0])
    if any(a in arten for a in PASSWORT_ARTEN):
        return _sitzung(request, con)
    return None


def angemeldet(request: Request, con: sqlite3.Connection = Depends(verbindung)) -> Wer:
    wer = wer_laden(request, con)
    if wer is None:
        if any(a in request.app.state.einst.anmeldearten for a in PASSWORT_ARTEN):
            raise Weiter(RedirectResponse("/login", status_code=303))
        raise Weiter(fehlerseite(request, "f.kein_zugang_titel", "f.kein_zugang", 403))
    return wer


def in_firma(wer: Wer = Depends(angemeldet)) -> Wer:
    if wer.firma_id is None:
        raise Weiter(RedirectResponse("/instanz", status_code=303))
    return wer


def owner(request: Request, wer: Wer = Depends(in_firma)) -> Wer:
    if not wer.owner:
        raise Weiter(fehlerseite(request, "f.nur_owner_titel", "f.nur_owner", 403))
    return wer


def instanz(request: Request, wer: Wer = Depends(angemeldet)) -> Wer:
    if not wer.instanz:
        raise Weiter(fehlerseite(request, "f.nur_instanz_titel", "f.nur_instanz", 403))
    return wer
