"""Contract-Tests der Proxy-Routen gegen docs/server-api.md §3.6/§3.7 (W2-SRV2): Felder der Antworten und Fehlercodes."""

import pytest

from whisperloom_server import db, upstream

from .conftest import bearer, chat, geraet_setzen, proxy_geraet, stt, zugang

MODELLE = "/api/v1/proxy/models"


def _fehler(r, status: int, code: str) -> None:
    assert r.status_code == status, (r.status_code, r.text)
    body = r.json()
    assert set(body) >= {"error", "message"} and body["error"] == code and isinstance(body["message"], str)
    assert r.headers["content-type"].startswith("application/json")


@pytest.fixture
def geraet(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "groq", fake.basis + "/v1", modell="whisper-large-v3-turbo")
    zugang(pein, f["id"], "llm", "custom", fake.basis + "/v1", modell="llama-3.3-70b")
    return f, g


def test_contract_proxy_models_200(pclient, geraet):
    r = pclient.get(MODELLE, headers=bearer(geraet[1]["deviceToken"]))
    assert r.status_code == 200
    b = r.json()
    assert b["object"] == "list" and {m["id"] for m in b["data"]} == {"whisper-large-v3-turbo", "llama-3.3-70b"}
    assert all(set(m) == {"id", "object", "owned_by"} and m["object"] == "model" for m in b["data"])


def test_contract_proxy_transcriptions_200(pclient, geraet):
    r = stt(pclient, geraet[1]["deviceToken"])
    assert r.status_code == 200 and isinstance(r.json()["text"], str)
    assert set(r.json()) <= {"text", "language", "usage"}


def test_contract_proxy_chat_200(pclient, geraet):
    r = chat(pclient, geraet[1]["deviceToken"])
    assert r.status_code == 200
    b = r.json()
    assert {"id", "object", "choices"} <= set(b) and b["object"] == "chat.completion"
    wahl = b["choices"][0]
    assert wahl["index"] == 0 and wahl["message"]["role"] == "assistant" and isinstance(wahl["message"]["content"], str)
    assert wahl["finish_reason"] in ("stop", "length")
    assert set(b["usage"]) == {"prompt_tokens", "completion_tokens", "total_tokens"}


@pytest.mark.parametrize("route", ["stt", "chat"])
def test_contract_proxy_fehler(pein, pclient, fake, geraet, monkeypatch, route):
    f, g = geraet

    def rufen():
        return stt(pclient, g["deviceToken"]) if route == "stt" else chat(pclient, g["deviceToken"])

    _fehler(stt(pclient, "wld_" + "y" * 43) if route == "stt" else chat(pclient, "wld_" + "y" * 43), 401, "device_unknown")
    geraet_setzen(pein, g["deviceId"], status="blocked")
    _fehler(rufen(), 403, "device_blocked")
    geraet_setzen(pein, g["deviceId"], status="approved")
    fake.modus = "fehler500"
    _fehler(rufen(), 502, "upstream_error")
    monkeypatch.setattr(upstream, "lese_zeit_s", lambda _z: 0.3)
    fake.modus, fake.warte_s = "langsam", 1.5
    _fehler(rufen(), 504, "upstream_timeout")
    fake.modus = "ok"
    con = db.verbinden(pein.db_pfad)
    con.execute("INSERT INTO budget (firma_id, ebene, ziel_id, max_anfragen) VALUES (?, 'firma', '', 0)", (f["id"],))
    con.close()
    _fehler(rufen(), 403, "budget_exhausted")


def test_contract_proxy_413_und_429(pein, pclient, geraet):
    g = geraet[1]
    _fehler(stt(pclient, g["deviceToken"], b"\x00" * (26 * 1024 * 1024)), 413, "payload_too_large")
    for _ in range(130):
        r = pclient.get(MODELLE, headers=bearer(g["deviceToken"]))
        if r.status_code == 429:
            break
    _fehler(r, 429, "rate_limited")
    assert r.headers["retry-after"].isdigit()
