"""Einstellungs-Katalog gegen docs/settings-format.md (W2-C0): Kürzel-Regel, management.*, eigene Anbieter."""

import pytest

from whisperloom_server import katalog
from whisperloom_server.katalog import Ungueltig


def test_kuerzel_regel_jeder_pfadteil_mit_hotkey():
    # settings-format.md: „jeder Pfad-Teil, der mit hotkey beginnt“ ist nie Fest
    for e in katalog.EINTRAEGE:
        erwartet = any(teil.startswith("hotkey") for teil in e.pfad.split("."))
        assert e.kuerzel == erwartet, e.pfad
        assert ("fixed" in katalog.stufen_fuer(e)) != erwartet, e.pfad


def test_selection_hotkey_combo_heisst_wie_im_vertrag():
    assert "selection.hotkeyCombo" in katalog.NACH_PFAD
    assert "selection.hotkey" not in katalog.NACH_PFAD


def test_management_allow_leave_nur_fest_oder_frei():
    e = katalog.eintrag("management.allowLeave")
    assert katalog.stufen_fuer(e) == ("fixed", "free")
    assert not [p for p in katalog.NACH_PFAD if p.startswith("management.") and p != "management.allowLeave"]


@pytest.mark.parametrize("pfad", ["stt.providerId", "sttFallback.providerId", "llm.providerId", "llmFallback.providerId"])
def test_eigener_anbieter_custom_id(pfad):
    e = katalog.eintrag(pfad)
    assert katalog.wert_pruefen(e, "custom:firmen-whisper") == "custom:firmen-whisper"
    for falsch in ("custom:", "custom:Gross", "custom:" + "x" * 33, "unbekannt"):
        with pytest.raises(Ungueltig):
            katalog.wert_pruefen(e, falsch)


# --- W2-FIXS: Sprache „Deutsch + Englisch“ (de+en, W2-PFIX) im Katalog ---
def test_language_de_en_zulaessig():
    e = katalog.eintrag("language")
    for gut in ("auto", "de", "en", "deu", "de+en"):
        assert katalog.wert_pruefen(e, gut) == gut
    for falsch in ("de+fr", "DE+EN", "de+en+fr", "en+de", "de en", "deutsch"):
        with pytest.raises(Ungueltig):
            katalog.wert_pruefen(e, falsch)


@pytest.mark.parametrize("sprache,anzeige", [("de", "Deutsch + Englisch (DE+EN)"), ("en", "German + English (DE+EN)")])
def test_language_de_en_im_portal(app, einst, uhr, sprache, anzeige):
    from .conftest import admin_client, neue_firma, seiten_csrf, sql

    f = neue_firma(einst, "firma-a")
    admin = admin_client(app, einst, uhr, f["id"], "anna")
    admin.cookies.set("wl_sprache", sprache)
    html = admin.get("/richtlinie").text
    assert 'value="de+en"' in html and anzeige in html  # Vorschlag in der Auswahl
    r = admin.post("/richtlinie", data={"csrf": seiten_csrf(admin, "/richtlinie"), "s:language": "fixed",
                                        "w:language": "de+en"}, follow_redirects=False)
    assert r.status_code == 303
    werte = [tuple(z) for z in sql(einst, "SELECT stufe, wert FROM richtlinie WHERE pfad = 'language'")]
    assert werte == [("fixed", '"de+en"')]
