"""Enrollment, Freigabe, Firmencode, Token-Rotation, ETag/304, Abmelden, Richtlinien-Auflösung, Key-Modi."""

import pytest

from whisperloom_server import db, firmen, krypto, richtlinie, schluessel
from whisperloom_server.katalog import Ungueltig
from whisperloom_server.konfig import ROTATION_MS

from .conftest import (
    FAKE_KEY,
    admin_client,
    bearer,
    enroll,
    freies_geraet,
    geraet_setzen,
    kry,
    neue_firma,
    seiten_csrf,
    sql,
)


def _con(einst):
    return db.verbinden(einst.db_pfad)


# --- Enrollment und Freigabe ---


def test_enrollment_pending_approved_blocked(app, einst, uhr, client):
    f = neue_firma(einst, "firma-a")
    g = enroll(client, f["code"].lower().replace("-", " "))
    assert g["state"] == "pending" and g["deviceToken"].startswith("wld_") and g["deviceId"].startswith("d_")
    h = bearer(g["deviceToken"])
    assert client.get("/api/v1/device", headers=h).json()["state"] == "pending"
    assert client.get("/api/v1/config", headers=h).json()["error"] == "device_pending"

    admin = admin_client(app, einst, uhr, f["id"], "anna")
    csrf = seiten_csrf(admin, "/geraete")
    assert admin.post(f"/geraete/{g['deviceId']}/freigeben", data={"csrf": csrf}, follow_redirects=False).status_code == 303
    assert client.get("/api/v1/device", headers=h).json()["state"] == "approved"
    assert client.get("/api/v1/config", headers=h).status_code == 200

    admin.post(f"/geraete/{g['deviceId']}/sperren", data={"csrf": csrf})
    assert client.get("/api/v1/device", headers=h).json()["state"] == "blocked"  # 200 auch gesperrt
    r = client.get("/api/v1/config", headers=h)
    assert r.status_code == 403 and r.json()["error"] == "device_blocked"
    admin.post(f"/geraete/{g['deviceId']}/loeschen", data={"csrf": csrf})
    assert client.get("/api/v1/device", headers=h).json()["error"] == "device_unknown"


def test_auto_freigabe(einst, client):
    f = neue_firma(einst, "firma-a", auto_freigabe=1)
    g = enroll(client, f["code"])
    assert g["state"] == "approved"
    assert client.get("/api/v1/config", headers=bearer(g["deviceToken"])).status_code == 200


def test_token_nur_gehasht_gespeichert(einst, client):
    f = neue_firma(einst, "firma-a")
    g = enroll(client, f["code"])
    roh = einst.db_pfad.read_bytes() + (einst.db_pfad.with_name(einst.db_pfad.name + "-wal").read_bytes()
                                         if einst.db_pfad.with_name(einst.db_pfad.name + "-wal").exists() else b"")
    assert g["deviceToken"].encode() not in roh
    assert krypto.code_normalisieren(f["code"]).encode() not in roh


def test_code_ungueltig_und_firma_gesperrt(einst, client):
    f = neue_firma(einst, "firma-a")
    r = client.post("/api/v1/enroll", json={"code": "0000-0000-0000-0000", "platform": "windows"})
    assert r.status_code == 404 and r.json()["error"] == "code_invalid"
    con = _con(einst)
    con.execute("UPDATE firma SET gesperrt = 1 WHERE id = ?", (f["id"],))
    con.close()
    r = client.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows"})
    assert r.status_code == 403 and r.json()["error"] == "tenant_blocked"


def test_zu_viele_wartende(einst, client):
    f = neue_firma(einst, "firma-a", max_wartend=2)
    enroll(client, f["code"])
    enroll(client, f["code"])
    r = client.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows"})
    assert r.status_code == 409 and r.json()["error"] == "too_many_pending"


def test_firmencode_rotieren_alter_code_ungueltig(app, einst, uhr, client):
    f = neue_firma(einst, "firma-a")
    alt = enroll(client, f["code"])
    admin = admin_client(app, einst, uhr, f["id"], "anna")
    r = admin.post("/firma/code", data={"csrf": seiten_csrf(admin, "/firma")}, follow_redirects=False)
    assert r.headers["location"] == "/firma?ok=code_neu"
    r = client.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows"})
    assert r.status_code == 404 and r.json()["error"] == "code_invalid"
    neu = firmen.code_lesen(_con(einst), kry(einst), f["id"], None)
    assert neu != f["code"] and enroll(client, neu)["state"] == "pending"
    assert client.get("/api/v1/device", headers=bearer(alt["deviceToken"])).status_code == 200  # verbundene bleiben


