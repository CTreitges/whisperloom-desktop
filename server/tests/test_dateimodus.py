"""Datei-Modus: YAML und JSON, Env-Referenzen statt Klartext-Keys, Neuladen bei Änderung, Fehler behält den Stand."""

import json
import os
import time
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from whisperloom_server import dateimodus
from whisperloom_server.app import create_app

from .conftest import BASIS, bearer, enroll, kry, neue_einst, sql

YAML = """
tenants:
  - slug: muster
    name: Muster GmbH
    code: ABCD-EFGH-JKMN-PQRS
    keyMode: distribute
    autoApprove: true
    policy:
      refine.stage: {level: fixed, value: polish}
      history.size: {level: default, value: 100}
    groups:
      - name: Vertrieb
        code: env:WL_TEST_GRUPPENCODE
        policy:
          refine.stage: {level: default, value: summarize}
    keys:
      stt: {provider: groq, key: env:WL_TEST_STT_KEY}
"""


@pytest.fixture
def datei(tmp_path, monkeypatch):
    monkeypatch.setenv("WL_TEST_STT_KEY", "test-key-not-real-env-1")
    monkeypatch.setenv("WL_TEST_GRUPPENCODE", "VVVV-WWWW-XXXX-YYYY")
    p = tmp_path / "firmen.yaml"
    p.write_text(YAML, encoding="utf-8")
    return p


@pytest.fixture
def datei_einst(tmp_path, uhr, datei):
    return neue_einst(tmp_path, uhr, anmeldearten=("file",), konfig_datei=datei)


def test_yaml_mit_env_referenzen(datei_einst):
    with TestClient(create_app(datei_einst), base_url=BASIS) as c:
        g = enroll(c, "ABCD-EFGH-JKMN-PQRS")
        assert g["state"] == "approved" and g["tenantName"] == "Muster GmbH"
        k = c.get("/api/v1/config", headers=bearer(g["deviceToken"])).json()
        assert k["secrets"] == {"stt": "test-key-not-real-env-1"}
        assert k["settings"]["refine"]["stage"] == "polish" and k["policy"]["levels"]["history.size"] == "default"
        v = enroll(c, "vvvv wwww xxxx yyyy")
        kv = c.get("/api/v1/config", headers=bearer(v["deviceToken"])).json()
        assert kv["settings"]["refine"]["stage"] == "summarize" and kv["policy"]["levels"]["refine.stage"] == "default"
    roh = b"".join(p.read_bytes() for p in datei_einst.daten.glob("whisperloom-server.db*"))
    assert b"test-key-not-real-env-1" not in roh


def test_json_gleichwertig(tmp_path, uhr):
    p = tmp_path / "firmen.json"
    p.write_text(json.dumps({"tenants": [{"slug": "json-firma", "name": "JSON AG", "code": "ABCD-EFGH-JKMN-PQRS"}]}),
                 encoding="utf-8")
    einst = neue_einst(tmp_path, uhr, anmeldearten=("file",), konfig_datei=p)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        assert enroll(c, "ABCD-EFGH-JKMN-PQRS")["state"] == "pending"


def test_aenderung_wird_neu_gelesen(datei_einst, datei):
    app = create_app(datei_einst)
    beob = app.state.datei
    assert beob.pruefen() is False  # unverändert
    datei.write_text(YAML.replace("value: polish", "value: beautify"), encoding="utf-8")
    os.utime(datei, (time.time() + 5, time.time() + 5))
    assert beob.pruefen() is True
    assert sql(datei_einst, "SELECT wert FROM richtlinie WHERE pfad = 'refine.stage' AND gruppe_id = ''")[0][0] == '"beautify"'


def test_fehler_in_datei_behaelt_stand(datei_einst, datei, monkeypatch):
    app = create_app(datei_einst)
    vorher = sql(datei_einst, "SELECT * FROM richtlinie ORDER BY pfad")
    for kaputt in (YAML.replace("level: fixed", "level: gesperrt"),
                   YAML.replace("env:WL_TEST_STT_KEY", "env:WL_FEHLT_GANZ"),
                   YAML.replace("history.size", "desktop.hotkeyHold").replace("level: default, value: 100",
                                                                              "level: fixed, value: Ctrl+X"),
                   "tenants: [kaputt"):
        datei.write_text(kaputt, encoding="utf-8")
        assert app.state.datei.laden() is False
        assert sql(datei_einst, "SELECT * FROM richtlinie ORDER BY pfad") == vorher


def test_fehlende_firma_wird_gesperrt(datei_einst, datei):
    app = create_app(datei_einst)
    datei.write_text("tenants: []\n", encoding="utf-8")
    assert app.state.datei.laden() is True
    assert sql(datei_einst, "SELECT gesperrt FROM firma WHERE slug = 'muster'")[0][0] == 1
    with TestClient(app, base_url=BASIS) as c:
        r = c.post("/api/v1/enroll", json={"code": "ABCD-EFGH-JKMN-PQRS", "platform": "linux"})
        assert r.json()["error"] == "tenant_blocked"


def test_start_mit_kaputter_datei_bricht_ab(tmp_path, uhr):
    p = tmp_path / "firmen.yaml"
    p.write_text("tenants:\n  - slug: x\n", encoding="utf-8")
    einst = neue_einst(tmp_path, uhr, anmeldearten=("file",), konfig_datei=p)
    with pytest.raises(dateimodus.DateiFehler):
        create_app(einst)


def test_beispieldatei_aus_readme_gueltig(tmp_path, uhr, monkeypatch):
    for name, wert in {"WL_CODE_MUSTER": "ABCD-EFGH-JKMN-PQRS", "WL_CODE_VERTRIEB": "VVVV-WWWW-XXXX-YYYY",
                       "WL_KEY_GROQ": "test-key-not-real-3", "WL_KEY_LLM": "test-key-not-real-4"}.items():
        monkeypatch.setenv(name, wert)
    beispiel = Path(__file__).parent.parent / "beispiele" / "firmen.yaml"
    einst = neue_einst(tmp_path, uhr, anmeldearten=("file",), konfig_datei=beispiel)
    assert dateimodus.anwenden(einst, kry(einst), dateimodus.lesen(beispiel)) == 1


def test_unbekannte_felder_abgelehnt(datei_einst, datei):
    with pytest.raises(dateimodus.DateiFehler):
        dateimodus.anwenden(datei_einst, kry(datei_einst), [{"slug": "neu-firma", "password": "x"}])
