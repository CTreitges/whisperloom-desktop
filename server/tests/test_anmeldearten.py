"""Alle Admin-Anmeldearten mit Negativfällen: password_totp, password, trusted_header, local_only, file."""

import ipaddress
import re

import pytest
from fastapi.testclient import TestClient

from whisperloom_server import totp
from whisperloom_server.app import create_app
from whisperloom_server.config import KonfigFehler, anmeldearten_lesen

from .conftest import (
    BASIS,
    PASSWORT,
    TOTP_GEHEIMNIS,
    csrf_aus,
    neue_einst,
    neue_firma,
    neuer_admin,
    passwort_login,
    seiten_csrf,
    sql,
    totp_code,
)

# --- password_totp (Standard) ---


def test_totp_pflicht_mit_richtigem_code(client, einst, uhr):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    r = passwort_login(client, "anna")
    assert r.status_code == 303 and r.headers["location"] == "/login/totp"
    assert client.get("/", follow_redirects=False).headers["location"] == "/login"  # ohne Code kein Zugang
    r = client.post("/login/totp", data={"code": totp_code(uhr), "csrf": seiten_csrf(client, "/login/totp")},
                    follow_redirects=False)
    assert r.headers["location"] == "/"
    assert client.get("/").status_code == 200


def test_totp_falscher_code_und_wiederverwendung(client, einst, uhr):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    passwort_login(client, "anna")
    csrf = seiten_csrf(client, "/login/totp")
    r = client.post("/login/totp", data={"code": "000000", "csrf": csrf})
    assert "Der Code stimmt nicht" in r.text
    code = totp_code(uhr)
    assert client.post("/login/totp", data={"code": code, "csrf": csrf}, follow_redirects=False).status_code == 303
    # Derselbe Code gilt kein zweites Mal (neue Anmeldung im selben Zeitschritt)
    c2 = TestClient(client.app, base_url=BASIS)
    passwort_login(c2, "anna")
    r = c2.post("/login/totp", data={"code": code, "csrf": seiten_csrf(c2, "/login/totp")})
    assert "Der Code stimmt nicht" in r.text


def test_totp_einrichten_beim_ersten_login(client, einst, uhr):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna", mit_totp=False)
    r = passwort_login(client, "anna")
    assert r.headers["location"] == "/login/totp-einrichten"
    assert client.get("/", follow_redirects=False).headers["location"] == "/login"
    html = client.get("/login/totp-einrichten").text
    geheimnis = re.search(r'id="totp-geheimnis">([A-Z2-7 ]+)<', html).group(1).replace(" ", "")
    uhr.vor(31_000)
    r = client.post("/login/totp-einrichten", data={"code": totp.code_fuer(geheimnis, uhr()), "csrf": csrf_aus(html)},
                    follow_redirects=False)
    assert r.headers["location"] == "/?ok=totp_ein"
    assert sql(einst, "SELECT totp_enc IS NOT NULL FROM admin WHERE benutzer = 'anna'")[0][0] == 1


def test_totp_rfc6238_testvektor():
    # RFC 6238 Anhang B, SHA-1, Geheimnis "12345678901234567890", T = 59 s → 94287082 (8 Ziffern) → 287082 (6 Ziffern)
    geheimnis = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
    assert totp.code_fuer(geheimnis, 59_000) == "287082"
    assert totp.pruefen(geheimnis, "287082", 59_000, 0) == 1
    assert totp.pruefen(geheimnis, "287082", 59_000, 1) is None  # Schritt schon benutzt
    assert totp.pruefen(geheimnis, "28708", 59_000, 0) is None


# --- password (TOTP abgeschaltet) ---


def test_password_ohne_totp(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, anmeldearten=("password",))
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a")
        neuer_admin(einst, f["id"], "anna", mit_totp=False)
        r = passwort_login(c, "anna")
        assert r.headers["location"] == "/"
        assert c.get("/").status_code == 200


