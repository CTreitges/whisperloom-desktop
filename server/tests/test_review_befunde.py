"""Befunde des Prüfer-Reviews (W2/W2-SRV1/review.md) als Regressionstests: H1, M1–M3, N1–N3, N5–N9.

N4 (Benutzernamen instanzweit eindeutig) bleibt bewusst: Login und trusted_header identifizieren über den Namen.
"""

import ipaddress
import os
import time

import pytest
from fastapi.testclient import TestClient

from whisperloom_server import dateimodus, db, richtlinie, schluessel
from whisperloom_server.app import create_app
from whisperloom_server.ratelimit import Begrenzer

from .conftest import (
    BASIS,
    PASSWORT,
    admin_client,
    bearer,
    enroll,
    freies_geraet,
    kry,
    neue_einst,
    neue_firma,
    neuer_admin,
    passwort_login,
    seiten_csrf,
    sql,
    totp_code,
)

STUNDE = 3_600_000


# --- R1: altes Token lebt durch erneutes Rotieren beliebig lange (Vertrag §3.4: höchstens 24 h) ---
def test_r1_altes_token_hoechstens_24h(client, einst, uhr):
    f = neue_firma(einst, "r1")
    alt = freies_geraet(client, einst, f["code"])["deviceToken"]
    assert client.post("/api/v1/device/token", headers=bearer(alt)).status_code == 200
    uhr.vor(23 * STUNDE)
    assert client.post("/api/v1/device/token", headers=bearer(alt)).status_code == 200  # erneut rotiert …
    uhr.vor(2 * STUNDE)
    # … verlängert die Frist nicht: 25 h nach der ersten Rotation, neues Token nie benutzt
    assert client.get("/api/v1/device", headers=bearer(alt)).status_code == 401


# --- R2: Datei-Modus – Fehler außerhalb von ValueError/DateiFehler werfen aus laden() heraus ---
GUT = """
tenants:
  - slug: aa
    name: A
    code: ABCD-EFGH-JKMN-PQRS
"""
DOPPELT = """
tenants:
  - slug: aa
    name: A
    code: ABCD-EFGH-JKMN-PQRS
  - slug: bb
    name: B
    code: ABCD-EFGH-JKMN-PQRS
"""
LISTE_KEYS = """
tenants:
  - slug: aa
    name: A
    keys: [stt]
"""
MAX_DICT = """
tenants:
  - slug: aa
    name: A
    maxPending: {x: 1}
"""


@pytest.mark.parametrize("kaputt", [DOPPELT, LISTE_KEYS, MAX_DICT], ids=["code_doppelt", "keys_liste", "maxPending_dict"])
def test_r2_datei_fehler_aendert_nichts_und_wirft_nicht(tmp_path, uhr, kaputt):
    p = tmp_path / "firmen.yaml"
    p.write_text(GUT, encoding="utf-8")
    e = neue_einst(tmp_path, uhr, anmeldearten=("file",), konfig_datei=p)
    a = create_app(e)
    p.write_text(kaputt, encoding="utf-8")
    assert a.state.datei.laden() is False  # heute: IntegrityError / AttributeError / TypeError fliegt heraus


# --- R3: „alle Anmeldearten“ wird als '' gespeichert und erweitert sich still mit der Instanz ---
def test_r3_firma_auswahl_wird_nicht_still_erweitert(tmp_path, uhr):
    e1 = neue_einst(tmp_path, uhr, anmeldearten=("password_totp",))
    a1 = create_app(e1)
    f = neue_firma(e1, "r3")
    with admin_client(a1, e1, uhr, f["id"], "chef") as c:
        csrf = seiten_csrf(c, "/firma")
        r = c.post("/firma", data={"csrf": csrf, "name": "R3", "key_modus": "proxy", "max_wartend": "50",
                                   "art:password_totp": "on"}, follow_redirects=False)
        assert r.status_code == 303 and "ok=" in r.headers["location"]
    # Owner hat ausdrücklich nur „Passwort + TOTP“ gewählt. Später erlaubt die Instanz zusätzlich `password`:
    e2 = neue_einst(tmp_path, uhr, anmeldearten=("password_totp", "password"))
    a2 = create_app(e2)
    neuer_admin(e2, f["id"], "ohne-totp", "admin", mit_totp=False)
    with TestClient(a2, base_url=BASIS) as c:
        r = passwort_login(c, "ohne-totp")
        # erwartet: TOTP bleibt Pflicht (Einrichten erzwingen); heute: volle Sitzung nur mit Passwort
        assert r.headers["location"] == "/login/totp-einrichten", r.headers["location"]


