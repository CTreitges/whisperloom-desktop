"""Contract-Tests gegen docs/server-api.md (§1.2 Fehlerformat, §3.1–3.5 Routen): Statuscodes und Felder.

ROUTEN spiegelt die Überschriften „### 3.x `METHODE /pfad`“ des Vertrags. test_contract_routen_wie_doku prüft, dass
beide übereinstimmen (Doku aus docs/server-api.md im Repo oder per WL_SERVER_API_MD). Die Proxy-Routen §3.6/§3.7
(W2-SRV2) prüft test_contract_proxy.py.
"""

import os
import re
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from whisperloom_server import db, richtlinie, schluessel
from whisperloom_server.app import create_app

from .conftest import BASIS, FAKE_KEY, bearer, enroll, geraet_setzen, kry, neue_einst, neue_firma

ROUTEN = {
    ("POST", "/api/v1/enroll"),
    ("GET", "/api/v1/device"),
    ("GET", "/api/v1/config"),
    ("POST", "/api/v1/device/token"),
    ("POST", "/api/v1/device/leave"),
}
PROXY = {
    ("GET", "/api/v1/proxy/models"),
    ("POST", "/api/v1/proxy/audio/transcriptions"),
    ("POST", "/api/v1/proxy/chat/completions"),
}
_TOKEN = re.compile(r"^wld_[A-Za-z0-9_-]{43}$")
_GERAET = re.compile(r"^d_[0-9a-z]{12}$")


def _fehler(r, status: int, code: str) -> None:
    assert r.status_code == status, (r.status_code, r.text)
    body = r.json()
    assert set(body) >= {"error", "message"} and body["error"] == code and isinstance(body["message"], str)
    assert r.headers["content-type"].startswith("application/json")


def _doku() -> Path | None:
    pfad = os.environ.get("WL_SERVER_API_MD")
    kandidat = Path(pfad) if pfad else Path(__file__).resolve().parents[2] / "docs" / "server-api.md"
    return kandidat if kandidat.exists() else None


def test_contract_routen_wie_doku():
    doku = _doku()
    if doku is None:
        pytest.skip("docs/server-api.md fehlt (kommt mit W2-C0); WL_SERVER_API_MD setzen")
    text = doku.read_text(encoding="utf-8")
    gefunden = set()
    for kopf in re.findall(r"^### 3\.\d+ (.+)$", text, flags=re.M):
        for methode, pfad in re.findall(r"`(GET|POST|PUT|DELETE|PATCH) (/api/v1/[^`]+)`", kopf):
            gefunden.add((methode, pfad))
    assert gefunden == ROUTEN | PROXY


# --- §1.2 Fehlerformat und §1.1 Anmeldung ---


@pytest.mark.parametrize("methode,pfad", sorted((ROUTEN | PROXY) - {("POST", "/api/v1/enroll")}))
def test_contract_ohne_oder_falsches_token_401(client, methode, pfad):
    _fehler(client.request(methode, pfad), 401, "device_unknown")
    _fehler(client.request(methode, pfad, headers=bearer("wld_" + "x" * 43)), 401, "device_unknown")


# --- §3.1 POST /api/v1/enroll ---


def test_contract_enroll_201(einst, client):
    f = neue_firma(einst, "muster", "Muster GmbH")
    r = client.post("/api/v1/enroll", json={"code": f["code"], "deviceName": "PC-Buchhaltung-3", "platform": "windows",
                                            "appVersion": "1.0.0"})
    assert r.status_code == 201
    b = r.json()
    assert set(b) == {"deviceId", "deviceToken", "state", "tenantName", "pollAfterSec"}
    assert _GERAET.match(b["deviceId"]) and _TOKEN.match(b["deviceToken"])
    assert b["state"] == "pending" and b["tenantName"] == "Muster GmbH" and isinstance(b["pollAfterSec"], int)