def test_password_totp_je_firma_abschaltbar(client, einst, uhr, tmp_path):
    """Instanz erlaubt beides; Firma A verlangt TOTP, Firma B schaltet es ab."""
    einst2 = neue_einst(tmp_path / "zwei", uhr, anmeldearten=("password_totp", "password"))
    with TestClient(create_app(einst2), base_url=BASIS) as c:
        a = neue_firma(einst2, "firma-a", anmeldearten="password_totp")
        b = neue_firma(einst2, "firma-b", anmeldearten="password")
        neuer_admin(einst2, a["id"], "anna", mit_totp=False)
        neuer_admin(einst2, b["id"], "bernd", mit_totp=False)
        assert passwort_login(c, "anna").headers["location"] == "/login/totp-einrichten"
        c.cookies.clear()
        assert passwort_login(c, "bernd").headers["location"] == "/"


def test_password_mit_eingeschaltetem_totp_fragt_code(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, anmeldearten=("password",))
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a")
        neuer_admin(einst, f["id"], "anna", mit_totp=True)
        assert passwort_login(c, "anna").headers["location"] == "/login/totp"


def test_falsches_passwort_und_sperre(client, einst):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    for _ in range(5):
        assert "stimmt nicht" in passwort_login(client, "anna", "falsch-falsch-falsch").text
    r = passwort_login(client, "anna")
    # Konto für 15 Minuten gesperrt, auch mit richtigem Passwort; Antwort wie bei unbekannten Namen (Review N3)
    assert r.status_code == 429 and "Zu viele Versuche" in r.text
    con = __import__("whisperloom_server.db", fromlist=["db"]).verbinden(einst.db_pfad)
    assert con.execute("SELECT gesperrt_bis_ms > 0 FROM admin WHERE benutzer = 'anna'").fetchone()[0] == 1
    con.close()


def test_firma_ohne_passwort_art_lehnt_ab(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, anmeldearten=("password_totp", "trusted_header"),
                       vertraute_proxies=(ipaddress.ip_network("10.0.0.1/32"),))
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a", anmeldearten="trusted_header")
        neuer_admin(einst, f["id"], "anna")
        r = passwort_login(c, "anna")
        assert r.status_code == 200 and "stimmt nicht" in r.text  # verrät nicht, dass das Passwort stimmte


# --- trusted_header ---


@pytest.fixture
def header_einst(tmp_path, uhr):
    return neue_einst(tmp_path, uhr, anmeldearten=("trusted_header",),
                      vertraute_proxies=(ipaddress.ip_network("10.0.0.0/30"),), header_benutzer="Remote-User")


def test_trusted_header_von_proxy_ip(header_einst):
    f = neue_firma(header_einst, "firma-a", "Firma A")
    neuer_admin(header_einst, f["id"], "anna", mit_totp=False)
    with TestClient(create_app(header_einst), base_url=BASIS, client=("10.0.0.2", 4000)) as c:
        r = c.get("/", headers={"Remote-User": "Anna"})
        assert r.status_code == 200 and "Firma A" in r.text
        csrf = csrf_aus(c.get("/gruppen", headers={"Remote-User": "anna"}).text)
        r = c.post("/gruppen", data={"csrf": csrf, "name": "Vertrieb"}, headers={"Remote-User": "anna", "Origin": BASIS},
                   follow_redirects=False)
        assert r.status_code == 303


def test_trusted_header_von_fremder_ip_wird_ignoriert(header_einst):
    f = neue_firma(header_einst, "firma-a")
    neuer_admin(header_einst, f["id"], "anna", mit_totp=False)
    with TestClient(create_app(header_einst), base_url=BASIS, client=("203.0.113.9", 4000)) as c:
        r = c.get("/", headers={"Remote-User": "anna"}, follow_redirects=False)
        assert r.status_code == 403 and "Firma" not in r.text
        # auch nicht über einen gefälschten X-Forwarded-For
        r = c.get("/", headers={"Remote-User": "anna", "X-Forwarded-For": "10.0.0.2"}, follow_redirects=False)
        assert r.status_code == 403


def test_trusted_header_unbekannter_benutzer_und_firma_ohne_art(header_einst):
    a = neue_firma(header_einst, "firma-a")
    b = neue_firma(header_einst, "firma-b", anmeldearten="password_totp")  # Firma lässt trusted_header nicht zu
    neuer_admin(header_einst, a["id"], "anna", mit_totp=False)
    neuer_admin(header_einst, b["id"], "bernd", mit_totp=False)
    with TestClient(create_app(header_einst), base_url=BASIS, client=("10.0.0.2", 4000)) as c:
        assert c.get("/", headers={"Remote-User": "niemand"}).status_code == 403
        assert c.get("/", headers={"Remote-User": "bernd"}).status_code == 403


