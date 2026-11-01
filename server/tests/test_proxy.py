"""Proxy (W2-SRV2): je Protokoll gegen lokale Fake-Anbieter, Modellwahl, Fallback, Gerätezustand, Mandanten-Trennung,
Upload-Grenze, ungültige Anfragen. Key-Lecks und Logs: test_proxy_datenschutz.py; SSRF: test_ssrf.py.
"""

import pytest

from whisperloom_server import db, upstream

from .conftest import (
    FAKE_KEY,
    FAKE_KEY_2,
    FakeUpstream,
    bearer,
    chat,
    geraet_setzen,
    proxy_geraet,
    sql,
    stt,
    wav,
    zugang,
)


def _fehler(r, status: int, code: str) -> None:
    assert r.status_code == status, (r.status_code, r.text)
    assert r.json()["error"] == code


# --- Erkennung je Protokoll ---


@pytest.mark.parametrize("anbieter,mit_prompt,mit_format", [("custom", True, True), ("groq", True, True),
                                                            ("mistral", False, False), ("openrouter", False, True)])
def test_erkennung_openai_kompatibel(pein, pclient, fake, anbieter, mit_prompt, mit_format):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", anbieter, fake.basis + "/v1")
    audio = wav(1.5)
    r = stt(pclient, g["deviceToken"], audio, prompt="Treitges, WhisperLoom")
    assert r.status_code == 200, r.text
    assert r.json() == {"text": "Hallo von haupt", "language": "de"}
    a = fake.anfragen[0]
    assert a["pfad"] == "/v1/audio/transcriptions" and a["header"]["authorization"] == f"Bearer {FAKE_KEY}"
    assert a["felder"]["model"] == ["whisper-x"] and a["felder"]["language"] == ["de"] and a["datei"] == len(audio)
    assert ("prompt" in a["felder"]) is mit_prompt and ("response_format" in a["felder"]) is mit_format
    assert g["deviceToken"] not in str(a["header"])  # Geräte-Token geht nie an den Anbieter


def test_erkennung_elevenlabs(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "elevenlabs", fake.basis + "/v1", modell="scribe_v2")
    r = stt(pclient, g["deviceToken"], prompt="Name")
    assert r.status_code == 200 and r.json() == {"text": "Hallo von haupt", "language": "deu"}
    a = fake.anfragen[0]
    assert a["pfad"] == "/v1/speech-to-text" and a["header"]["xi-api-key"] == FAKE_KEY
    assert "authorization" not in a["header"]
    assert a["felder"]["model_id"] == ["scribe_v2"] and a["felder"]["language_code"] == ["de"]
    assert "prompt" not in a["felder"]


def test_erkennung_sprache_auto_geht_nicht_an_elevenlabs(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "elevenlabs", fake.basis + "/v1", modell="scribe_v2")
    assert stt(pclient, g["deviceToken"], language="auto").status_code == 200
    assert "language_code" not in fake.anfragen[0]["felder"]


# --- Text je Protokoll ---


@pytest.mark.parametrize("anbieter,basis,pfad,kopf", [
    ("custom", "/v1", "/v1/chat/completions", "authorization"),
    ("anthropic", "/v1", "/v1/messages", "x-api-key"),
    ("gemini", "/v1beta", "/v1beta/models/text-x:generateContent", "x-goog-api-key"),
    ("ollama", "/api", "/api/chat", "authorization"),
])
def test_text_je_protokoll(pein, pclient, fake, anbieter, basis, pfad, kopf):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "llm", anbieter, fake.basis + basis)
    r = chat(pclient, g["deviceToken"])
    assert r.status_code == 200, r.text
    b = r.json()
    assert b["object"] == "chat.completion" and b["choices"][0]["message"] == {"role": "assistant", "content": "Hallo von haupt"}
    assert b["choices"][0]["finish_reason"] == "stop" and b["usage"]["total_tokens"] > 0
    assert "reasoning" not in r.text  # nur normalisierte Felder gehen an das Gerät
    a = fake.anfragen[0]
    assert a["pfad"] == pfad and FAKE_KEY in a["header"][kopf]
    js = a["json"]
    if anbieter == "anthropic":
        assert a["header"]["anthropic-version"] == upstream.ANTHROPIC_VERSION
        assert js["system"] == "Glätte den Text." and js["messages"] == [{"role": "user", "content": "hallo welt"}]
        assert "temperature" not in js and js["max_tokens"] == upstream.MAX_TOKENS
    elif anbieter == "gemini":
        assert js["systemInstruction"] == {"parts": [{"text": "Glätte den Text."}]}
        assert js["contents"] == [{"role": "user", "parts": [{"text": "hallo welt"}]}]
    elif anbieter == "ollama":
        assert js["stream"] is False and js["model"] == "text-x" and js["options"] == {"temperature": 0}
    else:
        assert js["model"] == "text-x" and js["temperature"] == 0 and len(js["messages"]) == 2