def test_gruppencode_ordnet_gruppe_zu(einst, client):
    f = neue_firma(einst, "firma-a")
    con = _con(einst)
    con.execute("INSERT INTO gruppe (id, firma_id, name, angelegt_ms) VALUES ('g_x', ?, 'Vertrieb', 0)", (f["id"],))
    code = krypto.code_anzeigen(firmen.code_setzen(con, kry(einst), f["id"], "g_x", 0))
    con.close()
    g = enroll(client, code)
    assert sql(einst, "SELECT gruppe_id FROM geraet WHERE id = ?", (g["deviceId"],))[0][0] == "g_x"


@pytest.mark.parametrize("body", [
    {"code": "x", "platform": "windows", "extra": 1},
    {"platform": "windows"},
    {"code": "ABCD", "platform": "x" * 21},
    {"code": "ABCD", "platform": "windows", "deviceName": "n" * 65},
])
def test_enroll_ungueltig(client, body):
    r = client.post("/api/v1/enroll", json=body)
    assert r.status_code == 400 and r.json()["error"] == "invalid_request"


# --- Token-Rotation ---


def test_token_rotation(einst, client, uhr):
    f = neue_firma(einst, "firma-a", auto_freigabe=1)
    g = enroll(client, f["code"])
    alt = bearer(g["deviceToken"])
    assert client.get("/api/v1/device", headers=alt).json()["rotateToken"] is False
    uhr.vor(ROTATION_MS + 1)
    assert client.get("/api/v1/device", headers=alt).json()["rotateToken"] is True
    neu = client.post("/api/v1/device/token", headers=alt, json={}).json()["deviceToken"]
    assert neu != g["deviceToken"]
    assert client.get("/api/v1/device", headers=alt).status_code == 200  # alt gilt bis zum ersten Gebrauch des neuen
    r = client.get("/api/v1/device", headers=bearer(neu))
    assert r.status_code == 200 and r.json()["rotateToken"] is False
    assert client.get("/api/v1/device", headers=alt).json()["error"] == "device_unknown"


def test_altes_token_hoechstens_24h(einst, client, uhr):
    f = neue_firma(einst, "firma-a", auto_freigabe=1)
    g = enroll(client, f["code"])
    alt = bearer(g["deviceToken"])
    client.post("/api/v1/device/token", headers=alt)
    uhr.vor(24 * 3600 * 1000 + 1)
    assert client.get("/api/v1/device", headers=alt).json()["error"] == "device_unknown"


def test_gesperrt_darf_nicht_rotieren(einst, client):
    f = neue_firma(einst, "firma-a")
    g = enroll(client, f["code"])
    geraet_setzen(einst, g["deviceId"], status="blocked")
    r = client.post("/api/v1/device/token", headers=bearer(g["deviceToken"]))
    assert r.status_code == 403 and r.json()["error"] == "device_blocked"


# --- ETag / 304 ---


def test_etag_304_und_neue_version_nach_aenderung(einst, client):
    f = neue_firma(einst, "firma-a")
    g = freies_geraet(client, einst, f["code"])
    h = bearer(g["deviceToken"])
    r = client.get("/api/v1/config", headers=h)
    etag = r.headers["etag"]
    assert etag == f'"{r.json()["configVersion"]}"'
    assert client.get("/api/v1/device", headers=h).json()["configVersion"] == r.json()["configVersion"]
    r2 = client.get("/api/v1/config", headers={**h, "If-None-Match": etag})
    assert r2.status_code == 304 and r2.content == b"" and r2.headers["etag"] == etag
    con = _con(einst)
    richtlinie.setzen(con, f["id"], "", "refine.stage", "default", "polish")
    con.close()
    r3 = client.get("/api/v1/config", headers={**h, "If-None-Match": etag})
    assert r3.status_code == 200 and r3.headers["etag"] != etag
    assert r3.json()["settings"]["refine"]["stage"] == "polish"
    assert r3.json()["policy"]["levels"]["refine.stage"] == "default"


# --- Abmelden ---


