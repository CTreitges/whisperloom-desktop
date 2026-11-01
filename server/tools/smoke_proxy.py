"""Proxy-Teil des Server-Smokes (`smoke_server.py --proxy`, W2-SRV2). Nur stdlib.

Ablauf: Zugänge stt/llm im Portal auf den lokalen Fake-Anbieter (fake_upstream.py) setzen → Gerät erkennt (Multipart)
und verbessert Text über den Proxy → Modellliste → Nutzung im Portal und als CSV → Budget der Firma setzen → nächste
Anfrage 403 budget_exhausted, Budget-Seite zeigt „Erreicht“.
"""

import json
import re
import uuid
import wave
from io import BytesIO

FAKE_KEY = "test-key-not-real-1"


def _wav(sekunden: float = 1.0) -> bytes:
    puffer = BytesIO()
    with wave.open(puffer, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(16_000)
        w.writeframes(b"\x00\x00" * int(16_000 * sekunden))
    return puffer.getvalue()


def _multipart(felder: dict[str, str], datei: bytes) -> tuple[bytes, str]:
    grenze = "----smoke" + uuid.uuid4().hex
    teile = [f'--{grenze}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode() for k, v in felder.items()]
    teile.append(f'--{grenze}\r\nContent-Disposition: form-data; name="file"; filename="audio.wav"\r\n'
                 "Content-Type: audio/wav\r\n\r\n".encode() + datei + b"\r\n")
    teile.append(f"--{grenze}--\r\n".encode())
    return b"".join(teile), f"multipart/form-data; boundary={grenze}"


def pruefen(portal, api, token: str, fake_basis: str, pruefe, csrf) -> None:
    h = {"Authorization": "Bearer " + token}
    for zugang, modell in (("stt", "fake-whisper"), ("llm", "fake-llm")):
        _, _, html = portal.anfrage("GET", "/schluessel")
        st, kopf, _ = portal.anfrage("POST", "/schluessel", {
            "csrf": csrf(html), "name": zugang, "anbieter": "custom", "basis_url": fake_basis + "/v1", "modell": modell,
            "key": FAKE_KEY})
        pruefe(st == 303 and "ok=key_gespeichert" in kopf["location"], f"Zugang {zugang} im Portal gespeichert")

    st, kopf, body = api.anfrage("GET", "/api/v1/config", headers=h)
    k = json.loads(body)
    pruefe(k["settings"]["stt"]["model"] == "fake-whisper" and k["settings"]["stt"]["baseUrl"].endswith("/api/v1/proxy"),
           "Konfiguration zeigt auf den Proxy mit festem Modell")

    roh, typ = _multipart({"model": "egal", "language": "de", "response_format": "json"}, _wav(1.5))
    st, _, body = api.anfrage("POST", "/api/v1/proxy/audio/transcriptions", daten=roh, headers={**h, "Content-Type": typ})
    pruefe(st == 200 and json.loads(body)["text"] == "Hallo von smoke-fake", "Erkennung über den Proxy")
    st, _, body = api.anfrage("POST", "/api/v1/proxy/chat/completions", headers=h, json_body={
        "model": "egal", "messages": [{"role": "system", "content": "Glätten"}, {"role": "user", "content": "hallo"}],
        "temperature": 0})
    antwort = json.loads(body)
    pruefe(st == 200 and antwort["choices"][0]["message"]["content"] == "Hallo von smoke-fake"
           and antwort["usage"]["total_tokens"] == 18, "Text-Stufe über den Proxy (mit usage)")
    st, _, body = api.anfrage("GET", "/api/v1/proxy/models", headers=h)
    pruefe(st == 200 and {m["id"] for m in json.loads(body)["data"]} == {"fake-whisper", "fake-llm"}, "Modellliste")
    pruefe(FAKE_KEY.encode() not in body, "Key nicht in der Antwort")

    st, _, html = portal.anfrage("GET", "/nutzung")
    pruefe(st == 200 and re.search(rb'<dd>2</dd>', html) is not None, "Portal „Nutzung“ zählt 2 Anfragen")
    st, kopf, csv = portal.anfrage("GET", "/nutzung.csv")
    pruefe(st == 200 and kopf["content-type"].startswith("text/csv") and b"device" in csv, "CSV-Export")

    _, _, html = portal.anfrage("GET", "/budget")
    st, kopf, _ = portal.anfrage("POST", "/budget", {"csrf": csrf(html), "ebene": "firma", "max_anfragen": "2",
                                                     "warn_prozent": "80"})
    pruefe(st == 303 and "ok=gespeichert" in kopf["location"], "Budget der Firma gesetzt (2 Anfragen)")
    st, _, body = api.anfrage("POST", "/api/v1/proxy/chat/completions", headers=h, json_body={
        "messages": [{"role": "user", "content": "noch eins"}]})
    pruefe(st == 403 and json.loads(body)["error"] == "budget_exhausted", "Budget erreicht → 403 budget_exhausted")
    st, _, html = portal.anfrage("GET", "/budget")
    pruefe(st == 200 and b"Erreicht" in html, "Portal „Budget“ zeigt „Erreicht“")
