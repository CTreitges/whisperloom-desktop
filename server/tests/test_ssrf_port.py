"""SSRF-Nachbesserung (W2-SFIX): Freigaben in WL_UPSTREAM_ALLOW mit Port, Basis-URL ohne Fragment, Query, Benutzerinfo,
Backslash, Leer- und Steuerzeichen – beim Speichern (Portal, Datei-Modus, CLI) und je Anfrage im Proxy.
"""

import io

import pytest
from fastapi.testclient import TestClient

from whisperloom_server import cli, dateimodus, db, schluessel, ssrf, upstream
from whisperloom_server.app import create_app
from whisperloom_server.config import Einstellungen, KonfigFehler

from .conftest import BASIS, FAKE_KEY, admin_client, kry, neue_einst, neue_firma, proxy_geraet, seiten_csrf, sql, stt
from .test_ssrf import aufloeser

# --- Freigabe mit Port ---


def test_freigabe_mit_port_gilt_nur_fuer_diesen_port():
    erlaubt = ssrf.freigaben_lesen("127.0.0.2:8100")
    assert ssrf.pruefen("http://127.0.0.2:8100/v1/audio/transcriptions", erlaubt, aufloeser({}))
    for url in ("http://127.0.0.2:22/", "http://127.0.0.2:3000/v1", "http://127.0.0.2:111/", "http://127.0.0.2/v1",
                "https://127.0.0.2/v1"):
        with pytest.raises(ssrf.ZielVerboten, match="adresse"):
            ssrf.pruefen(url, erlaubt, aufloeser({}))


def test_freigabe_mit_port_beachtet_standardport_des_schemas():
    tabelle = aufloeser({"intern.example": ["10.0.0.5"]})
    nur_https = ssrf.freigaben_lesen("10.0.0.5:443")
    assert ssrf.pruefen("https://intern.example/v1", nur_https, tabelle).url == "https://10.0.0.5:443/v1"
    with pytest.raises(ssrf.ZielVerboten):
        ssrf.pruefen("http://intern.example/v1", nur_https, tabelle)  # Port 80
    nur_http = ssrf.freigaben_lesen("10.0.0.5:80")
    assert ssrf.pruefen("http://intern.example/v1", nur_http, tabelle)
    with pytest.raises(ssrf.ZielVerboten):
        ssrf.pruefen("https://intern.example/v1", nur_http, tabelle)


def test_freigabe_ipv6_und_netz_mit_port():
    erlaubt = ssrf.freigaben_lesen("[fd00::5]:8100, 10.20.0.0/16:8000")
    assert ssrf.pruefen("http://[fd00::5]:8100/v1", erlaubt, aufloeser({}))
    assert ssrf.pruefen("http://10.20.3.4:8000/v1", erlaubt, aufloeser({}))
    for url in ("http://[fd00::5]:8101/v1", "http://10.20.3.4:8001/v1", "http://10.21.0.1:8000/v1"):
        with pytest.raises(ssrf.ZielVerboten):
            ssrf.pruefen(url, erlaubt, aufloeser({}))


def test_freigabe_ohne_port_wie_bisher_fuer_alle_ports():
    erlaubt = ssrf.freigaben_lesen("127.0.0.2, 10.20.0.0/16, [fd00::5]")
    for url in ("http://127.0.0.2:22/", "http://127.0.0.2/v1", "https://10.20.1.1:9443/v1", "http://[fd00::5]:1/"):
        assert ssrf.pruefen(url, erlaubt, aufloeser({}))


def test_metadaten_bleiben_trotz_freigabe_mit_port_gesperrt():
    erlaubt = ssrf.freigaben_lesen("169.254.169.254:80, 169.254.0.0/16:80, 100.100.100.200:80")
    for url in ("http://169.254.169.254/latest", "http://100.100.100.200/"):
        with pytest.raises(ssrf.ZielVerboten, match="adresse"):
            ssrf.pruefen(url, erlaubt, aufloeser({}))


@pytest.mark.parametrize("text", ["127.0.0.2:0", "127.0.0.2:65536", "127.0.0.2:abc", "127.0.0.2:", "[fd00::5]:x",
                                  "[fd00::5", "kein-netz:80", "127.0.0.2:+80"])
def test_kaputte_freigabe_wird_abgelehnt(text):
    with pytest.raises(ValueError):
        ssrf.freigaben_lesen(text)


def test_config_liest_freigabe_mit_port(monkeypatch, tmp_path):
    monkeypatch.setenv("WL_DATA_DIR", str(tmp_path))
    monkeypatch.setenv("WL_UPSTREAM_ALLOW", "127.0.0.2:8100, 10.20.0.0/16")
    erlaubt = Einstellungen.aus_umgebung().upstream_erlaubt
    assert [(str(f.netz), f.port) for f in erlaubt] == [("127.0.0.2/32", 8100), ("10.20.0.0/16", None)]
    monkeypatch.setenv("WL_UPSTREAM_ALLOW", "127.0.0.2:99999")
    with pytest.raises(KonfigFehler, match="WL_UPSTREAM_ALLOW"):
        Einstellungen.aus_umgebung()