def test_abmelden(einst, client):
    f = neue_firma(einst, "firma-a", auto_freigabe=1)
    g = enroll(client, f["code"])
    r = client.post("/api/v1/device/leave", headers=bearer(g["deviceToken"]), json={})
    assert r.status_code == 204 and r.content == b""
    assert client.get("/api/v1/device", headers=bearer(g["deviceToken"])).status_code == 401


def test_abmelden_verboten(einst, client):
    f = neue_firma(einst, "firma-a", auto_freigabe=1)
    con = _con(einst)
    richtlinie.setzen(con, f["id"], "", "management.allowLeave", "fixed", False)
    con.close()
    g = enroll(client, f["code"])
    r = client.post("/api/v1/device/leave", headers=bearer(g["deviceToken"]), json={})
    assert r.status_code == 403 and r.json()["error"] == "leave_not_allowed"


# --- Richtlinie ---


def test_aufloesung_firma_dann_gruppe(einst, client):
    f = neue_firma(einst, "firma-a")
    con = _con(einst)
    con.execute("INSERT INTO gruppe (id, firma_id, name, angelegt_ms) VALUES ('g_v', ?, 'Vertrieb', 0)", (f["id"],))
    richtlinie.setzen(con, f["id"], "", "refine.stage", "fixed", "polish")
    richtlinie.setzen(con, f["id"], "", "history.enabled", "fixed", True)
    richtlinie.setzen(con, f["id"], "", "output.alwaysCopy", "default", True)
    richtlinie.setzen(con, f["id"], "g_v", "refine.stage", "default", "summarize")  # Gruppe überschreibt
    richtlinie.setzen(con, f["id"], "g_v", "history.enabled", "free", None)  # Gruppe gibt frei
    con.close()
    ohne = freies_geraet(client, einst, f["code"])
    mit = freies_geraet(client, einst, f["code"], gruppe_id="g_v")
    k1 = client.get("/api/v1/config", headers=bearer(ohne["deviceToken"])).json()
    k2 = client.get("/api/v1/config", headers=bearer(mit["deviceToken"])).json()
    assert k1["settings"]["refine"]["stage"] == "polish" and k1["policy"]["levels"]["refine.stage"] == "fixed"
    assert k1["policy"]["historyEnabled"] is True
    assert k2["settings"]["refine"]["stage"] == "summarize" and k2["policy"]["levels"]["refine.stage"] == "default"
    assert "history.enabled" not in k2["policy"]["levels"] and "historyEnabled" not in k2["policy"]
    assert k2["policy"]["levels"]["output.alwaysCopy"] == "default"  # geerbt


def test_tastenkuerzel_nie_fest(app, einst, uhr):
    f = neue_firma(einst, "firma-a")
    con = _con(einst)
    with pytest.raises(Ungueltig):
        richtlinie.setzen(con, f["id"], "", "desktop.hotkeyHold", "fixed", "Ctrl+Alt+Space")
    richtlinie.setzen(con, f["id"], "", "desktop.hotkeyHold", "default", "Ctrl+Alt+Space")
    # Altbestand mit „fixed“ (z. B. von Hand in der DB) geht als „default“ hinaus
    con.execute("UPDATE richtlinie SET stufe = 'fixed' WHERE pfad = 'desktop.hotkeyHold'")
    _, policy = richtlinie.als_konfig(richtlinie.aufloesen(con, f["id"], None))
    con.close()
    assert policy["levels"]["desktop.hotkeyHold"] == "default"
    admin = admin_client(app, einst, uhr, f["id"], "anna")
    r = admin.post("/richtlinie", data={"csrf": seiten_csrf(admin, "/richtlinie"), "s:desktop.hotkeyToggle": "fixed",
                                        "w:desktop.hotkeyToggle": "Ctrl+Alt+T"})
    assert r.status_code == 400 and "Tastenkürzel lassen sich nicht festlegen" in r.text
    assert sql(einst, "SELECT COUNT(*) FROM richtlinie WHERE pfad = 'desktop.hotkeyToggle'")[0][0] == 0


