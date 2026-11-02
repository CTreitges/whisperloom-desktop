"""W3-REST (4): Vokabular (`keywords[]`) und „Deutsch + Englisch“ (zweimal `languages[]`) über den Proxy.

Das Gerät schickt eine neutrale Form; der Proxy prüft sie streng (Whitelist, Anzahl, Länge, Zeichen) und übersetzt je
Anbieter des Zugangs wie die App bei Direktzugang (TranscriptionRequest.cs, ElevenLabsStt.cs): gpt-transcribe bei OpenAI
→ `languages[]` + `keywords[]`, Mistral → `context_bias[]`, ElevenLabs → `keyterms`, alle anderen nur `prompt` und eine
Sprache. Nur Fake-Anbieter.
"""

import pytest

from whisperloom_server import upload

from .conftest import FAKE_KEY_2, bearer, proxy_geraet, wav, zugang

URL = "/api/v1/proxy/audio/transcriptions"
BEGRIFFE = ["WhisperLoom", "Treitges", "Firmen Manager"]


def _fehler(r, code: str = "invalid_request") -> None:
    assert r.status_code == 400, (r.status_code, r.text)
    assert r.json()["error"] == code


def _senden(pclient, pein, fake, anbieter: str, modell: str = "", **felder):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", anbieter, fake.basis + "/v1", modell=modell)
    werte = {k: v for k, v in {"model": "whisper-x", "language": "de", "response_format": "json", **felder}.items()
             if v is not None}
    return pclient.post(URL, headers=bearer(g["deviceToken"]), files={"file": ("a.wav", wav(), "audio/wav")},
                        data=werte)


# --- Durchreichen je Anbieter ---


def test_gpt_transcribe_bekommt_beide_sprachen_und_keywords(pein, pclient, fake):
    r = _senden(pclient, pein, fake, "openai", "gpt-transcribe", language=None, **{"languages[]": ["de", "en"]},
                **{"keywords[]": BEGRIFFE}, prompt="WhisperLoom, Treitges, Firmen Manager")
    assert r.status_code == 200, r.text
    felder = fake.anfragen[0]["felder"]
    assert felder["languages[]"] == ["de", "en"] and "language" not in felder
    assert felder["keywords[]"] == BEGRIFFE and felder["prompt"] == ["WhisperLoom, Treitges, Firmen Manager"]


def test_gpt_transcribe_eine_sprache_als_languages(pein, pclient, fake):
    assert _senden(pclient, pein, fake, "openai", "gpt-4o-transcribe").status_code == 200
    felder = fake.anfragen[0]["felder"]
    assert felder["languages[]"] == ["de"] and "language" not in felder


def test_mistral_bekommt_context_bias_statt_keywords(pein, pclient, fake):
    r = _senden(pclient, pein, fake, "mistral", "voxtral-mini-latest", **{"keywords[]": BEGRIFFE})
    assert r.status_code == 200, r.text
    felder = fake.anfragen[0]["felder"]
    assert felder["context_bias[]"] == ["WhisperLoom", "Treitges", "Firmen_Manager"]
    assert "keywords[]" not in felder and "prompt" not in felder


def test_mistral_context_bias_ohne_doppelte_nach_dem_ersetzen(pein, pclient, fake):
    """Wie TranscriptionRequest.ContextBias der App: erst Leerraum → _, dann Duplikate entfernen (Review W3-REST)."""
    r = _senden(pclient, pein, fake, "mistral", "voxtral-mini-latest", **{"keywords[]": ["a b", "a_b", "c"]})
    assert r.status_code == 200, r.text
    assert fake.anfragen[0]["felder"]["context_bias[]"] == ["a_b", "c"]


def test_ersatz_zugang_anderer_family_bekommt_eigene_felder(pein, pclient, fake, fake2):
    """Review D-2: Primär OpenAI gpt-transcribe fällt aus, der Ersatz Mistral bekommt context_bias[] und keine
    languages[]/keywords[] – die Übersetzung gilt je tatsächlich genutztem Zugang."""
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "openai", fake.basis + "/v1", modell="gpt-transcribe")
    zugang(pein, f["id"], "stt_fallback", "mistral", fake2.basis + "/v1", key=FAKE_KEY_2, modell="voxtral-mini-latest")
    fake.modus = "fehler500"
    r = pclient.post(URL, headers=bearer(g["deviceToken"]), files={"file": ("a.wav", wav(), "audio/wav")},
                     data={"model": "x", "languages[]": ["de", "en"], "keywords[]": BEGRIFFE})
    assert r.status_code == 200, r.text
    assert fake.anfragen[0]["felder"]["languages[]"] == ["de", "en"]
    ersatz = fake2.anfragen[0]["felder"]
    assert ersatz["context_bias[]"] == ["WhisperLoom", "Treitges", "Firmen_Manager"]
    assert not {"languages[]", "keywords[]", "language", "prompt"} & set(ersatz)


