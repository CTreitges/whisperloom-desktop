"""Proxy-Datenschutz (W2-SRV2): Der Anbieter-Key erscheint nie in einer Antwort, im Log oder als Klartext in der DB;
Diktat-Inhalte (Audio, Text, Prompt) und Geräte-Token landen in keinem Log und keiner DB-Datei.
"""

import logging
from pathlib import Path

import pytest

from .conftest import FAKE_KEY, bearer, chat, proxy_geraet, stt, wav, zugang

GEHEIM_TEXT = "GEHEIMER-DIKTATTEXT-4711"
GEHEIM_PROMPT = "GEHEIMES-VOKABULAR-0815"
FALSCHER_KEY = "test-key-not-real-falsch-9999"


class _Sammler(logging.Handler):
    def __init__(self):
        super().__init__(logging.DEBUG)
        self.zeilen: list[str] = []

    def emit(self, record):
        self.zeilen.append(record.getMessage())


@pytest.fixture
def logs():
    """Fängt alles ab, was irgendein Logger schreibt (auch httpx/httpcore auf DEBUG und den eigenen ohne propagate)."""
    sammler = _Sammler()
    namen = ["", "whisperloom_server", "httpx", "httpcore", "uvicorn", "fastapi"]
    alt = {n: logging.getLogger(n).level for n in namen}
    for n in namen:
        logging.getLogger(n).addHandler(sammler)
        logging.getLogger(n).setLevel(logging.DEBUG)
    yield sammler.zeilen
    for n in namen:
        logging.getLogger(n).removeHandler(sammler)
        logging.getLogger(n).setLevel(alt[n])


def _dateien(pfad: Path) -> bytes:
    return b"".join(p.read_bytes() for p in pfad.rglob("*") if p.is_file())


def test_key_nie_in_antwort_log_oder_db(pein, pclient, fake, logs):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1", modell="m1")
    zugang(pein, f["id"], "llm", "anthropic", fake.basis + "/v1", key=FALSCHER_KEY)
    antworten = [stt(pclient, g["deviceToken"]), chat(pclient, g["deviceToken"]),
                 pclient.get("/api/v1/proxy/models", headers=bearer(g["deviceToken"])),
                 pclient.get("/api/v1/config", headers=bearer(g["deviceToken"]))]
    assert [r.status_code for r in antworten] == [200, 502, 200, 200]
    # Der Fake spiegelt den falschen Key im 401-Body – der Proxy reicht ihn nicht durch.
    assert any(FALSCHER_KEY in str(a) for a in fake.anfragen)
    for r in antworten:
        assert FAKE_KEY not in r.text and FALSCHER_KEY not in r.text and "Incorrect API key" not in r.text
        assert all(FAKE_KEY not in v for v in r.headers.values())
    for zeile in logs:
        assert FAKE_KEY not in zeile and FALSCHER_KEY not in zeile, zeile
    roh = _dateien(pein.daten)
    assert FAKE_KEY.encode() not in roh and FALSCHER_KEY.encode() not in roh


def test_kein_inhalt_und_kein_token_im_log(pein, pclient, fake, logs):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    zugang(pein, f["id"], "llm", "custom", fake.basis + "/v1")
    audio = wav(0.5)
    assert stt(pclient, g["deviceToken"], audio, prompt=GEHEIM_PROMPT).status_code == 200
    assert chat(pclient, g["deviceToken"], text=GEHEIM_TEXT).status_code == 200
    fake.modus = "fehler500"
    assert chat(pclient, g["deviceToken"], text=GEHEIM_TEXT).status_code == 502
    assert any("proxy ok" in z for z in logs) and any("proxy fehler" in z for z in logs)  # Metadaten werden geloggt
    for zeile in logs:
        for geheim in (GEHEIM_TEXT, GEHEIM_PROMPT, "Hallo von haupt", "Glätte den Text", g["deviceToken"], FAKE_KEY,
                       "authorization", "Bearer"):
            assert geheim.lower() not in zeile.lower(), (geheim, zeile)
    roh = _dateien(pein.daten)
    for geheim in (GEHEIM_TEXT, GEHEIM_PROMPT, "Hallo von haupt", g["deviceToken"]):
        assert geheim.encode() not in roh, geheim
    assert audio[44:2000] not in roh or audio[44:2000] == b"\x00" * len(audio[44:2000])


def test_audio_nicht_auf_platte(pein, pclient, fake, monkeypatch):
    """Upload bis 25 MiB bleibt im Arbeitsspeicher: keine Temp-Datei (Starlette spoolt Formular-Dateien ab 1 MiB)."""
    import tempfile

    angelegt: list[str] = []
    for name in ("SpooledTemporaryFile", "TemporaryFile", "NamedTemporaryFile", "mkstemp"):
        echt = getattr(tempfile, name)

        def zaehlen(*a, _echt=echt, _name=name, **kw):
            angelegt.append(_name)
            return _echt(*a, **kw)

        monkeypatch.setattr(tempfile, name, zaehlen)
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    r = stt(pclient, g["deviceToken"], wav(40.0))  # gut 1,2 MB
    assert r.status_code == 200
    assert fake.anfragen[0]["datei"] == len(wav(40.0))
    assert angelegt == []
