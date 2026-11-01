"""SSRF-Schutz des Proxys (W2-SRV2): verbotene Zielklassen, DNS-Rebinding (Fake-Resolver), Anheften der geprüften IP,
keine Weiterleitungen, Freigabe per Instanz-Config (WL_UPSTREAM_ALLOW), Prüfung beim Speichern im Portal.
"""

import ipaddress

import pytest
from fastapi.testclient import TestClient

from whisperloom_server import ssrf
from whisperloom_server.app import create_app
from whisperloom_server.config import Einstellungen, KonfigFehler

from .conftest import BASIS, admin_client, neue_einst, proxy_geraet, seiten_csrf, sql, stt, zugang

OEFFENTLICH = "93.184.216.34"


def aufloeser(tabelle: dict[str, list[str]]):
    def aufloesen(host: str, _port: int) -> list[str]:
        if host in tabelle:
            return list(tabelle[host])
        try:
            return [str(ipaddress.ip_address(host))]
        except ValueError:
            raise OSError("unbekannt") from None
    return aufloesen


@pytest.mark.parametrize("adresse", [
    "127.0.0.1", "127.9.9.9", "::1", "0.0.0.0", "::",  # noqa: S104 (Loopback, unspezifiziert)
    "10.1.2.3", "172.16.5.4", "172.31.255.1", "192.168.0.10", "fd12:3456::1",  # private Netze, ULA
    "169.254.169.254", "169.254.1.1", "fe80::1",  # link-local, Metadaten (AWS/GCP/Azure)
    "100.100.100.200", "fd00:ec2::254",  # Metadaten Alibaba, AWS IPv6
    "100.64.0.1", "224.0.0.1", "ff02::1", "240.0.0.1", "::ffff:127.0.0.1",
    "::ffff:10.0.0.1",  # CGNAT, Multicast, reserviert, v4-in-v6
])
def test_verbotene_adressklassen(adresse):
    with pytest.raises(ssrf.ZielVerboten, match="adresse"):
        ssrf.pruefen("https://anbieter.example/v1/chat", (), aufloeser({"anbieter.example": [adresse]}))


@pytest.mark.parametrize("url,grund", [
    ("unix:///var/run/docker.sock", "schema"),
    ("http+unix://%2Fvar%2Frun%2Fdocker.sock/v1", "schema"),
    ("file:///etc/passwd", "schema"),
    ("ftp://anbieter.example/", "schema"),
    ("https://nutzer:passwort@anbieter.example/v1", "zugangsdaten"),
    ("https:///v1", "host"),
    ("https://unbekannt.example/v1", "aufloesung"),
])
def test_verbotene_urls(url, grund):
    with pytest.raises(ssrf.ZielVerboten, match=grund):
        ssrf.pruefen(url, (), aufloeser({"anbieter.example": [OEFFENTLICH]}))


def test_oeffentliches_ziel_wird_angeheftet():
    ziel = ssrf.pruefen("https://anbieter.example:8443/v1/audio?x=1", (), aufloeser({"anbieter.example": [OEFFENTLICH]}))
    assert ziel.url == f"https://{OEFFENTLICH}:8443/v1/audio?x=1"
    assert ziel.host_header == "anbieter.example:8443" and ziel.sni == "anbieter.example"
    ziel6 = ssrf.pruefen("http://v6.example/v1", (), aufloeser({"v6.example": ["2606:2800:220:1::1"]}))
    assert ziel6.url == "http://[2606:2800:220:1::1]:80/v1" and ziel6.sni is None


def test_eine_interne_von_mehreren_adressen_reicht_zum_ablehnen():
    with pytest.raises(ssrf.ZielVerboten):
        ssrf.pruefen("https://misch.example/v1", (), aufloeser({"misch.example": [OEFFENTLICH, "10.0.0.5"]}))


def test_freigabe_per_config():
    erlaubt = ssrf.netze_lesen("10.20.0.0/16, 127.0.0.1/32")
    assert ssrf.pruefen("http://whisper.intern:8000/v1", erlaubt, aufloeser({"whisper.intern": ["10.20.3.4"]}))
    with pytest.raises(ssrf.ZielVerboten):
        ssrf.pruefen("http://andere.intern/v1", erlaubt, aufloeser({"andere.intern": ["10.21.0.1"]}))
    with pytest.raises(ssrf.ZielVerboten):  # Metadaten bleiben verboten, solange ihr Netz nicht freigegeben ist
        ssrf.pruefen("http://169.254.169.254/", erlaubt, aufloeser({}))


