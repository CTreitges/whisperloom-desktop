"""Rate-Limits (Login, Enrollment, Gerät), Sicherheits-Header, Größengrenze, Cookies, Logs ohne Geheimnisse."""

import logging

import pytest

from whisperloom_server.ratelimit import Begrenzer

from .conftest import FAKE_KEY, PASSWORT, admin_client, bearer, enroll, neue_firma, neuer_admin, passwort_login, seiten_csrf


def test_login_rate_limit_je_ip(client, einst):
    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    for i in range(20):
        assert passwort_login(client, f"niemand-{i}", "falsch").status_code == 200
    r = passwort_login(client, "anna")
    assert r.status_code == 429 and "Zu viele Versuche" in r.text


def test_login_rate_limit_je_benutzer(client, einst, app):
    from fastapi.testclient import TestClient

    from .conftest import BASIS

    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    for i in range(10):
        c = TestClient(app, base_url=BASIS, client=(f"198.51.100.{i}", 1))
        passwort_login(c, "anna", "falsch")
    c = TestClient(app, base_url=BASIS, client=("198.51.100.99", 1))
    assert passwort_login(c, "anna", PASSWORT).status_code == 429


def test_enrollment_rate_limit(client, einst):
    f = neue_firma(einst, "firma-a", max_wartend=100)
    for _ in range(20):
        assert client.post("/api/v1/enroll", json={"code": "0000-0000-0000-0000", "platform": "x"}).status_code == 404
    r = client.post("/api/v1/enroll", json={"code": f["code"], "platform": "x"})
    assert r.status_code == 429 and r.json()["error"] == "rate_limited" and int(r.headers["retry-after"]) > 0


def test_geraet_rate_limit(client, einst):
    f = neue_firma(einst, "firma-a", auto_freigabe=1)
    h = bearer(enroll(client, f["code"])["deviceToken"])
    for _ in range(120):
        client.get("/api/v1/device", headers=h)
    assert client.get("/api/v1/device", headers=h).status_code == 429


def test_begrenzer_fenster():
    t = [0]
    b = Begrenzer(lambda: t[0])
    assert b.erlaubt("x", 2, 1000) is None and b.erlaubt("x", 2, 1000) is None
    assert b.erlaubt("x", 2, 1000) == 1
    t[0] = 1001
    assert b.erlaubt("x", 2, 1000) is None


def test_sicherheits_header_und_cookies(app, einst, uhr, client):
    r = client.get("/login")
    for h in ("content-security-policy", "x-frame-options", "x-content-type-options", "referrer-policy"):
        assert h in r.headers
    assert r.headers["cache-control"] == "no-store"
    assert "__Host-wl_csrf" in r.headers["set-cookie"] and "Secure" in r.headers["set-cookie"]
    assert "HttpOnly" in r.headers["set-cookie"] and "SameSite=strict" in r.headers["set-cookie"]


def test_referrer_policy_laesst_origin_bei_formularen_zu(client):
    """Bei „no-referrer“ schicken Browser für Formular-POSTs `Origin: null` → die Origin-Prüfung sperrte jedes Formular
    (gefunden mit Playwright/Chromium). „same-origin“ schützt nach außen und lässt Origin für die eigene Seite stehen."""
    assert client.get("/login").headers["referrer-policy"] == "same-origin"


def test_origin_null_wird_abgelehnt(client, einst):
    from .conftest import csrf_aus

    f = neue_firma(einst, "firma-a")
    neuer_admin(einst, f["id"], "anna")
    r = client.get("/login")
    r = client.post("/login", data={"benutzername": "anna", "passwort": PASSWORT, "csrf": csrf_aus(r.text)},
                    headers={"Origin": "null"}, follow_redirects=False)
    assert r.status_code == 403


def test_anfrage_zu_gross(client):
    r = client.post("/api/v1/enroll", content=b"{" + b" " * 70_000 + b"}", headers={"content-type": "application/json"})
    assert r.status_code == 413 and r.json()["error"] == "payload_too_large"


def test_unbekannte_api_route(client):
    r = client.get("/api/v1/gibtsnicht")
    assert r.status_code == 404 and r.json()["error"] == "not_found"


@pytest.fixture
def logzeilen():
    zeilen: list[str] = []

    class Sammler(logging.Handler):
        def emit(self, record):
            zeilen.append(self.format(record))

    h = Sammler(level=logging.DEBUG)
    namen = ["whisperloom_server", "", "uvicorn", "uvicorn.error", "fastapi"]
    for n in namen:
        logging.getLogger(n).addHandler(h)
    yield zeilen
    for n in namen:
        logging.getLogger(n).removeHandler(h)


def test_logs_ohne_geheimnisse(app, einst, uhr, client, logzeilen):
    f = neue_firma(einst, "firma-a", auto_freigabe=1, key_modus="distribute")
    admin = admin_client(app, einst, uhr, f["id"], "anna")
    admin.post("/schluessel", data={"csrf": seiten_csrf(admin, "/schluessel"), "name": "stt", "anbieter": "groq",
                                    "key": FAKE_KEY})
    g = enroll(client, f["code"])
    client.get("/api/v1/config", headers=bearer(g["deviceToken"]))
    text = "\n".join(logzeilen)
    assert "enroll ok" in text and "key gespeichert" in text
    for geheim in (FAKE_KEY, g["deviceToken"], f["code"], PASSWORT):
        assert geheim not in text