def test_trusted_header_ohne_proxies_ist_konfigfehler():
    with pytest.raises(KonfigFehler):
        from pathlib import Path

        from whisperloom_server.config import Einstellungen

        Einstellungen(daten=Path("x"), anmeldearten=("trusted_header",)).pruefen()


# --- local_only ---


@pytest.fixture
def lokal_einst(tmp_path, uhr):
    return neue_einst(tmp_path, uhr, anmeldearten=("local_only",), basis_url="http://127.0.0.1:8080")


def test_local_only_lokal_ohne_login(lokal_einst):
    neue_firma(lokal_einst, "firma-a", "Firma A")
    with TestClient(create_app(lokal_einst), base_url="http://127.0.0.1:8080", client=("127.0.0.1", 5000)) as c:
        r = c.get("/")
        assert r.status_code == 200 and "Firma A" in r.text
        r = c.post("/gruppen", data={"csrf": csrf_aus(c.get("/gruppen").text), "name": "IT"},
                   headers={"Origin": "http://127.0.0.1:8080"}, follow_redirects=False)
        assert r.status_code == 303
        assert c.get("/instanz").status_code == 200


def test_local_only_von_nicht_lokaler_adresse_403(lokal_einst):
    neue_firma(lokal_einst, "firma-a")
    with TestClient(create_app(lokal_einst), base_url="http://127.0.0.1:8080", client=("192.168.1.20", 5000)) as c:
        for pfad in ("/", "/geraete", "/instanz", "/login"):
            r = c.get(pfad, follow_redirects=False)
            assert r.status_code in (403, 303), pfad
            if r.status_code == 303:
                assert c.get(r.headers["location"], follow_redirects=False).status_code == 403
        assert c.get("/geraete").status_code == 403


def test_local_only_hinter_proxy_auf_demselben_rechner_403(lokal_einst):
    neue_firma(lokal_einst, "firma-a")
    with TestClient(create_app(lokal_einst), base_url="http://127.0.0.1:8080", client=("127.0.0.1", 5000)) as c:
        assert c.get("/", headers={"X-Forwarded-For": "198.51.100.7"}).status_code == 403
    with TestClient(create_app(lokal_einst), base_url="http://admin.example.com", client=("127.0.0.1", 5000)) as c:
        assert c.get("/").status_code == 403  # Host nicht lokal


# --- file (kein Portal) ---


def test_datei_modus_ohne_portal_routen(tmp_path, uhr, monkeypatch):
    datei = tmp_path / "firmen.yaml"
    datei.write_text("tenants:\n  - slug: firma-a\n    name: Firma A\n    autoApprove: true\n    code: ABCD-EFGH-JKMN-PQRS\n",
                     encoding="utf-8")
    einst = neue_einst(tmp_path, uhr, anmeldearten=("file",), konfig_datei=datei)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        for pfad in ("/", "/login", "/geraete", "/richtlinie", "/schluessel", "/admins", "/firma", "/instanz",
                     "/static/portal.css"):
            assert c.get(pfad).status_code == 404, pfad
        assert c.post("/login", data={"benutzername": "x", "passwort": PASSWORT}).status_code == 404
        assert c.get("/healthz").json() == {"status": "ok"}
        r = c.post("/api/v1/enroll", json={"code": "abcd efgh jkmn pqrs", "platform": "linux"})
        assert r.status_code == 201 and r.json()["state"] == "approved"


def test_anmeldearten_lesen():
    assert anmeldearten_lesen("password_totp, trusted_header") == ("password_totp", "trusted_header")
    for falsch in ("", "file,password", "sso"):
        with pytest.raises(KonfigFehler):
            anmeldearten_lesen(falsch)


def test_totp_testgeheimnis_ist_base32():
    assert len(totp.code_fuer(TOTP_GEHEIMNIS, 0)) == 6