def test_proxy_nutzt_nur_den_freigegebenen_port(tmp_path, uhr, fake):
    port = fake.server_address[1]
    einst = neue_einst(tmp_path, uhr, upstream_erlaubt=ssrf.freigaben_lesen(f"127.0.0.1:{port}"))
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f, g = proxy_geraet(c, einst)
        schluessel_direkt(einst, f["id"], "stt", f"http://127.0.0.1:{port}/v1")
        assert stt(c, g["deviceToken"]).status_code == 200
        schluessel_direkt(einst, f["id"], "stt", f"http://127.0.0.1:{port + 1}/v1")
        r = stt(c, g["deviceToken"])
        assert r.status_code == 502 and r.json()["error"] == "upstream_error"
    assert len(fake.anfragen) == 1


def test_portal_lehnt_anderen_port_ab(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, upstream_erlaubt=ssrf.freigaben_lesen("127.0.0.2:8100"))
    app = create_app(einst)
    f = proxy_geraet(TestClient(app, base_url=BASIS), einst)[0]
    c = admin_client(app, einst, uhr, f["id"], "owner-a")
    for url, ziel in (("http://127.0.0.2:22/v1", "/schluessel?hinweis=ziel_verboten"),
                      ("http://127.0.0.2:8100/v1", "/schluessel?ok=key_gespeichert")):
        r = c.post("/schluessel", data={"csrf": seiten_csrf(c, "/schluessel"), "name": "stt", "anbieter": "custom",
                                        "basis_url": url}, follow_redirects=False)
        assert r.headers["location"] == ziel, url


# --- Basis-URL streng ---

SCHLECHT = [
    "http://127.0.0.2:8100/v1#",
    "http://127.0.0.2:8100/v1#/../../",
    "http://127.0.0.2:8100/v1?",
    "http://127.0.0.2:8100/v1?x=/admin",
    "http://nutzer@127.0.0.2:8100/v1",
    "http://nutzer:pw@127.0.0.2:8100/v1",
    "http://127.0.0.2:8100\\v1",
    "http://127.0.0.2:8100/v 1",
    "http://127.0.0.2:8100/v1\t/x",
    "http://127.0.0.2:8100/v1\x00",
    "http://127.0.0.2:8100/v1\x7f",
    "http://127.0.0.2:8100/v1" + chr(0xA0) + "/x",
    "http://127.0.0.2:8100/v1" + chr(0x2028) + "/x",
    "ftp://127.0.0.2/v1",
    "javascript:alert(1)",
    "https:///v1",
    "http://127.0.0.2:99999/v1",
    "127.0.0.2:8100/v1",
]
GUT = ["https://api.openai.com/v1", "http://127.0.0.2:8100/v1", "http://[fd00::5]:8100/v1/", "https://host.example",
       "https://generativelanguage.googleapis.com/v1beta/openai"]


@pytest.mark.parametrize("url", SCHLECHT)
def test_basis_url_streng_abgelehnt(url):
    assert ssrf.basis_fehler(url) is not None


@pytest.mark.parametrize("url", GUT)
def test_basis_url_gueltig(url):
    assert ssrf.basis_fehler(url) is None


@pytest.mark.parametrize("url", SCHLECHT)
def test_speichern_lehnt_schlechte_basis_url_ab(tmp_path, uhr, url):
    """schluessel.speichern ist der gemeinsame Weg von Portal, Datei-Modus und CLI."""
    einst = neue_einst(tmp_path, uhr)
    fid = neue_firma(einst, "muster")["id"]
    con = db.verbinden(einst.db_pfad)
    try:
        with pytest.raises(schluessel.SchluesselFehler, match="h.basis_url"):
            schluessel.speichern(con, kry(einst), fid, "stt", "custom", url, "", 0)
    finally:
        con.close()


def test_portal_lehnt_fragment_und_query_ab(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, upstream_erlaubt=ssrf.freigaben_lesen("127.0.0.2:8100"))
    app = create_app(einst)
    f = proxy_geraet(TestClient(app, base_url=BASIS), einst)[0]
    c = admin_client(app, einst, uhr, f["id"], "owner-a")
    for url in ("http://127.0.0.2:8100/v1#", "http://127.0.0.2:8100/v1?x=1", "http://a@127.0.0.2:8100/v1",
                "http://127.0.0.2:8100\\v1"):
        r = c.post("/schluessel", data={"csrf": seiten_csrf(c, "/schluessel"), "name": "stt", "anbieter": "custom",
                                        "basis_url": url}, follow_redirects=False)
        assert r.headers["location"] == "/schluessel?hinweis=basis_url", url
    assert sql(einst, "SELECT COUNT(*) FROM schluessel")[0][0] == 0
    seite = c.get("/schluessel?hinweis=basis_url").text
    assert "#" in seite and "?" in seite  # Hinweistext nennt die verbotenen Zeichen


def test_hinweis_basis_url_de_und_en():
    import json
    from pathlib import Path

    ordner = Path(ssrf.__file__).parent / "texte"
    for sprache in ("de", "en"):
        assert json.loads((ordner / f"{sprache}.json").read_text(encoding="utf-8")).get("h.basis_url")