def test_richtlinie_im_portal_speichern(app, einst, uhr):
    f = neue_firma(einst, "firma-a")
    admin = admin_client(app, einst, uhr, f["id"], "anna")
    r = admin.post("/richtlinie", data={
        "csrf": seiten_csrf(admin, "/richtlinie"), "s:refine.stage": "fixed", "w:refine.stage": "beautify",
        "s:history.size": "default", "w:history.size": "100", "s:polish.customFillers": "fixed",
        "w:polish.customFillers": "halt\nquasi\nhalt", "s:output.alwaysCopy": "fixed",  # Checkbox fehlt = aus
    }, follow_redirects=False)
    assert r.status_code == 303
    werte = {p: w for p, _, w in sql(einst, "SELECT pfad, stufe, wert FROM richtlinie WHERE firma_id = ?", (f["id"],))}
    assert werte == {"refine.stage": '"beautify"', "history.size": "100", "polish.customFillers": '["halt", "quasi"]',
                     "output.alwaysCopy": "false"}
    r = admin.post("/richtlinie", data={"csrf": seiten_csrf(admin, "/richtlinie"), "s:history.size": "fixed",
                                        "w:history.size": "7"})
    assert r.status_code == 400  # ungültig → nichts gespeichert, auch nicht die übrigen Zeilen
    assert len(sql(einst, "SELECT * FROM richtlinie WHERE firma_id = ?", (f["id"],))) == 4


# --- Key-Modi ---


def _key_firma(einst, modus: str) -> dict:
    f = neue_firma(einst, "firma-a", key_modus=modus)
    con = _con(einst)
    schluessel.speichern(con, kry(einst), f["id"], "stt", "groq", "", FAKE_KEY, 0)
    schluessel.speichern(con, kry(einst), f["id"], "llm", "openai", "https://llm.example.com/v1", "test-key-not-real-2", 0)
    con.close()
    return f


def test_distribute_keys_nur_an_freigegebene(einst, client):
    f = _key_firma(einst, "distribute")
    g = enroll(client, f["code"])
    h = bearer(g["deviceToken"])
    for antwort in (client.get("/api/v1/config", headers=h), client.get("/api/v1/device", headers=h)):
        assert FAKE_KEY not in antwort.text  # pending: weder Konfig noch Zustand verraten Keys
    geraet_setzen(einst, g["deviceId"], status="approved")
    k = client.get("/api/v1/config", headers=h).json()
    assert k["keyMode"] == "distribute" and "proxy" not in k
    assert k["secrets"] == {"stt": FAKE_KEY, "llm": "test-key-not-real-2"}
    assert k["settings"]["stt"]["providerId"] == "groq" and k["policy"]["levels"]["stt.providerId"] == "fixed"
    assert k["settings"]["llm"]["baseUrl"] == "https://llm.example.com/v1"
    geraet_setzen(einst, g["deviceId"], status="blocked")
    r = client.get("/api/v1/config", headers=h)
    assert r.status_code == 403 and FAKE_KEY not in r.text


def test_proxy_liefert_nie_keys(einst, client):
    f = _key_firma(einst, "proxy")
    g = freies_geraet(client, einst, f["code"])
    r = client.get("/api/v1/config", headers=bearer(g["deviceToken"]))
    k = r.json()
    assert "secrets" not in k and FAKE_KEY not in r.text and "test-key-not-real-2" not in r.text
    assert k["proxy"]["baseUrl"] == "https://testserver/api/v1/proxy"
    assert k["settings"]["stt"] == {"providerId": "custom", "baseUrl": "https://testserver/api/v1/proxy"}
    assert k["policy"]["levels"]["stt.baseUrl"] == "fixed"


def test_keys_verschluesselt_in_der_db(app, einst, uhr):
    f = neue_firma(einst, "firma-a")
    admin = admin_client(app, einst, uhr, f["id"], "anna")
    geheim = "test-key-not-real-klartextsuche-0042"
    r = admin.post("/schluessel", data={"csrf": seiten_csrf(admin, "/schluessel"), "name": "stt", "anbieter": "groq",
                                        "key": geheim}, follow_redirects=False)
    assert r.headers["location"] == "/schluessel?ok=key_gespeichert"
    assert geheim not in admin.get("/schluessel").text  # nie angezeigt, nur letzte vier
    roh = b"".join(p.read_bytes() for p in einst.daten.glob("whisperloom-server.db*"))
    assert geheim.encode() not in roh and geheim[:20].encode() not in roh
    assert schluessel.lesen(_con(einst), kry(einst), f["id"], "stt") == geheim