def test_contract_enroll_fehler(einst, client, tmp_path, uhr):
    f = neue_firma(einst, "muster", max_wartend=1)
    _fehler(client.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows", "neu": 1}), 400, "invalid_request")
    _fehler(client.post("/api/v1/enroll", json={"code": "ZZZZ-ZZZZ-ZZZZ-ZZZZ", "platform": "windows"}), 404, "code_invalid")
    enroll(client, f["code"])
    _fehler(client.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows"}), 409, "too_many_pending")
    con = db.verbinden(einst.db_pfad)
    con.execute("UPDATE firma SET gesperrt = 1")
    con.close()
    _fehler(client.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows"}), 403, "tenant_blocked")
    for _ in range(20):
        client.post("/api/v1/enroll", json={"code": "x", "platform": "y"})
    r = client.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows"})
    _fehler(r, 429, "rate_limited")
    assert r.headers["retry-after"].isdigit()


def test_contract_enroll_client_outdated(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, min_app_version="1.2.0")
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "muster")
        r = c.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows", "appVersion": "1.1.9"})
        _fehler(r, 426, "client_outdated")
        assert r.json()["minAppVersion"] == "1.2.0"
        assert c.post("/api/v1/enroll", json={"code": f["code"], "platform": "windows", "appVersion": "1.2.0"}).status_code == 201


# --- §3.2 GET /api/v1/device ---


def test_contract_device_200(einst, client):
    f = neue_firma(einst, "muster", "Muster GmbH")
    g = enroll(client, f["code"], name="PC-1")
    for status in ("pending", "approved", "blocked"):
        geraet_setzen(einst, g["deviceId"], status=status)
        r = client.get("/api/v1/device", headers=bearer(g["deviceToken"]))
        assert r.status_code == 200
        b = r.json()
        assert set(b) == {"deviceId", "deviceName", "state", "tenantName", "configVersion", "rotateToken", "pollAfterSec"}
        assert b["state"] == status and b["deviceName"] == "PC-1" and b["configVersion"].startswith("c_")
        assert isinstance(b["rotateToken"], bool)


# --- §3.3 GET /api/v1/config ---


def _firma_mit_keys(einst, modus: str) -> dict:
    f = neue_firma(einst, "muster", "Muster GmbH", key_modus=modus)
    con = db.verbinden(einst.db_pfad)
    schluessel.speichern(con, kry(einst), f["id"], "groq", "groq", "", FAKE_KEY, 0)
    richtlinie.setzen(con, f["id"], "", "refine.stage", "default", "polish")
    richtlinie.setzen(con, f["id"], "", "management.allowLeave", "fixed", False)
    con.close()
    return f


@pytest.mark.parametrize("modus", ["proxy", "distribute"])
def test_contract_config_200_und_304(einst, client, modus):
    f = _firma_mit_keys(einst, modus)
    g = enroll(client, f["code"])
    geraet_setzen(einst, g["deviceId"], status="approved")
    h = bearer(g["deviceToken"])
    r = client.get("/api/v1/config", headers=h)
    assert r.status_code == 200
    b = r.json()
    pflicht = {"configVersion", "tenantName", "keyMode", "syncIntervalSec", "rotateToken", "settings", "policy"}
    assert pflicht <= set(b)
    assert r.headers["etag"] == f'"{b["configVersion"]}"' and b["keyMode"] == modus
    assert b["settings"]["schemaVersion"] == 1 and b["settings"]["management"] == {"allowLeave": False}
    assert b["policy"]["levels"]["refine.stage"] == "default" and b["policy"]["levels"]["management.allowLeave"] == "fixed"
    assert all(v in ("fixed", "default") for v in b["policy"]["levels"].values())
    if modus == "proxy":
        assert "secrets" not in b and b["proxy"]["baseUrl"].endswith("/api/v1/proxy")
    else:
        assert "proxy" not in b and b["secrets"] == {"groq": FAKE_KEY}
    r = client.get("/api/v1/config", headers={**h, "If-None-Match": r.headers["etag"]})
    assert r.status_code == 304 and r.content == b""


def test_contract_config_fehler(einst, client):
    f = neue_firma(einst, "muster")
    g = enroll(client, f["code"])
    h = bearer(g["deviceToken"])
    _fehler(client.get("/api/v1/config", headers=h), 403, "device_pending")
    geraet_setzen(einst, g["deviceId"], status="blocked")
    _fehler(client.get("/api/v1/config", headers=h), 403, "device_blocked")
    geraet_setzen(einst, g["deviceId"], status="approved")
    con = db.verbinden(einst.db_pfad)
    con.execute("UPDATE firma SET gesperrt = 1")
    con.close()
    _fehler(client.get("/api/v1/config", headers=h), 403, "tenant_blocked")


# --- §3.4 POST /api/v1/device/token ---


def test_contract_token_200_und_fehler(einst, client):
    f = neue_firma(einst, "muster")
    g = enroll(client, f["code"])
    h = bearer(g["deviceToken"])
    r = client.post("/api/v1/device/token", headers=h)
    assert r.status_code == 200 and set(r.json()) == {"deviceToken"} and _TOKEN.match(r.json()["deviceToken"])
    assert client.post("/api/v1/device/token", headers=h, json={}).status_code == 200
    _fehler(client.post("/api/v1/device/token", headers=h, json={"x": 1}), 400, "invalid_request")
    geraet_setzen(einst, g["deviceId"], status="blocked")
    _fehler(client.post("/api/v1/device/token", headers=h), 403, "device_blocked")


# --- §3.5 POST /api/v1/device/leave ---


def test_contract_leave_204_und_fehler(einst, client):
    f = _firma_mit_keys(einst, "proxy")
    g = enroll(client, f["code"])
    _fehler(client.post("/api/v1/device/leave", headers=bearer(g["deviceToken"]), json={}), 403, "leave_not_allowed")
    con = db.verbinden(einst.db_pfad)
    richtlinie.loeschen(con, f["id"], "", "management.allowLeave")
    con.close()
    _fehler(client.post("/api/v1/device/leave", headers=bearer(g["deviceToken"]), json={"x": 1}), 400, "invalid_request")
    r = client.post("/api/v1/device/leave", headers=bearer(g["deviceToken"]), json={})
    assert r.status_code == 204 and r.content == b""
    _fehler(client.post("/api/v1/device/leave", headers=bearer(g["deviceToken"]), json={}), 401, "device_unknown")
