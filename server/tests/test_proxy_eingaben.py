"""TP-WPROXY (OWASP A03, W3-REST D-1): `language` und `temperature` des Geräts werden streng geprüft.

Gültig gehen sie an den Upstream, ungültig gibt es 400 `invalid_request` ohne Echo des Werts und keinen Upstream-Aufruf.
Nur Fake-Anbieter.
"""

import pytest

from .conftest import bearer, proxy_geraet, wav, zugang

URL = "/api/v1/proxy/audio/transcriptions"
LANG = "x" * 4000


def _senden(pclient, pein, fake, **felder):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "groq", fake.basis + "/v1")
    return pclient.post(URL, headers=bearer(g["deviceToken"]), files={"file": ("a.wav", wav(), "audio/wav")},
                        data={"model": "whisper-x", **felder})


@pytest.mark.parametrize("sprache", ["auto", "de", "deu", "pt-BR"])
def test_gueltige_sprache_geht_durch(pein, pclient, fake, sprache):
    r = _senden(pclient, pein, fake, language=sprache)
    assert r.status_code == 200, r.text
    assert len(fake.anfragen) == 1


def test_sprache_mit_region_erreicht_upstream(pein, pclient, fake):
    assert _senden(pclient, pein, fake, language="pt-BR").status_code == 200
    assert fake.anfragen[0]["felder"]["language"] == ["pt-BR"]


@pytest.mark.parametrize("wert", ["0", "0.2", "1"])
def test_gueltige_temperatur_geht_durch(pein, pclient, fake, wert):
    r = _senden(pclient, pein, fake, language="de", temperature=wert)
    assert r.status_code == 200, r.text
    assert len(fake.anfragen) == 1


@pytest.mark.parametrize("feld,wert", [
    ("language", "de;x"), ("language", "xx-yyy"), ("language", ""), ("language", LANG), ("language", "DE"),
    ("language", "de\r\nX-Evil: 1"),
    ("temperature", "NaN"), ("temperature", "-1"), ("temperature", "abc"), ("temperature", "1.5"),
    ("temperature", "2"), ("temperature", ""), ("temperature", LANG), ("temperature", "inf"), ("temperature", "1e0"),
])
def test_ungueltiger_wert_400_ohne_upstream_und_ohne_echo(pein, pclient, fake, feld, wert):
    felder = {"language": "de", feld: wert}
    r = _senden(pclient, pein, fake, **felder)
    assert r.status_code == 400, (r.status_code, r.text)
    assert r.json()["error"] == "invalid_request" and r.json()["message"]
    if wert:
        assert wert not in r.text
    assert fake.anfragen == []
