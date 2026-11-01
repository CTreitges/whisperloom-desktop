"""CSRF auf jeder POST-Route des Portals: ohne Token, mit falschem Token und mit fremder Herkunft → 403, nichts geändert.

Die Routen werden aus der App gelesen; eine neue POST-Route ist damit automatisch abgedeckt.
"""

import re

import pytest
from fastapi.testclient import TestClient

from whisperloom_server import db
from whisperloom_server.app import create_app

from .conftest import BASIS, admin_client, alle_routen, neue_einst, neue_firma, neuer_admin, passwort_login, seiten_csrf

_DUMMY = {"gid": "d_unbekannt", "aktion": "sperren", "aid": "a_unbekannt", "name": "stt", "fid": "t_unbekannt",
          "token": "unbekannt"}


def _post_routen():
    app = create_app(neue_einst(__import__("pathlib").Path(__import__("tempfile").mkdtemp()), lambda: 0))
    return sorted({r.path for r in alle_routen(app.routes) if "POST" in r.methods and not r.path.startswith("/api/")})


ROUTEN = _post_routen()


def _stand(einst):
    con = db.verbinden(einst.db_pfad)
    try:
        return [tuple(r) for t in ("firma", "gruppe", "geraet", "richtlinie", "schluessel", "admin", "firmencode")
                for r in con.execute(f"SELECT * FROM {t}").fetchall()]  # noqa: S608
    finally:
        con.close()


@pytest.mark.parametrize("pfad", ROUTEN)
@pytest.mark.parametrize("fall", ["ohne", "falsch", "fremde_herkunft"])
def test_post_ohne_gueltiges_csrf_403(app, einst, uhr, pfad, fall):
    f = neue_firma(einst, "firma-a")
    c = admin_client(app, einst, uhr, f["id"], "anna")
    passwort_login(TestClient(app, base_url=BASIS), "anna")  # Sitzung in Stufe „totp“ für /login/totp
    vorher = _stand(einst)
    echt = seiten_csrf(c, "/gruppen")
    daten = {"name": "x", "benutzername": "anna", "passwort": "y", "code": "123456", "slug": "neu-x",
             "s:refine.stage": "fixed", "w:refine.stage": "polish"}
    headers = {}
    if fall == "falsch":
        daten["csrf"] = "falsch-" + echt[:10]
    elif fall == "fremde_herkunft":
        daten["csrf"] = echt
        headers["Origin"] = "https://angreifer.example"
    url = re.sub(r"\{(\w+)\}", lambda m: _DUMMY[m.group(1)], pfad)
    r = c.post(url, data=daten, headers=headers, follow_redirects=False)
    if pfad in ("/login/totp", "/login/totp-einrichten", "/login/totp-spaeter") and r.status_code == 303:
        assert r.headers["location"] == "/login"  # ohne passende Zwischen-Sitzung gar nicht erst geprüft
    elif pfad == "/einladung/{token}" and r.status_code == 404:
        pass
    else:
        assert r.status_code == 403, (pfad, fall, r.status_code)
    assert _stand(einst) == vorher


def test_totp_ohne_csrf_403(client, einst):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    passwort_login(client, "anna")
    assert client.post("/login/totp", data={"code": "123456"}).status_code == 403


def test_login_ohne_csrf_403(client, einst):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    assert client.post("/login", data={"benutzername": "anna", "passwort": "x"}).status_code == 403


def test_alle_routen_gefunden():
    assert {"/login", "/gruppen", "/richtlinie", "/schluessel", "/firma/code", "/instanz/firmen"} <= set(ROUTEN)
