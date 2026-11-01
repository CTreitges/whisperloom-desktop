"""Regressionstests zu den Befunden des Prüfer-Reviews W2-SRV2 (W2/W2-SRV2/review.md): Multipart-DoS, große Uploads,
Gesamt-Deadline und Parallelität, Modell beim Text über den Erkennungs-Zugang, thinking an Anthropic, Audio-Dauer,
SSRF-Restlücken (NAT64, 6to4, IPv4-kompatibel, site-local, 6to4-Relay).
"""

import asyncio
import json
import struct
import time

import pytest

from whisperloom_server import proxy, ssrf, upstream, vorab

from .conftest import bearer, chat, proxy_geraet, sql, stt, wav, zugang


def _aufloeser(adresse):
    return lambda _h, _p: [adresse]


# --- 1 (hoch): Multipart-Parser darf den Server nicht blockieren ---


def test_multipart_mit_sehr_vielen_teilen_wird_schnell_abgelehnt(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    grenze = "xx"
    teil = f'--{grenze}\r\nContent-Disposition: form-data; name="model"\r\n\r\na\r\n'.encode()
    body = teil * 100_000 + f"--{grenze}--\r\n".encode()
    start = time.monotonic()
    r = pclient.post("/api/v1/proxy/audio/transcriptions", content=body,
                     headers={**bearer(g["deviceToken"]), "Content-Type": f"multipart/form-data; boundary={grenze}"})
    assert r.status_code == 400 and r.json()["error"] == "invalid_request"
    assert time.monotonic() - start < 1.5
    assert fake.anfragen == []


def test_multipart_aus_zeilenumbruechen_schnell(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    start = time.monotonic()
    r = pclient.post("/api/v1/proxy/audio/transcriptions", content=b"\n" * (20 * 1024 * 1024),
                     headers={**bearer(g["deviceToken"]), "Content-Type": "multipart/form-data; boundary=xx"})
    assert r.status_code == 400 and time.monotonic() - start < 3.0


def test_multipart_datei_mit_zeilenumbruechen_bleibt_unveraendert(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    audio = wav(0.5) + b"\r\n--nicht-die-grenze\r\n\r\n" + b"\n" * 1000
    assert stt(pclient, g["deviceToken"], audio).status_code == 200
    assert fake.anfragen[0]["datei"] == len(audio)


# --- 3 (mittel): große Uploads nicht unbegrenzt parallel puffern ---


def test_zu_viele_grosse_uploads_gleichzeitig_503(pein, pclient, fake, monkeypatch):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    monkeypatch.setattr(vorab, "MAX_INSTANZ", 0)
    r = stt(pclient, g["deviceToken"], wav(3.0))  # > 64 KiB
    assert r.status_code == 503 and r.json()["error"] == "service_unavailable" and r.headers["retry-after"].isdigit()
    assert pclient.get("/healthz").status_code == 200  # kleine Anfragen laufen weiter


# --- 4 (mittel): Gesamt-Deadline und Parallelität je Firma ---


def test_tropfende_antwort_endet_an_der_gesamt_deadline(pein, pclient, fake, monkeypatch):
    monkeypatch.setattr(upstream, "lese_zeit_s", lambda _z: 0.5)
    monkeypatch.setattr(proxy, "VERBINDEN_S", 0.5)
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    fake.modus = "tropfen"
    start = time.monotonic()
    r = stt(pclient, g["deviceToken"])
    assert r.status_code == 504 and r.json()["error"] == "upstream_timeout"
    assert time.monotonic() - start < 4.0


def test_parallelitaet_je_firma_begrenzt(pein, pclient, fake, monkeypatch):
    monkeypatch.setattr(proxy, "MAX_GLEICHZEITIG_JE_FIRMA", 0)
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    r = stt(pclient, g["deviceToken"])
    assert r.status_code == 429 and r.json()["error"] == "rate_limited" and r.headers["retry-after"].isdigit()
    assert fake.anfragen == []


# --- 5 (mittel): Text über den Erkennungs-Zugang nimmt nicht dessen Erkennungsmodell ---


def test_text_ueber_erkennungszugang_nutzt_modell_der_anfrage(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "groq", fake.basis + "/v1", modell="whisper-large-v3")
    assert chat(pclient, g["deviceToken"], model="openai/gpt-oss-20b").status_code == 200
    assert fake.anfragen[0]["json"]["model"] == "openai/gpt-oss-20b"


# --- 6 (mittel): thinking des Geräts geht nicht an Anthropic ---


def test_thinking_geht_nicht_an_anthropic(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "llm", "anthropic", fake.basis + "/v1", modell="claude-opus-5-5")
    assert chat(pclient, g["deviceToken"], thinking={"type": "disabled"}).status_code == 200
    assert "thinking" not in fake.anfragen[0]["json"]


# --- 7 (mittel/niedrig): Audio-Dauer und Zählung lassen sich nicht drücken ---


def test_wav_mit_falscher_datengroesse_zaehlt_nach_nutzdaten(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    echt = wav(6.0)
    gefaelscht = echt[:40] + struct.pack("<I", 0) + echt[44:]  # data-Größe 0, Nutzdaten 6 s
    assert stt(pclient, g["deviceToken"], gefaelscht).status_code == 200
    assert sql(pein, "SELECT audio_ms FROM nutzung")[0][0] == 6000


def test_kein_wav_wird_abgelehnt(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    r = stt(pclient, g["deviceToken"], b"ID3" + b"\x00" * 50_000)  # mp3: Dauer nicht bestimmbar
    assert r.status_code == 400 and r.json()["error"] == "invalid_request"
    assert fake.anfragen == []


def test_audio_dauer_einheiten():
    assert proxy.audio_dauer_ms(wav(2.5)) == 2500
    assert proxy.audio_dauer_ms(wav(1.0, rate=8_000)) == 1000
    assert proxy.audio_dauer_ms(b"kein wav") is None


# --- 8 (niedrig): SSRF-Restlücken ---


@pytest.mark.parametrize("adresse", [
    "64:ff9b::a00:1",  # NAT64 → 10.0.0.1
    "64:ff9b::7f00:1",  # NAT64 → 127.0.0.1
    "::a00:1",  # IPv4-kompatibel
    "2002:a00:1::1",  # 6to4 mit 10.0.0.1
    "fec0::1",  # site-local (veraltet)
    "192.88.99.1",  # 6to4-Relay-Anycast
])
def test_ssrf_restluecken(adresse):
    with pytest.raises(ssrf.ZielVerboten):
        ssrf.pruefen("https://anbieter.example/v1", (), _aufloeser(adresse))


def test_ssrf_nat64_auf_oeffentliche_adresse_erlaubt():
    assert ssrf.pruefen("https://anbieter.example/v1", (), _aufloeser("64:ff9b::5db8:d822"))  # 93.184.216.34


def test_proxy_modul_parst_im_thread(monkeypatch):
    """multipart_lesen läuft nicht auf der Event-Loop (asyncio.to_thread)."""
    aufgerufen = []
    echt = asyncio.to_thread

    async def zaehlen(fn, *a, **kw):
        aufgerufen.append(fn.__name__)
        return await echt(fn, *a, **kw)

    monkeypatch.setattr(proxy.asyncio, "to_thread", zaehlen)
    body = b'--x\r\nContent-Disposition: form-data; name="file"; filename="a.wav"\r\n\r\n' + wav(0.1) + b"\r\n--x--\r\n"
    felder, datei = asyncio.run(proxy.multipart_im_thread("multipart/form-data; boundary=x", body))
    assert felder == {} and datei == wav(0.1) and "multipart_lesen" in aufgerufen
    json.dumps(felder)