def test_cli_lehnt_fragment_ab(tmp_path, uhr, capsys, monkeypatch):
    einst = neue_einst(tmp_path, uhr, upstream_erlaubt=ssrf.freigaben_lesen("127.0.0.2:8100"))
    cli.main(["tenant-create", "muster", "Muster GmbH"], einst)
    monkeypatch.setattr("sys.stdin", io.StringIO("\n"))
    assert cli.main(["key-set", "muster", "stt", "--provider", "custom", "--base-url",
                     "http://127.0.0.2:8100/v1#"], einst) == 1
    assert "basis_url" in capsys.readouterr().err
    assert sql(einst, "SELECT COUNT(*) FROM schluessel")[0][0] == 0


def test_dateimodus_lehnt_query_ab(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr)
    firma = {"slug": "muster", "name": "Muster GmbH", "code": "ABCD-EFGH-JKMN-PQRS", "keyMode": "proxy",
             "keys": {"stt": {"provider": "custom", "baseUrl": "http://127.0.0.2:8100/v1?x=/admin"}}}
    with pytest.raises(dateimodus.DateiFehler, match="basis_url"):
        dateimodus.anwenden(einst, kry(einst), [firma])


# --- Upstream-Pfad über URL-Bausteine, Prüfung je Anfrage ---


def _zugang(anbieter: str, basis_url: str) -> dict:
    return {"anbieter": anbieter, "basis_url": basis_url, "key": "test-key-not-real-7", "modell": ""}


@pytest.mark.parametrize("basis_url", ["http://h.example/v1#", "http://h.example/v1?x=/admin#", "http://h.example/v1/?"])
def test_upstream_pfad_ueberlebt_kein_fragment_oder_query(basis_url):
    erk = upstream.erkennung_anfrage(_zugang("custom", basis_url), {"model": "m"}, b"RIFF")
    assert erk.url == "http://h.example/v1/audio/transcriptions"
    body = {"model": "m", "messages": [{"role": "user", "content": "x"}]}
    assert upstream.chat_anfrage(_zugang("custom", basis_url), body).url == "http://h.example/v1/chat/completions"
    assert upstream.chat_anfrage(_zugang("anthropic", basis_url), body).url == "http://h.example/v1/messages"
    assert upstream.chat_anfrage(_zugang("ollama", basis_url), body).url == "http://h.example/api/chat"
    assert (upstream.chat_anfrage(_zugang("gemini", basis_url), body).url
            == "http://h.example/v1/models/m:generateContent")
    el = upstream.erkennung_anfrage(_zugang("elevenlabs", basis_url), {"model": "m"}, b"RIFF")
    assert el.url == "http://h.example/v1/speech-to-text"


def test_upstream_standardadressen_unveraendert():
    assert upstream.erkennung_anfrage(_zugang("groq", ""), {"model": "m"}, b"R").url == \
        "https://api.groq.com/openai/v1/audio/transcriptions"
    assert upstream.erkennung_anfrage(_zugang("deepinfra", ""), {"model": "m"}, b"R").url == \
        "https://api.deepinfra.com/v1/audio/transcriptions"
    body = {"model": "x/y", "messages": [{"role": "user", "content": "x"}]}
    assert upstream.chat_anfrage(_zugang("gemini", ""), body).url == \
        "https://generativelanguage.googleapis.com/v1beta/models/x%2Fy:generateContent"


def schluessel_direkt(einst: Einstellungen, firma_id: str, name: str, basis_url: str) -> None:
    """Am Formular vorbei in die DB (Altbestand vor W2-SFIX oder manipulierte DB): der Proxy muss je Anfrage prüfen."""
    con = db.verbinden(einst.db_pfad)
    try:
        chiffre, nonce = kry(einst).verschluesseln(FAKE_KEY, f"{firma_id}|{name}")
        con.execute("INSERT INTO schluessel (firma_id, zugang, anbieter, basis_url, chiffre, nonce, letzte4, geaendert_ms, "
                    "modell) VALUES (?, ?, 'custom', ?, ?, ?, '', 0, 'fake-whisper') ON CONFLICT (firma_id, zugang) "
                    "DO UPDATE SET basis_url = excluded.basis_url", (firma_id, name, basis_url, chiffre, nonce))
    finally:
        con.close()


@pytest.mark.parametrize("anhang", ["#", "?x=1", "#/../admin"])
def test_proxy_prueft_basis_url_je_anfrage(tmp_path, uhr, fake, anhang):
    port = fake.server_address[1]
    einst = neue_einst(tmp_path, uhr, upstream_erlaubt=ssrf.freigaben_lesen(f"127.0.0.1:{port}"))
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f, g = proxy_geraet(c, einst)
        schluessel_direkt(einst, f["id"], "stt", f"http://127.0.0.1:{port}/v1{anhang}")
        r = stt(c, g["deviceToken"])
        assert r.status_code == 502 and r.json()["error"] == "upstream_error"
    assert fake.anfragen == []
