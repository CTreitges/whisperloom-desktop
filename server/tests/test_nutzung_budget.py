"""Nutzungsstatistik und Budgets des Proxys (W2-SRV2): Zähler je Gerät/Gruppe/Firma und Monat, Kosten-Schätzung aus der
Preistabelle, Budget je Ebene inkl. Monatswechsel, Warnschwelle, Portal-Seiten, CSV-Export.
"""

import csv
import io

import pytest
from fastapi.testclient import TestClient

from whisperloom_server import budget, db, krypto, nutzung
from whisperloom_server.app import create_app

from .conftest import (
    BASIS,
    admin_client,
    chat,
    freies_geraet,
    geraet_setzen,
    proxy_geraet,
    seiten_csrf,
    sql,
    stt,
    wav,
    zugang,
)

TAG = 24 * 3600 * 1000


def _gruppe(einst, fid: str, name: str) -> str:
    gid = krypto.kurz_id("g_")
    con = db.verbinden(einst.db_pfad)
    con.execute("INSERT INTO gruppe (id, firma_id, name, angelegt_ms) VALUES (?, ?, ?, 0)", (gid, fid, name))
    con.close()
    return gid


def _budget(einst, fid: str, ebene: str, ziel: str = "", warn: int = 80, **grenzen) -> None:
    con = db.verbinden(einst.db_pfad)
    budget.setzen(con, fid, ebene, ziel, grenzen, warn)
    con.close()