def test_config_aus_umgebung(monkeypatch, tmp_path):
    monkeypatch.setenv("WL_DATA_DIR", str(tmp_path))
    monkeypatch.setenv("WL_UPSTREAM_ALLOW", "192.168.10.0/24")
    assert Einstellungen.aus_umgebung().upstream_erlaubt == (ipaddress.ip_network("192.168.10.0/24"),)
    monkeypatch.setenv("WL_UPSTREAM_ALLOW", "kein-netz")
    with pytest.raises(KonfigFehler, match="WL_UPSTREAM_ALLOW"):
        Einstellungen.aus_umgebung()
    monkeypatch.delenv("WL_UPSTREAM_ALLOW")
    assert Einstellungen.aus_umgebung().upstream_erlaubt == ()


# --- im Proxy ---


def test_ohne_freigabe_kein_aufruf_an_loopback(tmp_path, uhr, fake):
    einst = neue_einst(tmp_path, uhr)  # ohne upstream_erlaubt
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f, g = proxy_geraet(c, einst)
        zugang(einst, f["id"], "stt", "custom", fake.basis + "/v1")
        r = stt(c, g["deviceToken"])
        assert r.status_code == 502 and r.json()["error"] == "upstream_error"
    assert fake.anfragen == []


def test_dns_rebinding_wird_je_anfrage_geprueft(tmp_path, uhr, fake):
    """Beim Speichern zeigt der Name nach außen, bei der Anfrage auf 127.0.0.1 → abgelehnt, ohne Verbindung."""
    tabelle = {"rebind.example": [OEFFENTLICH]}
    einst = neue_einst(tmp_path, uhr, aufloeser=aufloeser(tabelle))
    url = f"http://rebind.example:{fake.server_address[1]}/v1"
    assert ssrf.beim_speichern_ok(url, einst.upstream_erlaubt, einst.aufloeser)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f, g = proxy_geraet(c, einst)
        zugang(einst, f["id"], "stt", "custom", url)
        tabelle["rebind.example"] = ["127.0.0.1"]
        r = stt(c, g["deviceToken"])
        assert r.status_code == 502
    assert fake.anfragen == []


def test_proxy_verbindet_sich_mit_der_gepruften_ip(tmp_path, uhr, fake):
    """`upstream.test` kennt nur der Fake-Resolver: der Aufruf klappt nur, wenn der Proxy die geprüfte IP nutzt und
    nicht selbst noch einmal auflöst. Der Anbieter sieht den Namen im Host-Header."""
    einst = neue_einst(tmp_path, uhr, upstream_erlaubt=(ipaddress.ip_network("127.0.0.1/32"),),
                       aufloeser=aufloeser({"upstream.test": ["127.0.0.1"]}))
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f, g = proxy_geraet(c, einst)
        zugang(einst, f["id"], "stt", "custom", f"http://upstream.test:{fake.server_address[1]}/v1")
        r = stt(c, g["deviceToken"])
        assert r.status_code == 200, r.text
    assert fake.anfragen[0]["header"]["host"] == f"upstream.test:{fake.server_address[1]}"


def test_weiterleitung_wird_nicht_gefolgt(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    fake.modus = "weiterleitung"
    r = stt(pclient, g["deviceToken"])
    assert r.status_code == 502 and r.json()["error"] == "upstream_error"
    assert len(fake.anfragen) == 1


# --- Portal: Prüfung beim Speichern ---


def test_portal_lehnt_internes_ziel_ab(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, aufloeser=aufloeser({"intern.example": ["10.0.0.7"]}))
    app = create_app(einst)
    f = proxy_geraet(TestClient(app, base_url=BASIS), einst)[0]
    c = admin_client(app, einst, uhr, f["id"], "owner-a")
    for url in ("http://127.0.0.1:9000/v1", "http://intern.example/v1", "http://169.254.169.254/latest"):
        r = c.post("/schluessel", data={"csrf": seiten_csrf(c, "/schluessel"), "name": "stt", "anbieter": "custom",
                                        "basis_url": url, "key": "test-key-not-real-3"}, follow_redirects=False)
        assert r.status_code == 303 and r.headers["location"] == "/schluessel?hinweis=ziel_verboten", url
    assert sql(einst, "SELECT COUNT(*) FROM schluessel")[0][0] == 0


def test_portal_nimmt_freigegebenes_ziel(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, upstream_erlaubt=(ipaddress.ip_network("127.0.0.1/32"),))
    app = create_app(einst)
    f = proxy_geraet(TestClient(app, base_url=BASIS), einst)[0]
    c = admin_client(app, einst, uhr, f["id"], "owner-a")
    r = c.post("/schluessel", data={"csrf": seiten_csrf(c, "/schluessel"), "name": "stt", "anbieter": "custom",
                                    "basis_url": "http://127.0.0.1:9000/v1", "modell": "fake-whisper"},
               follow_redirects=False)
    assert r.headers["location"] == "/schluessel?ok=key_gespeichert"
    assert tuple(sql(einst, "SELECT anbieter, modell, letzte4 FROM schluessel")[0]) == ("custom", "fake-whisper", "")