def test_text_ohne_llm_zugang_nutzt_erkennungszugang(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "groq", fake.basis + "/v1")
    assert chat(pclient, g["deviceToken"]).status_code == 200
    assert fake.anfragen[0]["pfad"] == "/v1/chat/completions"


def test_elevenlabs_kann_keinen_text(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "elevenlabs", fake.basis + "/v1")
    _fehler(chat(pclient, g["deviceToken"]), 404, "not_found")
    assert fake.anfragen == []


def test_modell_des_zugangs_gewinnt(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1", modell="firmen-whisper")
    zugang(pein, f["id"], "llm", "custom", fake.basis + "/v1", modell="firmen-llm")
    assert stt(pclient, g["deviceToken"], model="teures-modell").status_code == 200
    assert chat(pclient, g["deviceToken"], model="teures-modell").status_code == 200
    assert fake.anfragen[0]["felder"]["model"] == ["firmen-whisper"] and fake.anfragen[1]["json"]["model"] == "firmen-llm"
    r = pclient.get("/api/v1/proxy/models", headers=bearer(g["deviceToken"]))
    assert r.json()["data"] == [{"id": "firmen-llm", "object": "model", "owned_by": "custom"},
                                {"id": "firmen-whisper", "object": "model", "owned_by": "custom"}]


def test_zugang_ohne_key_eigener_server(pein, pclient, fake):
    fake.key = ""
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1", key="")
    assert stt(pclient, g["deviceToken"]).status_code == 200
    assert "authorization" not in fake.anfragen[0]["header"]


def test_temperature_abgelehnt_ein_zweiter_versuch(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "llm", "openai", fake.basis + "/v1")
    fake.modus = "temperatur400"
    r = chat(pclient, g["deviceToken"], reasoning_effort="none")
    assert r.status_code == 200, r.text
    assert len(fake.anfragen) == 2
    assert "temperature" in fake.anfragen[0]["json"] and "temperature" not in fake.anfragen[1]["json"]
    assert "reasoning_effort" not in fake.anfragen[1]["json"]


# --- Fallback ---


@pytest.mark.parametrize("modus", ["fehler500", "kaputt", "falscher_key"])
def test_fallback_bei_fehler(pein, pclient, fake, fake2, modus):
    f, g = proxy_geraet(pclient, pein)
    key = "test-key-not-real-falsch" if modus == "falscher_key" else FAKE_KEY
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1", key=key)
    zugang(pein, f["id"], "stt_fallback", "custom", fake2.basis + "/v1", key=FAKE_KEY_2)
    if modus != "falscher_key":
        fake.modus = modus
    r = stt(pclient, g["deviceToken"])
    assert r.status_code == 200 and r.json()["text"] == "Hallo von ersatz"
    assert len(fake.anfragen) == 1 and fake2.anfragen[0]["header"]["authorization"] == f"Bearer {FAKE_KEY_2}"


def test_fallback_bei_zeitlimit(pein, pclient, fake, fake2, monkeypatch):
    monkeypatch.setattr(upstream, "lese_zeit_s", lambda _z: 0.3)
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "llm", "custom", fake.basis + "/v1")
    zugang(pein, f["id"], "llm_fallback", "custom", fake2.basis + "/v1", key=FAKE_KEY_2)
    fake.modus, fake.warte_s = "langsam", 1.5
    r = chat(pclient, g["deviceToken"])
    assert r.status_code == 200 and r.json()["choices"][0]["message"]["content"] == "Hallo von ersatz"