@pytest.fixture
def firma(pein, pclient, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1", modell="whisper-fest")
    zugang(pein, f["id"], "llm", "custom", fake.basis + "/v1", modell="llm-fest")
    return f, g


def test_zaehler_je_geraet_gruppe_firma(pein, pclient, fake, firma):
    f, g = firma
    gid = _gruppe(pein, f["id"], "Buchhaltung")
    geraet_setzen(pein, g["deviceId"], gruppe_id=gid)
    g2 = freies_geraet(pclient, pein, f["code"])
    assert stt(pclient, g["deviceToken"], wav(1.5)).status_code == 200
    assert stt(pclient, g["deviceToken"], wav(1.5)).status_code == 200
    assert chat(pclient, g["deviceToken"]).status_code == 200
    assert chat(pclient, g2["deviceToken"]).status_code == 200
    fake.modus = "fehler500"
    assert chat(pclient, g2["deviceToken"]).status_code == 502
    con = db.verbinden(pein.db_pfad)
    m = nutzung.monat(pein.uhr())
    t = nutzung.tabelle(con, f["id"], m)
    assert nutzung.summe(con, f["id"], m, "geraet", g["deviceId"]) == {
        "anfragen": 3, "audio_ms": 3000, "tokens_ein": 11, "tokens_aus": 7, "kosten_mikro": 0}
    assert nutzung.summe(con, f["id"], m, "gruppe", gid)["anfragen"] == 3
    con.close()
    assert t["firma"]["anfragen"] == 4 and t["firma"]["tokens_ein"] == 22 and t["firma"]["fehler"] == {"upstream_error": 1}
    zeilen = {d["geraet_id"]: d for d in t["geraete"]}
    assert zeilen[g2["deviceId"]]["fehler"] == {"upstream_error": 1} and zeilen[g["deviceId"]]["gruppe"] == "Buchhaltung"
    assert {gr["name"] for gr in t["gruppen"]} == {"Buchhaltung", ""}


def test_kosten_aus_preistabelle(pein, pclient, firma):
    f, g = firma
    con = db.verbinden(pein.db_pfad)
    budget.preis_setzen(con, f["id"], "whisper-fest", nutzung.betrag_lesen("0,006"), None, None)
    budget.preis_setzen(con, f["id"], "llm-fest", None, nutzung.betrag_lesen("2"), nutzung.betrag_lesen("10"))
    con.close()
    stt(pclient, g["deviceToken"], wav(30.0))  # 0,5 min × 0,006 = 0,003
    chat(pclient, g["deviceToken"])  # 11 × 2/1e6 + 7 × 10/1e6 = 0,000092
    assert sql(pein, "SELECT kosten_mikro FROM nutzung")[0][0] == 3000 + 92


@pytest.mark.parametrize("ebene", ["geraet", "gruppe", "firma"])
def test_budget_greift_je_ebene(pein, pclient, fake, firma, ebene):
    f, g = firma
    gid = _gruppe(pein, f["id"], "Vertrieb")
    geraet_setzen(pein, g["deviceId"], gruppe_id=gid)
    g2 = freies_geraet(pclient, pein, f["code"])  # andere Gruppe (keine)
    ziel = {"geraet": g["deviceId"], "gruppe": gid, "firma": ""}[ebene]
    _budget(pein, f["id"], ebene, ziel, max_anfragen=2)
    assert stt(pclient, g["deviceToken"]).status_code == 200
    assert chat(pclient, g["deviceToken"]).status_code == 200
    n = len(fake.anfragen)
    r = stt(pclient, g["deviceToken"])
    assert r.status_code == 403 and r.json()["error"] == "budget_exhausted"
    assert len(fake.anfragen) == n  # abgelehnt, ohne den Anbieter zu fragen
    andere = stt(pclient, g2["deviceToken"]).status_code
    assert andere == (403 if ebene == "firma" else 200)
    fehler = dict(sql(pein, "SELECT geraet_id, anzahl FROM nutzung_fehler WHERE klasse = 'budget'"))
    assert fehler[g["deviceId"]] == 1


@pytest.mark.parametrize("grenze,anfrage", [("max_audio_min", "stt"), ("max_tokens", "chat"), ("max_kosten_mikro", "chat")])
def test_budget_minuten_tokens_kosten(pein, pclient, firma, grenze, anfrage):
    f, g = firma
    con = db.verbinden(pein.db_pfad)
    budget.preis_setzen(con, f["id"], "llm-fest", None, nutzung.betrag_lesen("1000"), None)  # 11 Tokens = 0,011
    con.close()
    _budget(pein, f["id"], "firma", **{grenze: {"max_audio_min": 1, "max_tokens": 18, "max_kosten_mikro": 11_000}[grenze]})
    rufen = (lambda: stt(pclient, g["deviceToken"], wav(61.0))) if anfrage == "stt" else (lambda: chat(pclient, g["deviceToken"]))
    assert rufen().status_code == 200
    assert rufen().status_code == 403


def test_budget_neuer_monat_setzt_zurueck(pein, pclient, firma, uhr):
    f, g = firma
    _budget(pein, f["id"], "geraet", g["deviceId"], max_anfragen=1)
    assert stt(pclient, g["deviceToken"]).status_code == 200
    assert stt(pclient, g["deviceToken"]).status_code == 403
    uhr.vor(32 * TAG)
    assert stt(pclient, g["deviceToken"]).status_code == 200


def test_warnschwelle_und_zustand(pein, pclient, firma):
    f, g = firma
    _budget(pein, f["id"], "firma", max_anfragen=5, warn=60)
    _budget(pein, f["id"], "geraet", g["deviceId"], max_anfragen=10)
    for _ in range(3):
        stt(pclient, g["deviceToken"])
    con = db.verbinden(pein.db_pfad)
    stand = {s["b"]["ebene"]: s for s in budget.stand(con, f["id"], nutzung.monat(pein.uhr()))}
    con.close()
    assert stand["firma"]["zustand"] == "warnung" and stand["firma"]["prozent"] == 60
    assert stand["geraet"]["zustand"] == "ok" and stand["geraet"]["prozent"] == 30


def test_budget_setzen_prueft(pein, pclient, firma):
    f, g = firma
    con = db.verbinden(pein.db_pfad)
    with pytest.raises(budget.BudgetFehler, match="budget_leer"):
        budget.setzen(con, f["id"], "firma", "", {}, 80)
    with pytest.raises(budget.BudgetFehler):
        budget.setzen(con, f["id"], "geraet", "d_fremd", {"max_anfragen": 1}, 80)
    with pytest.raises(budget.BudgetFehler):
        budget.setzen(con, f["id"], "firma", "", {"max_anfragen": 1}, 0)
    con.close()
    for falsch in ("-1", "abc", "1e999", "NaN"):
        with pytest.raises(ValueError):
            nutzung.betrag_lesen(falsch)
    assert nutzung.betrag_lesen("") is None and nutzung.betrag_zeigen(nutzung.betrag_lesen("12,50")) == "12.5"


# --- Portal ---


def test_portal_budget_owner_setzt_admin_nicht(papp, pein, pclient, uhr, firma):
    f, g = firma
    c = admin_client(papp, pein, uhr, f["id"], "owner-a")
    r = c.post("/budget", data={"csrf": seiten_csrf(c, "/budget"), "ebene": "geraet", "geraet": g["deviceId"],
                                "max_anfragen": "1", "max_kosten": "12,50", "warn_prozent": "75"}, follow_redirects=False)
    assert r.headers["location"] == "/budget?ok=gespeichert"
    assert tuple(sql(pein, "SELECT max_anfragen, max_kosten_mikro, warn_prozent FROM budget")[0]) == (1, 12_500_000, 75)
    con = db.verbinden(pein.db_pfad)
    budget.preis_setzen(con, f["id"], "whisper-fest", 0, None, None)  # unter Kostendeckel braucht das Modell einen Preis
    con.close()
    assert stt(pclient, g["deviceToken"]).status_code == 200
    html = c.get("/budget").text
    assert "Erreicht" in html and "<meter" in html
    c.post("/budget/preis", data={"csrf": seiten_csrf(c, "/budget"), "modell": "whisper-fest", "minute": "0,006"})
    assert "whisper-fest" in c.get("/budget").text
    a = admin_client(papp, pein, uhr, f["id"], "admin-b", rolle="admin")
    assert a.get("/budget").status_code == 200
    assert a.post("/budget", data={"csrf": seiten_csrf(a, "/budget"), "ebene": "firma", "max_anfragen": "9"}).status_code == 403
    assert sql(pein, "SELECT COUNT(*) FROM budget WHERE ebene = 'firma'")[0][0] == 0


def test_portal_budget_leer_gibt_hinweis(papp, pein, uhr, firma):
    f, _g = firma
    c = admin_client(papp, pein, uhr, f["id"], "owner-a")
    r = c.post("/budget", data={"csrf": seiten_csrf(c, "/budget"), "ebene": "firma"}, follow_redirects=False)
    assert r.headers["location"] == "/budget?hinweis=budget_leer"
    r = c.post("/budget", data={"csrf": seiten_csrf(c, "/budget"), "ebene": "firma", "max_tokens": "-5"}, follow_redirects=False)
    assert r.headers["location"] == "/budget?hinweis=wert"


@pytest.mark.parametrize("sprache,wort", [("de", "Anfragen"), ("en", "Requests")])
def test_portal_nutzung_und_csv(papp, pein, pclient, uhr, firma, sprache, wort):
    f, g = firma
    geraet_setzen(pein, g["deviceId"], name="=HYPERLINK(\"http://x\")")
    stt(pclient, g["deviceToken"], wav(1.5))
    c = admin_client(papp, pein, uhr, f["id"], "owner-a")
    c.cookies.set("wl_sprache", sprache)
    html = c.get("/nutzung").text
    assert wort in html and nutzung.monat(pein.uhr()) in html
    r = c.get("/nutzung.csv")
    assert r.headers["content-type"].startswith("text/csv") and "attachment" in r.headers["content-disposition"]
    zeilen = list(csv.reader(io.StringIO(r.text.lstrip("﻿"))))
    assert zeilen[0][:6] == ["month", "level", "id", "name", "group", "requests"]
    geraet = next(z for z in zeilen if z[1] == "device")
    assert geraet[3].startswith("'=") and geraet[5] == "1" and geraet[6] == "0.03"
    assert c.get("/nutzung?monat=kaputt").status_code == 200  # ungültiger Monat → aktueller


def test_nutzung_ohne_login_umgeleitet(pein):
    with TestClient(create_app(pein), base_url=BASIS) as c:
        for pfad in ("/nutzung", "/nutzung.csv", "/budget"):
            r = c.get(pfad, follow_redirects=False)
            assert r.status_code == 303 and r.headers["location"] == "/login"


# --- Review N-15 (W2-FIXS): unter Kostendeckel kein Modell ohne Preiseintrag ---
def test_kostendeckel_lehnt_modell_ohne_preis_ab(pein, pclient, fake, firma):
    f, g = firma
    zugang(pein, f["id"], "llm", "custom", fake.basis + "/v1")  # ohne festes Modell: das Gerät wählt frei
    con = db.verbinden(pein.db_pfad)
    budget.preis_setzen(con, f["id"], "text-x", None, nutzung.betrag_lesen("1"), None)
    con.close()
    assert chat(pclient, g["deviceToken"], model="gratis-modell").status_code == 200  # ohne Kostendeckel wie bisher
    _budget(pein, f["id"], "firma", max_kosten_mikro=1_000_000)
    n = len(fake.anfragen)
    r = chat(pclient, g["deviceToken"], model="gratis-modell")
    assert r.status_code == 403 and r.json()["error"] == "budget_exhausted"
    assert len(fake.anfragen) == n  # der Anbieter wurde nicht gefragt
    assert chat(pclient, g["deviceToken"]).status_code == 200  # text-x hat einen Preis
    assert [tuple(z) for z in sql(pein, "SELECT klasse, anzahl FROM nutzung_fehler")] == [("no_price", 1)]


@pytest.mark.parametrize("sprache,wort", [("de", "frisches Geräte-Budget"), ("en", "fresh device budget")])
def test_portal_geraete_budget_hinweis_bei_auto_freigabe(papp, pein, uhr, firma, sprache, wort):
    """Review N-14: unter Auto-Freigabe ist ein Geräte-Budget per Neu-Verbinden umgehbar → Hinweis im Formular."""
    f, _ = firma
    c = admin_client(papp, pein, uhr, f["id"], "owner-n14")
    c.cookies.set("wl_sprache", sprache)
    assert wort not in c.get("/budget").text
    con = db.verbinden(pein.db_pfad)
    con.execute("UPDATE firma SET auto_freigabe = 1 WHERE id = ?", (f["id"],))
    con.close()
    html = c.get("/budget").text
    assert wort in html and ("Preistabelle ab" if sprache == "de" else "without a price table entry") in html


def test_kostendeckel_ersatz_zugang_mit_preis_springt_ein(pein, pclient, fake, fake2, firma):
    f, g = firma
    zugang(pein, f["id"], "llm_fallback", "custom", fake2.basis + "/v1", key="test-key-not-real-2", modell="ersatz-preis")
    con = db.verbinden(pein.db_pfad)
    budget.preis_setzen(con, f["id"], "ersatz-preis", None, nutzung.betrag_lesen("1"), None)
    con.close()
    _budget(pein, f["id"], "geraet", g["deviceId"], max_kosten_mikro=1_000_000)
    n = len(fake.anfragen)
    r = chat(pclient, g["deviceToken"])  # llm-fest hat keinen Preis → Ersatz-Zugang
    assert r.status_code == 200 and len(fake.anfragen) == n and len(fake2.anfragen) == 1
