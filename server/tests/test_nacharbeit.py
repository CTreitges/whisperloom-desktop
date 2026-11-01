"""Nacharbeit Prüfrunde 1 (W2/W2-SRV1/review-orchestrator.md): Sitzungs-ID nach TOTP, TOTP ersetzen nur mit Passwort,
Hinweis auf Code-Rotation beim Sperren unter Auto-Freigabe."""

import re

from fastapi.testclient import TestClient

from whisperloom_server import totp
from whisperloom_server.app import create_app

from .conftest import (
    BASIS,
    PASSWORT,
    TOTP_GEHEIMNIS,
    admin_client,
    csrf_aus,
    enroll,
    neue_einst,
    neue_firma,
    neuer_admin,
    passwort_login,
    seiten_csrf,
    sql,
    totp_code,
)


def _sitzungswert(c: TestClient) -> str:
    return c.cookies.get("__Host-wl_sitzung")


# --- 2: neue Sitzungs-ID beim Wechsel der Rechte (Passwort → voll) ---


def test_neue_sitzungs_id_nach_totp(client, einst, uhr):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    assert passwort_login(client, "anna").headers["location"] == "/login/totp"
    halb = _sitzungswert(client)
    r = client.post("/login/totp", data={"code": totp_code(uhr), "csrf": seiten_csrf(client, "/login/totp")},
                    follow_redirects=False)
    assert r.headers["location"] == "/"
    voll = _sitzungswert(client)
    assert voll and voll != halb
    alt = TestClient(client.app, base_url=BASIS, cookies={"__Host-wl_sitzung": halb})
    assert alt.get("/geraete", follow_redirects=False).headers["location"] == "/login"  # alte ID ungültig
    assert client.get("/geraete").status_code == 200


def test_neue_sitzungs_id_nach_totp_einrichten(client, einst, uhr):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna", mit_totp=False)
    passwort_login(client, "anna")
    halb = _sitzungswert(client)
    html = client.get("/login/totp-einrichten").text
    geheimnis = re.search(r'id="totp-geheimnis">([A-Z2-7 ]+)<', html).group(1).replace(" ", "")
    uhr.vor(31_000)
    client.post("/login/totp-einrichten", data={"code": totp.code_fuer(geheimnis, uhr()), "csrf": csrf_aus(html)},
                follow_redirects=False)
    assert _sitzungswert(client) != halb
    assert sql(einst, "SELECT COUNT(*) FROM sitzung")[0][0] == 1


# --- 3: aus voller Sitzung TOTP nur mit Passwort einrichten bzw. ersetzen ---


def _neues_totp(c: TestClient, uhr, passwort: str | None):
    html = c.get("/login/totp-einrichten").text
    geheimnis = re.search(r'id="totp-geheimnis">([A-Z2-7 ]+)<', html).group(1).replace(" ", "")
    uhr.vor(31_000)
    daten = {"code": totp.code_fuer(geheimnis, uhr()), "csrf": csrf_aus(html)}
    if passwort is not None:
        daten["passwort"] = passwort
    return c.post("/login/totp-einrichten", data=daten, follow_redirects=False), geheimnis


def test_totp_ersetzen_ohne_passwort_abgelehnt(app, einst, uhr):
    f = neue_firma(einst, "firma-a")
    with admin_client(app, einst, uhr, f["id"], "anna") as c:  # volle Sitzung, TOTP = Test-Geheimnis
        vorher = sql(einst, "SELECT totp_enc FROM admin WHERE benutzer = 'anna'")[0][0]
        for pw in (None, "falsch-falsch-falsch"):
            r, _ = _neues_totp(c, uhr, pw)
            assert r.status_code != 303 or "ok=totp_ein" not in r.headers.get("location", "")
            assert sql(einst, "SELECT totp_enc FROM admin WHERE benutzer = 'anna'")[0][0] == vorher
        r, neu = _neues_totp(c, uhr, PASSWORT)
        assert r.status_code == 303 and "ok=totp_ein" in r.headers["location"]
        assert sql(einst, "SELECT totp_enc FROM admin WHERE benutzer = 'anna'")[0][0] != vorher
        assert neu != TOTP_GEHEIMNIS


# --- 4: Hinweis auf Code-Rotation beim Sperren unter Auto-Freigabe ---


def test_sperren_unter_auto_freigabe_nennt_code_rotation(app, einst, uhr, client):
    for auto, erwartet in ((1, True), (0, False)):
        f = neue_firma(einst, f"firma-{auto}", auto_freigabe=auto)
        g = enroll(client, f["code"])
        with admin_client(app, einst, uhr, f["id"], f"chef-{auto}") as c:
            r = c.post(f"/geraete/{g['deviceId']}/sperren", data={"csrf": seiten_csrf(c, "/geraete")})
            assert r.status_code == 200
            assert ("Firmencode erneuern" in r.text) is erwartet, auto
            assert ('<a href="/firma">Firmencode erneuern</a>' in r.text) is erwartet


def test_hinweis_auch_englisch(app, einst, uhr, client):
    f = neue_firma(einst, "firma-en", auto_freigabe=1)
    g = enroll(client, f["code"])
    with admin_client(app, einst, uhr, f["id"], "chef-en") as c:
        c.cookies.set("wl_sprache", "en")
        r = c.post(f"/geraete/{g['deviceId']}/sperren", data={"csrf": seiten_csrf(c, "/geraete")})
        assert "renew the company code" in r.text


def test_local_only_einst_unberuehrt(tmp_path, uhr):
    # Kontrolle: lokale Person hat kein Konto → TOTP-Einrichten bleibt für sie unerreichbar
    e = neue_einst(tmp_path, uhr, anmeldearten=("local_only",), basis_url="http://127.0.0.1:8080")
    neue_firma(e, "firma-l")
    with TestClient(create_app(e), base_url="http://127.0.0.1:8080", client=("127.0.0.1", 1)) as c:
        assert c.get("/login/totp-einrichten", follow_redirects=False).headers["location"] == "/login"