# --- R4: Proxy-Modus setzt stt.providerId=custom fest, lässt aber allowedProviders ohne custom stehen ---
def test_r4_proxy_modus_allowed_providers_konsistent(client, einst):
    f = neue_firma(einst, "r4")
    con = db.verbinden(einst.db_pfad)
    try:
        richtlinie.setzen(con, f["id"], "", "policy.allowedProviders", "fixed", "groq")
    finally:
        con.close()
    tok = freies_geraet(client, einst, f["code"])["deviceToken"]
    k = client.get("/api/v1/config", headers=bearer(tok)).json()
    assert k["settings"]["stt"]["providerId"] == "custom"
    assert "custom" in k["policy"]["allowedProviders"], k["policy"]  # heute ['groq'] → App lehnt Import ab


# --- R5: Konto-Sperre verrät, ob ein Benutzername existiert ---
def test_r5_sperre_ohne_benutzer_aufzaehlung(client, einst):
    f = neue_firma(einst, "r5")
    neuer_admin(einst, f["id"], "echt")
    status = {}
    for name in ("echt", "gibt-es-nicht"):
        for _ in range(5):
            passwort_login(client, name, "falsch-falsch-falsch")
        status[name] = passwort_login(client, name, "falsch-falsch-falsch").status_code
    assert status["echt"] == status["gibt-es-nicht"], status  # heute 403 vs. 200


# --- R6: TOTP-Schritt ignoriert die Konto-Sperre ---
def test_r6_totp_schritt_respektiert_sperre(client, einst, uhr):
    f = neue_firma(einst, "r6")
    neuer_admin(einst, f["id"], "chef")
    assert passwort_login(client, "chef").headers["location"] == "/login/totp"
    con = db.verbinden(einst.db_pfad)
    try:
        con.execute("UPDATE admin SET gesperrt_bis_ms = ? WHERE benutzer = 'chef'", (uhr() + 10 * STUNDE,))
    finally:
        con.close()
    csrf = seiten_csrf(client, "/login/totp")
    r = client.post("/login/totp", data={"code": totp_code(uhr), "csrf": csrf}, follow_redirects=False)
    assert r.headers.get("location") != "/", "gesperrtes Konto wurde per TOTP-Schritt voll angemeldet"


# --- R7: doppelter Anmelde-Header – Server nimmt den ersten (ggf. vom Client gesetzten) Wert ---
def test_r7_doppelter_trusted_header_wird_abgelehnt(tmp_path, uhr):
    e = neue_einst(tmp_path, uhr, anmeldearten=("trusted_header",),
                   vertraute_proxies=(ipaddress.ip_network("10.0.0.0/30"),))
    f = neue_firma(e, "r7")
    neuer_admin(e, None, "root", "instance", mit_totp=False)
    neuer_admin(e, f["id"], "normal", "admin", mit_totp=False)
    with TestClient(create_app(e), base_url=BASIS, client=("10.0.0.2", 4000)) as c:
        r = c.get("/instanz", headers=[("Remote-User", "root"), ("Remote-User", "normal")], follow_redirects=False)
        assert r.status_code == 403, r.status_code  # heute 200: erster Wert „root“ gewinnt


# --- R9: wartendes Gerät sieht configVersion, die sich mit den verteilten Keys ändert ---
def test_r9_wartendes_geraet_ohne_key_orakel(client, einst):
    f = neue_firma(einst, "r9", key_modus="distribute")
    con = db.verbinden(einst.db_pfad)
    try:
        schluessel.speichern(con, kry(einst), f["id"], "stt", "groq", "", "test-key-not-real-1", 0)
        tok = enroll(client, f["code"])["deviceToken"]  # pending
        v1 = client.get("/api/v1/device", headers=bearer(tok)).json()["configVersion"]
        schluessel.speichern(con, kry(einst), f["id"], "stt", "groq", "", "test-key-not-real-2", 0)
        v2 = client.get("/api/v1/device", headers=bearer(tok)).json()["configVersion"]
    finally:
        con.close()
    assert v1 == v2, "configVersion eines wartenden Geräts hängt vom Key ab"


# --- R10: letzter aktiver owner kann sich entfernen, wenn nur ein eingeladener (inaktiver) owner existiert ---
def test_r10_letzter_aktiver_owner_bleibt(app, einst, uhr):
    f = neue_firma(einst, "r10")
    with admin_client(app, einst, uhr, f["id"], "chef") as c:
        csrf = seiten_csrf(c, "/admins")
        c.post("/admins", data={"csrf": csrf, "benutzer": "neu.owner", "rolle": "owner"})
        ich = sql(einst, "SELECT id FROM admin WHERE benutzer = 'chef'")[0]["id"]
        csrf = seiten_csrf(c, "/admins")
        r = c.post(f"/admins/{ich}/loeschen", data={"csrf": csrf}, follow_redirects=False)
        assert "letzter_owner" in r.headers.get("location", ""), r.headers.get("location")