def test_elevenlabs_bekommt_keyterms_und_bei_zwei_sprachen_keinen_code(pein, pclient, fake):
    r = _senden(pclient, pein, fake, "elevenlabs", "scribe_v2", language=None, **{"languages[]": ["de", "en"]},
                **{"keywords[]": BEGRIFFE + ["{klammer}"]})
    assert r.status_code == 200, r.text
    felder = fake.anfragen[0]["felder"]
    assert felder["keyterms"] == BEGRIFFE  # ElevenLabs lehnt {}[]\ ab: fällt weg wie in der App
    assert "language_code" not in felder and "keywords[]" not in felder


@pytest.mark.parametrize("anbieter,modell", [("custom", "whisper-x"), ("groq", "whisper-large-v3"),
                                             ("openai", "whisper-1")])
def test_andere_bekommen_kein_vokabular_feld_und_bei_zwei_sprachen_keine(pein, pclient, fake, anbieter, modell):
    r = _senden(pclient, pein, fake, anbieter, modell, language=None, **{"languages[]": ["de", "en"]},
                **{"keywords[]": BEGRIFFE}, prompt="WhisperLoom")
    assert r.status_code == 200, r.text
    felder = fake.anfragen[0]["felder"]
    assert not {"keywords[]", "languages[]", "language", "context_bias[]"} & set(felder)
    assert felder["prompt"] == ["WhisperLoom"]


# --- Whitelist und Grenzen (OWASP A03) ---


@pytest.mark.parametrize("felder", [
    {"keywords[]": ["a" * 50]},  # zu lang
    {"keywords[]": ["eins zwei drei vier fünf sechs"]},  # zu viele Wörter
    {"keywords[]": ["<script>"]},  # verbotene Zeichen
    {"keywords[]": ["a,b"]},
    {"keywords[]": [" "]},
    {"keywords[]": [f"w{i}" for i in range(101)]},  # zu viele
    {"languages[]": ["de", "en", "fr"]},  # zu viele
    {"languages[]": ["de", "de"]},  # doppelt
    {"languages[]": ["DE"]},
    {"languages[]": ["deutsch"]},
    {"languages[]": ["de"], "language": "de"},  # beides zugleich
    {"context_bias[]": ["x"]},  # nur der Proxy erzeugt es
    {"keyterms": ["x"]},
    {"prompt": ["a", "b"]},  # Einzelfeld doppelt
])
def test_ungueltige_felder_abgelehnt(pein, pclient, fake, felder):
    r = _senden(pclient, pein, fake, "openai", "gpt-transcribe", **felder)
    _fehler(r)
    assert fake.anfragen == []


def test_hundert_begriffe_und_zwei_sprachen_erlaubt(pein, pclient, fake):
    viele = [f"wort{i}" for i in range(100)]
    r = _senden(pclient, pein, fake, "openai", "gpt-transcribe", language=None, **{"languages[]": ["de", "en"]},
                **{"keywords[]": viele})
    assert r.status_code == 200, r.text
    assert fake.anfragen[0]["felder"]["keywords[]"] == viele


def test_parser_teilt_listen_und_einzelfelder():
    grenze = "xyz"
    teile = [("model", b"m"), ("keywords[]", b"A"), ("keywords[]", "Ölbaum".encode()), ("languages[]", b"de"),
             ("languages[]", b"en")]
    body = b"".join(f'--{grenze}\r\nContent-Disposition: form-data; name="{n}"\r\n\r\n'.encode() + v + b"\r\n"
                    for n, v in teile)
    body += f'--{grenze}\r\nContent-Disposition: form-data; name="file"; filename="a.wav"\r\n\r\n'.encode()
    body += wav(0.1) + f"\r\n--{grenze}--\r\n".encode()
    felder, datei = upload.multipart_lesen(f"multipart/form-data; boundary={grenze}", body)
    assert felder == {"model": "m", "keywords[]": ["A", "Ölbaum"], "languages[]": ["de", "en"]}
    assert datei == wav(0.1)


def test_direkt_ohne_listen_unveraendert(pein, pclient, fake):
    """Bestehende Geräte (ohne Listenfelder) bleiben wie bisher: eine Sprache als `language`."""
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    r = pclient.post(URL, headers=bearer(g["deviceToken"]), files={"file": ("a.wav", wav(), "audio/wav")},
                     data={"model": "x", "language": "de"})
    assert r.status_code == 200 and fake.anfragen[0]["felder"]["language"] == ["de"]