def test_ohne_ersatz_zeitlimit_504_und_fehler_502(pein, pclient, fake, monkeypatch):
    monkeypatch.setattr(upstream, "lese_zeit_s", lambda _z: 0.3)
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    fake.modus, fake.warte_s = "langsam", 1.5
    _fehler(stt(pclient, g["deviceToken"]), 504, "upstream_timeout")
    fake.modus = "fehler500"
    _fehler(stt(pclient, g["deviceToken"]), 502, "upstream_error")


def test_kein_fallback_bei_400(pein, pclient, fake, fake2):
    """400 heißt: die Anfrage selbst passt nicht – der Ersatz bekäme dieselbe (und kostet nur)."""
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "llm", "custom", fake.basis + "/v1")
    zugang(pein, f["id"], "llm_fallback", "custom", fake2.basis + "/v1", key=FAKE_KEY_2)
    fake.modus = "temperatur400"
    r = chat(pclient, g["deviceToken"], temperature=1, reasoning_effort="x")
    assert r.status_code == 200  # zweiter Versuch ohne die Felder klappt
    fake.anfragen.clear()
    fake.modus = "weiterleitung"
    _fehler(chat(pclient, g["deviceToken"]), 502, "upstream_error")
    assert fake2.anfragen == []


def test_verbindungsfehler_nimmt_ersatz(pein, pclient, fake2):
    tot = FakeUpstream(FAKE_KEY, "tot")
    adresse = tot.basis
    tot.server_close()  # Port ist danach frei: Verbindung abgelehnt
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", adresse + "/v1")
    zugang(pein, f["id"], "stt_fallback", "custom", fake2.basis + "/v1", key=FAKE_KEY_2)
    assert stt(pclient, g["deviceToken"]).json()["text"] == "Hallo von ersatz"


# --- Gerätezustand, Modus, fehlende Zugänge ---


@pytest.mark.parametrize("route", ["stt", "chat", "models"])
def test_geraetezustand(pein, pclient, fake, route):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")

    def rufen(token=g["deviceToken"]):
        if route == "stt":
            return stt(pclient, token)
        if route == "chat":
            return chat(pclient, token)
        return pclient.get("/api/v1/proxy/models", headers=bearer(token))

    _fehler(rufen("wld_" + "x" * 43), 401, "device_unknown")
    geraet_setzen(pein, g["deviceId"], status="pending")
    _fehler(rufen(), 403, "device_pending")
    geraet_setzen(pein, g["deviceId"], status="blocked")
    _fehler(rufen(), 403, "device_blocked")
    geraet_setzen(pein, g["deviceId"], status="approved")
    con = db.verbinden(pein.db_pfad)
    con.execute("UPDATE firma SET gesperrt = 1 WHERE id = ?", (f["id"],))
    _fehler(rufen(), 403, "tenant_blocked")
    con.execute("UPDATE firma SET gesperrt = 0, key_modus = 'distribute' WHERE id = ?", (f["id"],))
    con.close()
    _fehler(rufen(), 404, "not_found")
    assert fake.anfragen == []


def test_ohne_zugang_not_found(pein, pclient):
    _f, g = proxy_geraet(pclient, pein)
    _fehler(stt(pclient, g["deviceToken"]), 404, "not_found")
    _fehler(chat(pclient, g["deviceToken"]), 404, "not_found")


# --- Mandanten-Trennung ---