# --- R2b: nach einem solchen Fehler stirbt der Beobachter-Thread; spätere gültige Änderungen kommen nie an ---
def test_r2b_beobachter_ueberlebt_fehler(tmp_path, uhr):
    p = tmp_path / "firmen.yaml"
    p.write_text(GUT, encoding="utf-8")
    e = neue_einst(tmp_path, uhr, anmeldearten=("file",), konfig_datei=p)
    b = dateimodus.Beobachter(e, kry(e), intervall_s=0.05)
    b.laden(streng=True)
    b.starten()
    try:
        p.write_text(DOPPELT, encoding="utf-8")
        os.utime(p, (time.time() + 5, time.time() + 5))
        time.sleep(0.5)
        p.write_text(GUT.replace("name: A", "name: Neu"), encoding="utf-8")
        os.utime(p, (time.time() + 10, time.time() + 10))
        time.sleep(0.5)
        assert b._faden.is_alive(), "Beobachter-Thread beendet"
        assert sql(e, "SELECT name FROM firma WHERE slug = 'aa'")[0]["name"] == "Neu"
    finally:
        b.stoppen()


# --- N7: TOTP ausschalten verlangt das Passwort ---
def test_n7_totp_aus_nur_mit_passwort(tmp_path, uhr):
    e = neue_einst(tmp_path, uhr, anmeldearten=("password",))
    a = create_app(e)
    f = neue_firma(e, "n7")
    with admin_client(a, e, uhr, f["id"], "chef") as c:
        r = c.post("/konto/totp-aus", data={"csrf": seiten_csrf(c, "/admins"), "passwort": "falsch"}, follow_redirects=False)
        assert "hinweis=pw_alt" in r.headers["location"]
        assert sql(e, "SELECT totp_enc IS NOT NULL FROM admin WHERE benutzer = 'chef'")[0][0] == 1
        r = c.post("/konto/totp-aus", data={"csrf": seiten_csrf(c, "/admins"), "passwort": PASSWORT}, follow_redirects=False)
        assert "ok=totp_aus" in r.headers["location"]


# --- N8: verschärft die Firma die Anmeldearten, enden Sitzungen ohne TOTP ---
def test_n8_sitzung_endet_wenn_totp_pflicht_kommt(tmp_path, uhr):
    e = neue_einst(tmp_path, uhr, anmeldearten=("password_totp", "password"))
    a = create_app(e)
    f = neue_firma(e, "n8", anmeldearten="password")
    neuer_admin(e, f["id"], "ohne", "owner", mit_totp=False)
    with TestClient(a, base_url=BASIS) as c:
        assert passwort_login(c, "ohne").headers["location"] == "/"
        assert c.get("/geraete").status_code == 200
        con = db.verbinden(e.db_pfad)
        con.execute("UPDATE firma SET anmeldearten = 'password_totp' WHERE id = ?", (f["id"],))
        con.close()
        assert c.get("/geraete", follow_redirects=False).headers["location"] == "/login"


# --- N9: Rate-Limit je IPv6-/64, unbekannte Tokens je IP begrenzt, Aufräumen nicht bei jedem Treffer ---
def test_n9_ipv6_je_64(app, einst):
    f = neue_firma(einst, "n9")
    for i in range(20):
        c = TestClient(app, base_url=BASIS, client=(f"2001:db8::{i:x}", 1))
        c.post("/api/v1/enroll", json={"code": "0000-0000-0000-0000", "platform": "x"})
    c = TestClient(app, base_url=BASIS, client=("2001:db8::ffff", 1))
    r = c.post("/api/v1/enroll", json={"code": f["code"], "platform": "x"})
    assert r.status_code == 429


def test_n9_unbekannte_tokens_je_ip(client):
    for i in range(60):
        client.get("/api/v1/device", headers=bearer(f"wld_{i:043d}"))
    r = client.get("/api/v1/device", headers=bearer("wld_" + "x" * 43))
    assert r.status_code == 429 and r.json()["error"] == "rate_limited"


def test_n9_aufraeumen_hoechstens_einmal_je_minute():
    t = [0]
    b = Begrenzer(lambda: t[0])
    aufrufe = []
    b._aufraeumen = lambda jetzt: aufrufe.append(jetzt)
    b._treffer.update({f"k{i}": __import__("collections").deque([0]) for i in range(100_001)})
    for _ in range(50):
        b.erlaubt("neu", 1000, 1000)
    assert len(aufrufe) == 1