def test_geraet_nutzt_nur_keys_der_eigenen_firma(pein, pclient):
    key_a, key_b = "test-key-not-real-a-1111", "test-key-not-real-b-2222"
    up_a, up_b = FakeUpstream(key_a, "a").starten(), FakeUpstream(key_b, "b").starten()
    try:
        fa, ga = proxy_geraet(pclient, pein, "firma-a")
        fb, gb = proxy_geraet(pclient, pein, "firma-b")
        for fid, up, key in ((fa["id"], up_a, key_a), (fb["id"], up_b, key_b)):
            zugang(pein, fid, "stt", "custom", up.basis + "/v1", key=key)
            zugang(pein, fid, "llm", "custom", up.basis + "/v1", key=key)
        assert stt(pclient, ga["deviceToken"]).json()["text"] == "Hallo von a"
        assert chat(pclient, ga["deviceToken"]).json()["choices"][0]["message"]["content"] == "Hallo von a"
        assert up_b.anfragen == [] and all(key_a in a["header"]["authorization"] for a in up_a.anfragen)
        n_a = len(up_a.anfragen)
        assert stt(pclient, gb["deviceToken"]).json()["text"] == "Hallo von b"
        assert len(up_a.anfragen) == n_a and key_b in up_b.anfragen[0]["header"]["authorization"]
        # Firma B ohne Text-Zugang: B fällt nie auf die Zugänge von A zurück
        con = db.verbinden(pein.db_pfad)
        con.execute("DELETE FROM schluessel WHERE firma_id = ?", (fb["id"],))
        con.close()
        _fehler(chat(pclient, gb["deviceToken"]), 404, "not_found")
        assert len(up_a.anfragen) == n_a
        # Zähler landen bei der eigenen Firma
        assert sql(pein, "SELECT COUNT(*) FROM nutzung WHERE firma_id = ?", (fb["id"],))[0][0] == 1
    finally:
        up_a.stoppen()
        up_b.stoppen()


# --- Upload-Grenze und ungültige Anfragen ---


def test_upload_grenze(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    _fehler(stt(pclient, g["deviceToken"], b"\x00" * (25 * 1024 * 1024 + 70 * 1024)), 413, "payload_too_large")
    r = stt(pclient, g["deviceToken"], wav(60.0))  # knapp 2 MB: erlaubt
    assert r.status_code == 200
    _fehler(chat(pclient, g["deviceToken"], text="x" * (1024 * 1024 + 10)), 413, "payload_too_large")
    # Übrige API bleibt bei 64 KiB
    _fehler(pclient.post("/api/v1/enroll", content=b"{" + b" " * 70_000 + b"}",
                         headers={"Content-Type": "application/json"}), 413, "payload_too_large")
    assert len(fake.anfragen) == 1


@pytest.mark.parametrize("fall", ["unbekanntes_feld", "stream", "leer", "rolle", "kein_json"])
def test_chat_ungueltig(pein, pclient, fake, fall):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "llm", "custom", fake.basis + "/v1")
    h = bearer(g["deviceToken"])
    url = "/api/v1/proxy/chat/completions"
    nachricht = [{"role": "user", "content": "x"}]
    r = {
        "unbekanntes_feld": lambda: pclient.post(url, headers=h, json={"messages": nachricht, "tools": []}),
        "stream": lambda: pclient.post(url, headers=h, json={"messages": nachricht, "stream": True}),
        "leer": lambda: pclient.post(url, headers=h, json={"messages": []}),
        "rolle": lambda: pclient.post(url, headers=h, json={"messages": [{"role": "tool", "content": "x"}]}),
        "kein_json": lambda: pclient.post(url, headers={**h, "Content-Type": "application/json"}, content=b"{kaputt"),
    }[fall]()
    _fehler(r, 400, "invalid_request")
    assert fake.anfragen == []
    assert tuple(sql(pein, "SELECT klasse, anzahl FROM nutzung_fehler")[0]) == ("invalid_request", 1)


def test_erkennung_ungueltig(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    h = bearer(g["deviceToken"])
    url = "/api/v1/proxy/audio/transcriptions"
    _fehler(pclient.post(url, headers=h, data={"model": "x"}), 400, "invalid_request")  # ohne Datei
    _fehler(stt(pclient, g["deviceToken"], timestamp_granularities="word"), 400, "invalid_request")
    _fehler(stt(pclient, g["deviceToken"], b""), 400, "invalid_request")
    assert fake.anfragen == []
